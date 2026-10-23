import Foundation
import CoreGraphics

enum WindowCatalog {
    static func bounds(of id: CGWindowID) -> CGRect? {
        guard let windows = CGWindowListCopyWindowInfo(.optionIncludingWindow, id) as? [[String: Any]],
              let item = windows.first,
              let rectangle = item[kCGWindowBounds as String] as? [String: Any] else { return nil }
        return CGRect(dictionaryRepresentation: rectangle as CFDictionary)
    }

    static func write() throws {
        guard CGPreflightScreenCaptureAccess() else { throw CaptureFailure.permissionDenied }
        guard let windows = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID)
            as? [[String: Any]] else { throw CaptureFailure.captureFailed }
        let entries: [[String: Any]] = windows.compactMap { item in
            guard let id = item[kCGWindowNumber as String] as? NSNumber,
                  let pid = item[kCGWindowOwnerPID as String] as? NSNumber,
                  let layer = item[kCGWindowLayer as String] as? NSNumber, layer.intValue == 0,
                  let owner = item[kCGWindowOwnerName as String] as? String,
                  let title = item[kCGWindowName as String] as? String, !title.isEmpty,
                  let rectangle = item[kCGWindowBounds as String] as? [String: Any],
                  let bounds = CGRect(dictionaryRepresentation: rectangle as CFDictionary),
                  bounds.width > 0, bounds.height > 0 else { return nil }
            let geometry: [String: Int] = ["x": Int(bounds.minX.rounded(.down)), "y": Int(bounds.minY.rounded(.down)),
                                          "width": Int(bounds.width.rounded(.up)), "height": Int(bounds.height.rounded(.up))]
            return ["handle": id.uint32Value, "pid": pid.int32Value, "process": owner,
                    "title": title, "bounds": geometry, "client": geometry]
        }
        var output = try JSONSerialization.data(withJSONObject: entries, options: [.sortedKeys])
        output.append(10)
        try FileHandle.standardOutput.write(contentsOf: output)
    }
}
