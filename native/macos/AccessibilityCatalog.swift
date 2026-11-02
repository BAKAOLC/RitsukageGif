import Foundation
import ApplicationServices
import CoreGraphics

// Queries are scoped to the selected application so the capture overlay cannot intercept hit testing.
enum AccessibilityCatalog {
    static func capability(request: Bool) -> [String: Any] {
        let trusted = request ? AXIsProcessTrustedWithOptions([kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary) : AXIsProcessTrusted()
        return ["available": trusted, "reason": trusted ? "" : "accessibility-permission-required"]
    }

    static func hierarchy(window: CGWindowID, x: Float, y: Float) -> [String: Any] {
        guard AXIsProcessTrusted() else { return ["available": false, "reason": "accessibility-permission-required", "controls": []] }
        guard let items = CGWindowListCopyWindowInfo(.optionIncludingWindow, window) as? [[String: Any]],
              let item = items.first,
              let process = item[kCGWindowOwnerPID as String] as? NSNumber,
              let dictionary = item[kCGWindowBounds as String] as? [String: Any],
              let windowBounds = CGRect(dictionaryRepresentation: dictionary as CFDictionary) else {
            return ["available": true, "reason": "source-lost", "controls": []]
        }
        let application = AXUIElementCreateApplication(process.int32Value)
        AXUIElementSetMessagingTimeout(application, 0.15)
        var hit: AXUIElement?
        let status = AXUIElementCopyElementAtPosition(application, x, y, &hit)
        guard status == .success, var current = hit else {
            return ["available": true, "reason": status == .cannotComplete ? "accessibility-provider-timeout" : "", "controls": []]
        }
        let started = DispatchTime.now().uptimeNanoseconds
        var entries: [[String: Int]] = []
        var visited: [AXUIElement] = []
        let point = CGPoint(x: CGFloat(x), y: CGFloat(y))
        for _ in 0..<64 {
            if DispatchTime.now().uptimeNanoseconds - started > 200_000_000 { break }
            if visited.contains(where: { CFEqual($0, current) }) { break }
            visited.append(current)
            var pid: pid_t = 0
            if AXUIElementGetPid(current, &pid) != .success || pid != process.int32Value { break }
            if let frame = frame(current) {
                let clipped = frame.intersection(windowBounds)
                if !clipped.isNull && !clipped.isEmpty && clipped.contains(point) {
                    entries.append(["x": Int(clipped.minX.rounded(.down)), "y": Int(clipped.minY.rounded(.down)),
                                    "width": Int(clipped.width.rounded(.up)), "height": Int(clipped.height.rounded(.up))])
                }
            }
            var parent: CFTypeRef?
            guard AXUIElementCopyAttributeValue(current, kAXParentAttribute as CFString, &parent) == .success,
                  let parent, CFGetTypeID(parent) == AXUIElementGetTypeID() else { break }
            current = unsafeBitCast(parent, to: AXUIElement.self)
        }
        return ["available": true, "reason": "", "controls": entries.reversed().map { $0 }]
    }

    private static func frame(_ element: AXUIElement) -> CGRect? {
        var position: CFTypeRef?
        var size: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXPositionAttribute as CFString, &position) == .success,
              AXUIElementCopyAttributeValue(element, kAXSizeAttribute as CFString, &size) == .success,
              let position, let size, CFGetTypeID(position) == AXValueGetTypeID(), CFGetTypeID(size) == AXValueGetTypeID() else { return nil }
        let positionValue = unsafeBitCast(position, to: AXValue.self)
        let sizeValue = unsafeBitCast(size, to: AXValue.self)
        var point = CGPoint.zero
        var extent = CGSize.zero
        guard AXValueGetValue(positionValue, .cgPoint, &point), AXValueGetValue(sizeValue, .cgSize, &extent),
              point.x.isFinite, point.y.isFinite, extent.width.isFinite, extent.height.isFinite,
              extent.width > 0, extent.height > 0 else { return nil }
        return CGRect(origin: point, size: extent)
    }
}
