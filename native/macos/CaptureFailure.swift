import Foundation
import Darwin

enum CaptureFailure: Error, Equatable {
    case permissionDenied
    case sourceLost
    case invalidArguments
    case captureFailed

    var code: String {
        switch self {
        case .permissionDenied: return "permission-denied"
        case .sourceLost: return "source-lost"
        case .invalidArguments: return "unsupported"
        case .captureFailed: return "capture-failed"
        }
    }

    static func terminate(_ error: Error) -> Never {
        let failure = error as? CaptureFailure
        let code = failure?.code ?? "capture-failed"
        let detail = failure == nil ? ": \(error.localizedDescription)" : ""
        FileHandle.standardError.write(Data((code + detail + "\n").utf8))
        exit(failure == .permissionDenied ? 77 : 1)
    }
}
