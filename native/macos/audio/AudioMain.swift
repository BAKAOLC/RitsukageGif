import AVFoundation
import Darwin
import Foundation
import ScreenCaptureKit

@main
struct AudioMain {
    static func main() async {
        do {
            let arguments = Array(CommandLine.arguments.dropFirst())
            let command = arguments.first ?? ""
            let device = arguments.count > 1 ? arguments[1] : ""
            switch command {
            case "version":
                try FileHandle.standardOutput.write(contentsOf: Data("1\n".utf8))
                return
            case "authorize-system":
                _ = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
                return
            case "authorize-microphone":
                guard await AVCaptureDevice.requestAccess(for: .audio) else {
                    throw AudioFailure.message("Microphone permission was denied in System Settings.")
                }
                return
            case "list-system":
                try list([["id": "system", "name": "System audio (ScreenCaptureKit)", "isDefault": true]])
                return
            case "list-microphones":
                let defaultID = AVCaptureDevice.default(for: .audio)?.uniqueID
                try list(MicrophoneAudio.devices.map {
                    ["id": $0.uniqueID, "name": $0.localizedName, "isDefault": $0.uniqueID == defaultID]
                })
                return
            case "system":
                guard #available(macOS 13.0, *) else {
                    throw AudioFailure.message("System audio capture requires macOS 13 or later.")
                }
                let source = SystemAudio()
                defer { withExtendedLifetime(source) {} }
                try await source.start(device: device)
                await wait()
            case "microphone":
                let source = MicrophoneAudio()
                defer { withExtendedLifetime(source) {} }
                try await source.start(device: device)
                await wait()
            default:
                throw AudioFailure.message("Expected list-system, list-microphones, system, or microphone.")
            }
        } catch {
            fail(error)
        }
    }

    private static func list(_ devices: [[String: Any]]) throws {
        try FileHandle.standardOutput.write(contentsOf: JSONSerialization.data(withJSONObject: devices))
    }

    private static func wait() async {
        // The parent owns this process and terminates it when capture stops or is cancelled.
        await withUnsafeContinuation { (_: UnsafeContinuation<Void, Never>) in }
    }

    static func fail(_ error: Error) -> Never {
        try? FileHandle.standardError.write(contentsOf: Data((error.localizedDescription + "\n").utf8))
        exit(1)
    }
}
