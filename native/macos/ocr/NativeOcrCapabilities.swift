import Foundation

struct NativeOcrCapabilities: Encodable {
    let maxImageDimension: Int
    let languages: [String]
}
