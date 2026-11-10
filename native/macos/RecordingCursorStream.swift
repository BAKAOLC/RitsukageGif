import Foundation

extension CaptureHelper {
    @MainActor
    static func recordingCursor() throws {
        let envelope = CaptureEnvelope()
        var bitmapId: UInt32 = 0
        while let request = readLine() {
            guard request == "cursor" else { throw CaptureFailure.invalidArguments }
            bitmapId &+= 1
            if bitmapId == 0 { bitmapId = 1 }
            let cursor = CursorSnapshot.capture(bitmapId: bitmapId, reuseBitmap: true)
            try envelope.cursor(["cursor": cursor.metadata], pixels: cursor.pixels)
        }
    }
}
