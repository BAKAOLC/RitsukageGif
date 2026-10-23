import Foundation
import CoreGraphics

struct CaptureOptions {
    let mode: String
    let id: UInt32?
    let fps: Int32
    let cursor: Bool
    let bounds: CGRect?

    init(_ arguments: [String]) throws {
        guard let mode = arguments.first,
              ["screen", "window", "windows", "window-server", "permission", "request-permission"].contains(mode) else {
            throw CaptureFailure.invalidArguments
        }
        var values: [String: String] = [:]
        var position = 1
        while position < arguments.count {
            let name = arguments[position]
            guard ["--id", "--fps", "--cursor", "--x", "--y", "--width", "--height"].contains(name),
                  position + 1 < arguments.count, values[name] == nil else { throw CaptureFailure.invalidArguments }
            values[name] = arguments[position + 1]
            position += 2
        }
        let parsedID = values["--id"].flatMap(UInt32.init)
        if values["--id"] != nil && parsedID == nil { throw CaptureFailure.invalidArguments }
        guard let parsedFPS = Int32(values["--fps"] ?? "30"), (1...240).contains(parsedFPS),
              let parsedCursor = Int(values["--cursor"] ?? "1"), (0...1).contains(parsedCursor) else {
            throw CaptureFailure.invalidArguments
        }
        let geometryNames = ["--x", "--y", "--width", "--height"]
        let hasGeometry = geometryNames.contains { values[$0] != nil }
        var parsedBounds: CGRect?
        if hasGeometry {
            let geometry = geometryNames.compactMap { values[$0].flatMap(Int32.init) }
            guard geometry.count == 4, geometry[2] > 0, geometry[3] > 0 else {
                throw CaptureFailure.invalidArguments
            }
            parsedBounds = CGRect(x: CGFloat(geometry[0]), y: CGFloat(geometry[1]),
                                  width: CGFloat(geometry[2]), height: CGFloat(geometry[3]))
        }
        if mode == "window" && parsedID == nil { throw CaptureFailure.invalidArguments }
        self.mode = mode
        id = parsedID
        fps = parsedFPS
        cursor = parsedCursor == 1
        bounds = parsedBounds
    }
}
