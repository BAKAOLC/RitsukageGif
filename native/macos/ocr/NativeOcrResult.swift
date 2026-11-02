import Foundation

struct NativeOcrResult: Encodable {
    let text: String
    let languageTag: String
    let width: Int
    let height: Int
    let textAngle: Double?
    let lines: [NativeOcrLine]
}
