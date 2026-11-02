import AppKit
import CoreGraphics
import Foundation

struct CaptureGeometry {
    let sourceId: String
    var displayId: CGDirectDisplayID
    var bounds: CGRect
    var backingScale: CGFloat
    let generation: Int64

    func metadata(width: Int, height: Int) -> [String: Any] {
        ["sourceId": sourceId, "streamId": sourceId, "stableIdentity": true, "generation": generation, "space": "desktop",
         "displayId": displayId, "x": Int(bounds.minX.rounded(.down)), "y": Int(bounds.minY.rounded(.down)),
         "width": Int(bounds.width.rounded(.up)), "height": Int(bounds.height.rounded(.up)),
         "pixelWidth": width, "pixelHeight": height, "backingScale": Double(backingScale)]
    }

    static func displayScale(_ id: CGDirectDisplayID) -> CGFloat {
        guard let mode = CGDisplayCopyDisplayMode(id), mode.width > 0 else { return 1 }
        return CGFloat(mode.pixelWidth) / CGFloat(mode.width)
    }

    @MainActor
    static func displayHdr(_ id: CGDirectDisplayID) -> Bool {
        guard let screen = NSScreen.screens.first(where: {
            ($0.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.uint32Value == id
        }) else { return false }
        return screen.maximumPotentialExtendedDynamicRangeColorComponentValue > 1
    }

    static func displays() throws -> [[String: Any]] {
        var count: UInt32 = 0
        guard CGGetActiveDisplayList(0, nil, &count) == .success else { throw CaptureFailure.captureFailed }
        var ids = [CGDirectDisplayID](repeating: 0, count: Int(count))
        guard CGGetActiveDisplayList(count, &ids, &count) == .success else { throw CaptureFailure.captureFailed }
        return ids.prefix(Int(count)).map { id in
            let logical = CGDisplayBounds(id)
            let mode = CGDisplayCopyDisplayMode(id)
            return ["displayId": id, "x": Int(logical.minX.rounded(.down)), "y": Int(logical.minY.rounded(.down)),
                    "width": Int(logical.width.rounded(.up)), "height": Int(logical.height.rounded(.up)),
                    "pixelWidth": mode?.pixelWidth ?? Int(logical.width),
                    "pixelHeight": mode?.pixelHeight ?? Int(logical.height),
                    "backingScale": Double(displayScale(id)), "primary": CGDisplayIsMain(id) != 0]
        }
    }
}
