import Foundation
import ScreenCaptureKit
import CoreMedia
import CoreVideo
import CoreGraphics
import Darwin

@main
struct CaptureHelper {
    static func main() async {
        signal(SIGPIPE, SIG_IGN)
        do {
            let options = try CaptureOptions(Array(CommandLine.arguments.dropFirst()))
            if options.mode == "permission" || options.mode == "request-permission" {
                let granted = options.mode == "permission" ? CGPreflightScreenCaptureAccess() : CGRequestScreenCaptureAccess()
                FileHandle.standardOutput.write(Data((granted ? "true\n" : "false\n").utf8))
                exit(granted ? 0 : 77)
            }
            if options.mode == "windows" {
                try WindowCatalog.write()
                return
            }
            if options.mode == "window-server" {
                while let request = readLine() {
                    guard request == "windows" else { throw CaptureFailure.invalidArguments }
                    try WindowCatalog.write()
                }
                return
            }
            guard CGPreflightScreenCaptureAccess() || CGRequestScreenCaptureAccess() else {
                throw CaptureFailure.permissionDenied
            }
            try await capture(options)
        } catch {
            CaptureFailure.terminate(error)
        }
    }

    private static func capture(_ options: CaptureOptions) async throws {
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
        let filter: SCContentFilter
        let bounds: CGRect
        let scale: CGFloat
        let origin: CGPoint
        if options.mode == "window" {
            guard let window = content.windows.first(where: { $0.windowID == options.id }) else {
                throw CaptureFailure.sourceLost
            }
            filter = SCContentFilter(desktopIndependentWindow: window)
            bounds = window.frame
            let display = content.displays.max { intersection($0.frame, bounds) < intersection($1.frame, bounds) }
            scale = display.map { displayScale($0.displayID) } ?? 1
            origin = .zero
        } else {
            let display: SCDisplay?
            if let id = options.id {
                display = content.displays.first { $0.displayID == id }
            } else if let requested = options.bounds {
                display = content.displays.first { approximatelyEqual(CGDisplayBounds($0.displayID), requested) }
            } else {
                display = content.displays.first { $0.displayID == CGMainDisplayID() }
            }
            guard let display else { throw CaptureFailure.sourceLost }
            filter = SCContentFilter(display: display, excludingWindows: [])
            bounds = CGDisplayBounds(display.displayID)
            scale = displayScale(display.displayID)
            origin = options.bounds?.origin ?? bounds.origin
        }
        let width = Int((bounds.width * scale).rounded(.up))
        let height = Int((bounds.height * scale).rounded(.up))
        guard width > 0, height > 0, width <= 32768, height <= 32768, width * height <= 128 * 1024 * 1024,
              origin.x >= CGFloat(Int32.min), origin.x <= CGFloat(Int32.max),
              origin.y >= CGFloat(Int32.min), origin.y <= CGFloat(Int32.max) else {
            throw CaptureFailure.captureFailed
        }
        let configuration = SCStreamConfiguration()
        configuration.width = width
        configuration.height = height
        configuration.pixelFormat = kCVPixelFormatType_32BGRA
        configuration.colorSpaceName = CGColorSpace.sRGB
        configuration.showsCursor = options.cursor
        configuration.minimumFrameInterval = CMTime(value: 1, timescale: options.fps)
        configuration.queueDepth = 3
        configuration.capturesAudio = false
        configuration.scalesToFit = true
        if #available(macOS 14.0, *) { configuration.ignoreShadowsSingleWindow = true }
        let output = FrameOutput(originX: Int32(origin.x), originY: Int32(origin.y))
        let stream = SCStream(filter: filter, configuration: configuration, delegate: output)
        try stream.addStreamOutput(output, type: .screen,
                                   sampleHandlerQueue: DispatchQueue(label: "RitsukageGif.capture.frames", qos: .userInteractive))
        try await stream.startCapture()
        let parent = getppid()
        while !Task.isCancelled {
            try await Task.sleep(nanoseconds: 100_000_000)
            if getppid() != parent || kill(parent, 0) != 0 { break }
            if options.mode == "window", let id = options.id {
                guard let current = WindowCatalog.bounds(of: id) else { throw CaptureFailure.sourceLost }
                let display = content.displays.max { intersection($0.frame, current) < intersection($1.frame, current) }
                let currentScale = display.map { displayScale($0.displayID) } ?? scale
                let currentWidth = Int((current.width * currentScale).rounded(.up))
                let currentHeight = Int((current.height * currentScale).rounded(.up))
                guard currentWidth > 0, currentHeight > 0, currentWidth <= 32768, currentHeight <= 32768,
                      currentWidth * currentHeight <= 128 * 1024 * 1024 else { throw CaptureFailure.sourceLost }
                if configuration.width != currentWidth || configuration.height != currentHeight {
                    configuration.width = currentWidth
                    configuration.height = currentHeight
                    try await stream.updateConfiguration(configuration)
                }
            }
        }
        try await stream.stopCapture()
        withExtendedLifetime(output) { }
    }

    private static func displayScale(_ id: CGDirectDisplayID) -> CGFloat {
        guard let mode = CGDisplayCopyDisplayMode(id), mode.width > 0 else { return 1 }
        return CGFloat(mode.pixelWidth) / CGFloat(mode.width)
    }

    private static func approximatelyEqual(_ a: CGRect, _ b: CGRect) -> Bool {
        abs(a.minX - b.minX) <= 1 && abs(a.minY - b.minY) <= 1 && abs(a.width - b.width) <= 1 && abs(a.height - b.height) <= 1
    }

    private static func intersection(_ a: CGRect, _ b: CGRect) -> CGFloat {
        let overlap = a.intersection(b)
        return overlap.isNull ? 0 : overlap.width * overlap.height
    }
}
