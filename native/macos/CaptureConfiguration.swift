import Foundation
import ScreenCaptureKit
import CoreGraphics

@MainActor
final class CaptureConfiguration {
    private let stream: SCStream
    private let configuration: SCStreamConfiguration
    private let output: FrameOutput
    private let envelope: CaptureEnvelope
    private var source: CaptureGeometry
    private var relativeCrop: CGRect?
    private var scaleX: CGFloat?
    private var scaleY: CGFloat?
    private var configurationId: Int64 = 0
    private var appliedBounds: CGRect?
    private var appliedWidth = 0
    private var appliedHeight = 0
    private var appliedInputWidth = 0
    private var appliedInputHeight = 0
    private(set) var stopped = false
    private(set) var suspended = false

    init(stream: SCStream, configuration: SCStreamConfiguration, output: FrameOutput,
         envelope: CaptureEnvelope, source: CaptureGeometry) {
        self.stream = stream
        self.configuration = configuration
        self.output = output
        self.envelope = envelope
        self.source = source
    }

    func handle(_ request: CaptureRequest) async {
        do {
            switch request.command {
            case "stop": stopped = true
            case "suspend":
                if !suspended { output.setSuspended(true); try await stream.stopCapture(); suspended = true }
                try reply(request.requestId, "suspended", "")
            case "resume":
                if suspended { output.setSuspended(false); try await stream.startCapture(); suspended = false }
                try reply(request.requestId, "resumed", "")
            case "cursor":
                let cursor = CursorSnapshot.capture(bitmapId: UInt32(truncatingIfNeeded: request.requestId))
                try envelope.cursor(["requestId": request.requestId, "cursor": cursor.metadata], pixels: cursor.pixels)
            case "capabilities":
                try envelope.capabilities(["requestId": request.requestId,
                    "sourceGeometry": source.metadata(width: Int((source.bounds.width * source.backingScale).rounded(.up)),
                                                       height: Int((source.bounds.height * source.backingScale).rounded(.up)))])
            case "configure":
                guard request.generation == source.generation, request.streamId == source.sourceId,
                      let region = request.region, let width = request.outputWidth, let height = request.outputHeight,
                      region.width > 0, region.height > 0, width > 0, height > 0,
                      width <= 32768, height <= 32768, Int64(width) * Int64(height) * 8 <= 512 * 1024 * 1024 else {
                    try reply(request.requestId, "unavailable", "The capture configuration or source identity is invalid.")
                    return
                }
                let requested = CGRect(x: CGFloat(region.x), y: CGFloat(region.y), width: CGFloat(region.width), height: CGFloat(region.height))
                guard !requested.intersection(source.bounds).isEmpty else {
                    try reply(request.requestId, "unavailable", "The capture region is outside the source.")
                    return
                }
                relativeCrop = requested.offsetBy(dx: -source.bounds.minX, dy: -source.bounds.minY)
                scaleX = CGFloat(width) / requested.width
                scaleY = CGFloat(height) / requested.height
                configurationId = request.requestId
                try await apply(force: true)
                try reply(request.requestId, "configured", "")
            default: try reply(request.requestId, "unavailable", "The capture command is unsupported.")
            }
        } catch {
            do { try reply(request.requestId, "failed", error.localizedDescription) }
            catch { CaptureFailure.terminate(error) }
        }
    }

    func refresh(_ geometry: CaptureGeometry, hdrSource: Bool) async throws {
        source = geometry
        output.updateSource(geometry, hdrSource: hdrSource)
        try await apply(force: false)
    }

    private func apply(force: Bool) async throws {
        let requested = relativeCrop?.offsetBy(dx: source.bounds.minX, dy: source.bounds.minY) ?? source.bounds
        let bounds = requested.intersection(source.bounds)
        guard !bounds.isEmpty, !bounds.isNull else { throw CaptureFailure.sourceLost }
        let width = max(1, Int((bounds.width * (scaleX ?? source.backingScale)).rounded(.up)))
        let height = max(1, Int((bounds.height * (scaleY ?? source.backingScale)).rounded(.up)))
        guard width <= 32768, height <= 32768, Int64(width) * Int64(height) * 8 <= 512 * 1024 * 1024 else {
            throw CaptureFailure.captureFailed
        }
        var geometry = source
        geometry.bounds = bounds
        let inputWidth = max(1, Int((source.bounds.width * min(scaleX ?? source.backingScale, source.backingScale)).rounded(.up)))
        let inputHeight = max(1, Int((source.bounds.height * min(scaleY ?? source.backingScale, source.backingScale)).rounded(.up)))
        guard inputWidth <= 32768, inputHeight <= 32768 else { throw CaptureFailure.captureFailed }
        if force || appliedBounds != bounds || appliedWidth != width || appliedHeight != height ||
            appliedInputWidth != inputWidth || appliedInputHeight != inputHeight {
            configuration.width = inputWidth
            configuration.height = inputHeight
            try await stream.updateConfiguration(configuration)
            output.configure(geometry, source: source, width: width, height: height,
                             inputWidth: inputWidth, inputHeight: inputHeight, id: configurationId)
            appliedBounds = bounds; appliedWidth = width; appliedHeight = height
            appliedInputWidth = inputWidth; appliedInputHeight = inputHeight
        }
    }

    private func reply(_ request: Int64, _ result: String, _ reason: String) throws {
        try envelope.reply(["requestId": request, "result": result, "reason": reason])
    }
}
