import CoreGraphics
import Foundation

struct PrintCapabilities: Encodable {
    let available: Bool
    let failureReason: String?

    enum CodingKeys: String, CodingKey {
        case available = "Available"
        case failureReason = "FailureReason"
    }

    static func current() -> PrintCapabilities {
        if CGSessionCopyCurrentDictionary() == nil {
            return PrintCapabilities(available: false,
                                     failureReason: "A Quartz GUI session is required for the native print panel.")
        }
        return PrintCapabilities(available: true, failureReason: nil)
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(available, forKey: .available)
        if let reason = failureReason { try container.encode(reason, forKey: .failureReason) }
        else { try container.encodeNil(forKey: .failureReason) }
    }
}
