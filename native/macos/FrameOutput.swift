import Foundation
import ScreenCaptureKit
import CoreMedia
import CoreVideo
import Darwin

final class FrameOutput: NSObject, SCStreamOutput, SCStreamDelegate {
    private let originX: Int32
    private let originY: Int32
    private let lock = NSLock()
    private let outputQueue = DispatchQueue(label: "RitsukageGif.capture.output", qos: .userInteractive)
    private var pending: Data?
    private var writing = false
    private var lastWidth = 0
    private var lastHeight = 0
    private var transparentFrame: Data?

    init(originX: Int32, originY: Int32) {
        self.originX = originX
        self.originY = originY
        super.init()
    }

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        CaptureFailure.terminate(error)
    }

    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of outputType: SCStreamOutputType) {
        guard outputType == .screen, sampleBuffer.isValid,
              let attachments = CMSampleBufferGetSampleAttachmentsArray(sampleBuffer, createIfNecessary: false)
                as? [[SCStreamFrameInfo: Any]],
              let statusValue = attachments.first?[.status] as? Int,
              let status = SCFrameStatus(rawValue: statusValue), status != .idle else { return }
        if status != .complete {
            if let buffer = sampleBuffer.imageBuffer {
                updateSize(width: CVPixelBufferGetWidth(buffer), height: CVPixelBufferGetHeight(buffer))
            }
            guard lastWidth > 0 && lastHeight > 0 else { return }
            if transparentFrame == nil { transparentFrame = makeFrame(width: lastWidth, height: lastHeight) }
            if let transparentFrame { enqueue(transparentFrame) }
            return
        }
        guard let buffer = sampleBuffer.imageBuffer else {
            CaptureFailure.terminate(CaptureFailure.captureFailed)
        }
        let width = CVPixelBufferGetWidth(buffer)
        let height = CVPixelBufferGetHeight(buffer)
        updateSize(width: width, height: height)
        guard CVPixelBufferGetPixelFormatType(buffer) == kCVPixelFormatType_32BGRA else {
            CaptureFailure.terminate(CaptureFailure.captureFailed)
        }
        let byteCount = width * height * 4
        var frame = makeFrame(width: width, height: height)
        do {
            guard CVPixelBufferLockBaseAddress(buffer, .readOnly) == kCVReturnSuccess else { return }
            defer { CVPixelBufferUnlockBaseAddress(buffer, .readOnly) }
            guard let source = CVPixelBufferGetBaseAddress(buffer) else { return }
            let rowStride = CVPixelBufferGetBytesPerRow(buffer)
            guard rowStride >= width * 4 else { return }
            frame.withUnsafeMutableBytes { raw in
                guard let destination = raw.baseAddress?.advanced(by: 24) else { return }
                for row in 0..<height {
                    memcpy(destination.advanced(by: row * width * 4), source.advanced(by: row * rowStride), width * 4)
                }
                let pixels = destination.assumingMemoryBound(to: UInt8.self)
                for index in stride(from: 0, to: byteCount, by: 4) {
                    let alpha = Int(pixels[index + 3])
                    if alpha > 0 && alpha < 255 {
                        for channel in 0..<3 {
                            pixels[index + channel] = UInt8(min(255, (Int(pixels[index + channel]) * 255 + alpha / 2) / alpha))
                        }
                    } else if alpha == 0 {
                        pixels[index] = 0
                        pixels[index + 1] = 0
                        pixels[index + 2] = 0
                    }
                }
            }
        }
        enqueue(frame)
    }

    private func updateSize(width: Int, height: Int) {
        guard width > 0, height > 0, width <= 32768, height <= 32768,
              width * height <= 128 * 1024 * 1024 else {
            CaptureFailure.terminate(CaptureFailure.captureFailed)
        }
        if lastWidth != width || lastHeight != height {
            lastWidth = width
            lastHeight = height
            transparentFrame = nil
        }
    }

    private func makeFrame(width: Int, height: Int) -> Data {
        let byteCount = width * height * 4
        var frame = Data(count: 24 + byteCount)
        frame.withUnsafeMutableBytes { raw in
            let header = [UInt32(0x46434752), UInt32(width), UInt32(height), UInt32(bitPattern: originX),
                          UInt32(bitPattern: originY), UInt32(byteCount)]
            for (index, integer) in header.enumerated() {
                var littleEndian = integer.littleEndian
                withUnsafeBytes(of: &littleEndian) { source in
                    raw.baseAddress!.advanced(by: index * 4).copyMemory(from: source.baseAddress!, byteCount: 4)
                }
            }
        }
        return frame
    }

    private func enqueue(_ frame: Data) {
        lock.lock()
        pending = frame
        let start = !writing
        writing = true
        lock.unlock()
        if start { outputQueue.async { self.drain() } }
    }

    private func drain() {
        while true {
            lock.lock()
            guard let frame = pending else {
                writing = false
                lock.unlock()
                return
            }
            pending = nil
            lock.unlock()
            frame.withUnsafeBytes { raw in
                var offset = 0
                while offset < raw.count {
                    let count = Darwin.write(STDOUT_FILENO, raw.baseAddress!.advanced(by: offset), raw.count - offset)
                    if count < 0 && errno == EINTR { continue }
                    if count <= 0 { exit(errno == EPIPE ? 0 : 1) }
                    offset += count
                }
            }
        }
    }
}
