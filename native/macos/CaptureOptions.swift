import Foundation
import CoreGraphics

struct CaptureOptions {
    let mode: String
    let id: UInt32?
    let fps: Int32
    let cursor: Bool
    let hdr: Bool
    let generation: Int64
    let includedWindowTitle: String?
    let excludedWindows: Set<CGWindowID>

    init(_ arguments: [String]) throws {
        guard let mode = arguments.first,
              ["screen", "window", "windows", "window-server", "active-window", "displays", "cursor", "recording-cursor", "permission", "request-permission"].contains(mode) else {
            throw CaptureFailure.invalidArguments
        }
        var values: [String: String] = [:]
        var position = 1
        while position < arguments.count {
            let name = arguments[position]
            guard ["--id", "--fps", "--cursor", "--color", "--generation", "--include-window-title", "--exclude-windows"].contains(name),
                  position + 1 < arguments.count, values[name] == nil else { throw CaptureFailure.invalidArguments }
            values[name] = arguments[position + 1]
            position += 2
        }
        let parsedID = values["--id"].flatMap(UInt32.init)
        if values["--id"] != nil && parsedID == nil { throw CaptureFailure.invalidArguments }
        let included = values["--include-window-title"]
        if let title = included, title.isEmpty || mode != "screen" {
            throw CaptureFailure.invalidArguments
        }
        var excluded: Set<CGWindowID> = []
        if let list = values["--exclude-windows"] {
            guard mode == "screen" else { throw CaptureFailure.invalidArguments }
            for field in list.split(separator: ",", omittingEmptySubsequences: false) {
                guard let id = CGWindowID(field), id != 0 else { throw CaptureFailure.invalidArguments }
                excluded.insert(id)
            }
        }
        guard let parsedFPS = Int32(values["--fps"] ?? "30"), (1...240).contains(parsedFPS),
              let parsedCursor = Int(values["--cursor"] ?? "1"), (0...1).contains(parsedCursor),
              let parsedGeneration = Int64(values["--generation"] ?? "0"), parsedGeneration >= 0,
              ["sdr", "hdr"].contains(values["--color"] ?? "sdr") else {
            throw CaptureFailure.invalidArguments
        }
        if mode == "window" && parsedID == nil { throw CaptureFailure.invalidArguments }
        self.mode = mode
        id = parsedID
        fps = parsedFPS
        cursor = parsedCursor == 1
        hdr = values["--color"] == "hdr"
        generation = parsedGeneration
        includedWindowTitle = included
        excludedWindows = excluded
    }
}
