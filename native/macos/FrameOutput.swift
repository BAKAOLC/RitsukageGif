import Foundation
import ScreenCaptureKit
import CoreMedia
import CoreVideo
import CoreImage
import CoreGraphics

final class FrameOutput: NSObject, SCStreamOutput, SCStreamDelegate {
    let queue = DispatchQueue(label: "RitsukageGif.capture.frames", qos: .userInteractive)
    private var latestSample: CMSampleBuffer?
    private let envelope: CaptureEnvelope
    private let hdr: Bool
    private let colorSpace: CGColorSpace?
    private var color: [String: Any]
    private let lock = NSLock()
    private var geometry: CaptureGeometry
    private var sourceGeometry: CaptureGeometry
    private var configurationId: Int64 = 0
    private var expectedWidth = 0
    private var expectedHeight = 0
    private var outputWidth = 0
    private var outputHeight = 0
    private var suspended = false
    private var intentionallySuspended = false
    private lazy var context = CIContext(options: [.cacheIntermediates: false])

    init(geometry: CaptureGeometry, envelope: CaptureEnvelope, hdr: Bool, colorSpace: CGColorSpace?,
         color: [String: Any]) {
        self.geometry = geometry
        self.sourceGeometry = geometry
        self.envelope = envelope
        self.hdr = hdr
        self.colorSpace = colorSpace
        self.color = color
        super.init()
    }

    func updateSource(_ value: CaptureGeometry, hdrSource: Bool) {
        lock.lock()
        sourceGeometry = value
        color["hdr"] = hdrSource
        lock.unlock()
    }

    func configure(_ value: CaptureGeometry, source: CaptureGeometry, width: Int, height: Int,
                   inputWidth: Int, inputHeight: Int, id: Int64) {
        lock.lock()
        geometry = value
        sourceGeometry = source
        outputWidth = width
        outputHeight = height
        expectedWidth = inputWidth
        expectedHeight = inputHeight
        configurationId = id
        lock.unlock()
        queue.async { [self] in
            if let sample = latestSample { process(sample) }
        }
    }

    func setSuspended(_ value: Bool) {
        lock.lock(); intentionallySuspended = value; lock.unlock()
    }

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        CaptureFailure.terminate(error)
    }

    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of outputType: SCStreamOutputType) {
        guard outputType == .screen else { return }
        process(sampleBuffer)
    }

    private func process(_ sampleBuffer: CMSampleBuffer) {
        lock.lock(); let ignore = intentionallySuspended; lock.unlock()
        if ignore { return }
        guard sampleBuffer.isValid,
              let attachments = CMSampleBufferGetSampleAttachmentsArray(sampleBuffer, createIfNecessary: false)
                as? [[SCStreamFrameInfo: Any]],
              let statusValue = attachments.first?[.status] as? Int,
              let status = SCFrameStatus(rawValue: statusValue) else { return }
        if status == .idle { return }
        guard status == .complete else {
            if !suspended {
                suspended = true
                do { try envelope.capabilities(["reason": "source-suspended"]) }
                catch { CaptureFailure.terminate(error) }
            }
            return
        }
        suspended = false
        latestSample = sampleBuffer
        guard let buffer = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }
        let width = CVPixelBufferGetWidth(buffer)
        let height = CVPixelBufferGetHeight(buffer)
        let bytesPerPixel = hdr ? 8 : 4
        guard width > 0, height > 0, width <= 32768, height <= 32768,
              width * height * bytesPerPixel <= 512 * 1024 * 1024 else {
            CaptureFailure.terminate(CaptureFailure.captureFailed)
        }
        lock.lock()
        let current = geometry
        let currentColor = color
        let fullSource = sourceGeometry
        let currentConfiguration = configurationId
        let configuredWidth = expectedWidth
        let configuredHeight = expectedHeight
        let targetWidth = outputWidth > 0 ? outputWidth : width
        let targetHeight = outputHeight > 0 ? outputHeight : height
        lock.unlock()
        if configuredWidth > 0 && (width != configuredWidth || height != configuredHeight) { return }
        guard targetWidth > 0, targetHeight > 0, targetWidth <= 32768, targetHeight <= 32768,
              targetWidth * targetHeight * bytesPerPixel <= 512 * 1024 * 1024 else {
            CaptureFailure.terminate(CaptureFailure.captureFailed)
        }
        var pixels = Data(count: targetWidth * targetHeight * bytesPerPixel)
        let cropSource = current.bounds != fullSource.bounds || targetWidth != width || targetHeight != height
        if hdr || cropSource {
            let destinationSpace = hdr ? colorSpace : CGColorSpace(name: CGColorSpace.sRGB)
            guard let destinationSpace else { CaptureFailure.terminate(CaptureFailure.captureFailed) }
            var image = CIImage(cvPixelBuffer: buffer)
            if cropSource {
                // Keep complete GPU surfaces so queued frames cannot carry a stale hardware crop.
                // Only the current ROI is read into CPU memory, for displays and windows alike.
                let crop = CGRect(x: (current.bounds.minX - fullSource.bounds.minX) * CGFloat(width) / fullSource.bounds.width,
                                  y: (fullSource.bounds.maxY - current.bounds.maxY) * CGFloat(height) / fullSource.bounds.height,
                                  width: current.bounds.width * CGFloat(width) / fullSource.bounds.width,
                                  height: current.bounds.height * CGFloat(height) / fullSource.bounds.height)
                image = image.cropped(to: crop)
                    .transformed(by: CGAffineTransform(translationX: -crop.minX, y: -crop.minY))
                    .transformed(by: CGAffineTransform(scaleX: CGFloat(targetWidth) / crop.width,
                                                      y: CGFloat(targetHeight) / crop.height))
            }
            pixels.withUnsafeMutableBytes { raw in
                guard let destination = raw.baseAddress else { return }
                context.render(image, toBitmap: destination, rowBytes: targetWidth * bytesPerPixel,
                               bounds: CGRect(x: 0, y: 0, width: CGFloat(targetWidth), height: CGFloat(targetHeight)),
                               format: hdr ? .RGBAh : .BGRA8, colorSpace: destinationSpace)
            }
        } else {
            guard CVPixelBufferGetPixelFormatType(buffer) == kCVPixelFormatType_32BGRA,
                  CVPixelBufferLockBaseAddress(buffer, .readOnly) == kCVReturnSuccess else {
                CaptureFailure.terminate(CaptureFailure.captureFailed)
            }
            defer { CVPixelBufferUnlockBaseAddress(buffer, .readOnly) }
            guard let source = CVPixelBufferGetBaseAddress(buffer) else { return }
            let stride = CVPixelBufferGetBytesPerRow(buffer)
            guard stride >= width * 4 else { CaptureFailure.terminate(CaptureFailure.captureFailed) }
            pixels.withUnsafeMutableBytes { raw in
                guard let destination = raw.baseAddress else { return }
                for row in 0..<height {
                    memcpy(destination.advanced(by: row * width * 4), source.advanced(by: row * stride), width * 4)
                }
            }
        }
        if !hdr {
            pixels.withUnsafeMutableBytes { raw in
                let values = raw.bindMemory(to: UInt8.self)
                for offset in Swift.stride(from: 0, to: values.count, by: 4) {
                    let alpha = Int(values[offset + 3])
                    if alpha == 0 { values[offset] = 0; values[offset + 1] = 0; values[offset + 2] = 0 }
                    else if alpha < 255 {
                        for channel in 0..<3 {
                            values[offset + channel] = UInt8(min(255, (Int(values[offset + channel]) * 255 + alpha / 2) / alpha))
                        }
                    }
                }
            }
        }
        let plane: [String: Any] = ["width": targetWidth, "height": targetHeight, "stride": targetWidth * bytesPerPixel,
                                   "format": hdr ? "rgba16f" : "bgra8",
                                   "alpha": hdr ? "premultiplied" : "straight", "color": currentColor]
        let metadata: [String: Any] = ["configurationId": currentConfiguration,
                                     "sourceGeometry": fullSource.metadata(width: Int((fullSource.bounds.width * fullSource.backingScale).rounded(.up)),
                                                                            height: Int((fullSource.bounds.height * fullSource.backingScale).rounded(.up))),
                                     "timestampNanoseconds": DispatchTime.now().uptimeNanoseconds,
                                      "geometry": current.metadata(width: targetWidth, height: targetHeight),
                                      hdr ? "hdr" : "sdr": plane]
        do { try envelope.frame(metadata, sdr: hdr ? nil : pixels, hdr: hdr ? pixels : nil, cursor: nil) }
        catch { CaptureFailure.terminate(error) }
    }
}
