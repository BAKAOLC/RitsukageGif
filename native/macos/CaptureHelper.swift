import Foundation
import AppKit
import ScreenCaptureKit
import CoreMedia
import CoreVideo
import CoreGraphics
import Darwin

@main
struct CaptureHelper {
    @MainActor
    static func main() async {
        signal(SIGPIPE, SIG_IGN)
        do {
            let options = try CaptureOptions(Array(CommandLine.arguments.dropFirst()))
            switch options.mode {
            case "permission", "request-permission":
                let granted = options.mode == "permission" ? CGPreflightScreenCaptureAccess() : CGRequestScreenCaptureAccess()
                try FileHandle.standardOutput.write(contentsOf: Data((granted ? "true\n" : "false\n").utf8))
                exit(granted ? 0 : 77)
            case "windows":
                try WindowCatalog.write()
                return
            case "displays":
                try writeJSON(CaptureGeometry.displays())
                return
            case "cursor":
                let cursor = CursorSnapshot.capture(bitmapId: 1)
                try CaptureEnvelope().cursor(["cursor": cursor.metadata], pixels: cursor.pixels)
                return
            case "window-server":
                while let request = readLine() {
                    if request == "windows" {
                        try WindowCatalog.write()
                    } else if request == "displays" {
                        try writeJSON(CaptureGeometry.displays())
                    } else if request == "control-capability" || request == "request-control-permission" {
                        try writeJSON(AccessibilityCatalog.capability(request: request == "request-control-permission"))
                    } else if request.hasPrefix("controls ") {
                        let fields = request.split(separator: " ")
                        guard fields.count == 4, let id = UInt32(fields[1]), let x = Float(fields[2]), let y = Float(fields[3]),
                              x.isFinite, y.isFinite else { throw CaptureFailure.invalidArguments }
                        try writeJSON(AccessibilityCatalog.hierarchy(window: id, x: x, y: y))
                    } else if request.hasPrefix("window-state "), let id = UInt32(request.dropFirst(13)) {
                        try WindowCatalog.writeState(of: id)
                    } else {
                        throw CaptureFailure.invalidArguments
                    }
                }
                return
            default:
                guard CGPreflightScreenCaptureAccess() || CGRequestScreenCaptureAccess() else {
                    throw CaptureFailure.permissionDenied
                }
                try await capture(options)
            }
        } catch {
            CaptureFailure.terminate(error)
        }
    }

    private static func writeJSON(_ value: Any) throws {
        var output = try JSONSerialization.data(withJSONObject: value, options: [.sortedKeys])
        output.append(10)
        try FileHandle.standardOutput.write(contentsOf: output)
    }

    @MainActor
    private static func capture(_ options: CaptureOptions) async throws {
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
        let filter: SCContentFilter
        var bounds: CGRect
        let displayId: CGDirectDisplayID
        let sourceId: String
        if options.mode == "window" {
            guard let window = content.windows.first(where: { $0.windowID == options.id }) else {
                throw CaptureFailure.sourceLost
            }
            filter = SCContentFilter(desktopIndependentWindow: window)
            bounds = window.frame
            displayId = content.displays.max { overlap($0.frame, bounds) < overlap($1.frame, bounds) }?.displayID ?? CGMainDisplayID()
            sourceId = "window:\(window.windowID):\(window.owningApplication?.processID ?? 0)"
        } else {
            let requested = options.id ?? CGMainDisplayID()
            guard let display = content.displays.first(where: { $0.displayID == requested }) else {
                throw CaptureFailure.sourceLost
            }
            let excluded = content.applications.filter { $0.processID == getppid() || $0.processID == getpid() }
            filter = SCContentFilter(display: display, excludingApplications: excluded, exceptingWindows: [])
            bounds = CGDisplayBounds(display.displayID)
            displayId = display.displayID
            sourceId = "display:\(displayId)"
        }
        var geometry = CaptureGeometry(sourceId: sourceId, displayId: displayId, bounds: bounds,
                                       backingScale: CaptureGeometry.displayScale(displayId), generation: options.generation)
        let configuration: SCStreamConfiguration
        var hdr = false
        #if arch(arm64)
        if #available(macOS 15.0, *), options.hdr {
            configuration = SCStreamConfiguration(preset: .captureHDRStreamCanonicalDisplay)
            hdr = true
        } else {
            configuration = SCStreamConfiguration()
        }
        #else
        configuration = SCStreamConfiguration()
        #endif
        var color: [String: Any] = ["hdr": false, "primaries": "bt709", "transfer": "srgb",
                                    "sdrWhiteNits": 80, "linearReferenceNits": 80, "peakNits": 1000]
        var outputSpace: CGColorSpace?
        if hdr {
            let name = configuration.colorSpaceName
            guard let space = CGColorSpace(name: name) else {
                throw CaptureFailure.captureFailed
            }
            let identifier = name as String
            let transfer: String
            if identifier.localizedCaseInsensitiveContains("HLG") { transfer = "hlg" }
            else if identifier.localizedCaseInsensitiveContains("PQ") { transfer = "pq" }
            else { throw CaptureFailure.captureFailed }
            color = ["hdr": CaptureGeometry.displayHdr(displayId), "primaries": identifier.localizedCaseInsensitiveContains("P3") ? "display-p3" : "bt2020",
                     "transfer": transfer, "sdrWhiteNits": 203, "linearReferenceNits": 80, "peakNits": 1000]
            outputSpace = space
        } else {
            configuration.pixelFormat = kCVPixelFormatType_32BGRA
            configuration.colorSpaceName = CGColorSpace.sRGB
        }
        configuration.width = Int((bounds.width * geometry.backingScale).rounded(.up))
        configuration.height = Int((bounds.height * geometry.backingScale).rounded(.up))
        guard configuration.width > 0, configuration.height > 0,
              configuration.width <= 32768, configuration.height <= 32768 else { throw CaptureFailure.captureFailed }
        configuration.showsCursor = false
        configuration.minimumFrameInterval = CMTime(value: 1, timescale: options.fps)
        configuration.queueDepth = 3
        configuration.capturesAudio = false
        configuration.scalesToFit = true
        if #available(macOS 14.0, *) { configuration.ignoreShadowsSingleWindow = true }
        let envelope = CaptureEnvelope()
        try envelope.capabilities(["geometry": geometry.metadata(width: configuration.width, height: configuration.height),
                                   "capabilities": ["screen": true, "window": true, "hdr": hdr,
                                                    "separateCursor": true, "canRequestPointerInput": true,
                                                    "reason": options.hdr && !hdr ? "hdr-platform-unavailable" : ""]])
        let output = FrameOutput(geometry: geometry, envelope: envelope, hdr: hdr, colorSpace: outputSpace, color: color)
        let stream = SCStream(filter: filter, configuration: configuration, delegate: output)
        try stream.addStreamOutput(output, type: .screen,
                                   sampleHandlerQueue: output.queue)
        try await stream.startCapture()
        let control = CaptureConfiguration(stream: stream, configuration: configuration, output: output,
                                           envelope: envelope, source: geometry)
        let cursorUpdates = Task { @MainActor in
            var bitmapId: UInt32 = 0
            while !Task.isCancelled && !control.stopped {
                if !control.suspended {
                    bitmapId &+= 1
                    let cursor = CursorSnapshot.capture(bitmapId: bitmapId, reuseBitmap: true)
                    do { try envelope.cursor(["cursor": cursor.metadata], pixels: cursor.pixels) }
                    catch { CaptureFailure.terminate(error) }
                }
                do { try await Task.sleep(nanoseconds: 1_000_000_000 / UInt64(options.fps)) }
                catch { return }
            }
        }
        let commands = Task.detached {
            while let line = readLine() {
                do {
                    let request = try JSONDecoder().decode(CaptureRequest.self, from: Data(line.utf8))
                    await control.handle(request)
                    if request.command == "stop" { return }
                } catch { CaptureFailure.terminate(error) }
            }
            await control.handle(CaptureRequest(command: "stop", requestId: 0, generation: nil,
                streamId: nil, region: nil, outputWidth: nil, outputHeight: nil))
        }
        let parent = getppid()
        while !Task.isCancelled && !control.stopped {
            try await Task.sleep(nanoseconds: 100_000_000)
            if getppid() != parent || kill(parent, 0) != 0 { break }
            if options.mode == "window", let id = options.id {
                guard let current = WindowCatalog.bounds(of: id) else { throw CaptureFailure.sourceLost }
                bounds = current
                geometry.displayId = content.displays.max { overlap($0.frame, current) < overlap($1.frame, current) }?.displayID ?? displayId
            } else {
                guard CGDisplayIsActive(displayId) != 0 else { throw CaptureFailure.sourceLost }
                bounds = CGDisplayBounds(displayId)
            }
            geometry.bounds = bounds
            geometry.backingScale = CaptureGeometry.displayScale(geometry.displayId)
            try await control.refresh(geometry, hdrSource: hdr && CaptureGeometry.displayHdr(geometry.displayId))
        }
        try await stream.stopCapture()
        commands.cancel()
        cursorUpdates.cancel()
        withExtendedLifetime(output) { }
    }

    private static func overlap(_ a: CGRect, _ b: CGRect) -> CGFloat {
        let value = a.intersection(b)
        return value.isNull ? 0 : value.width * value.height
    }
}
