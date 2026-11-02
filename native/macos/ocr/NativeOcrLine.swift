import Foundation

struct NativeOcrLine: Encodable {
    let text: String
    let bounds: NativeOcrBounds
    let words: [NativeOcrWord]
}
