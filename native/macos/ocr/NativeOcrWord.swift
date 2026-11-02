import Foundation

struct NativeOcrWord: Encodable {
    let text: String
    let bounds: NativeOcrBounds
}
