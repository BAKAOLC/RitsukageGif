import Foundation

struct CaptureRequest: Decodable, Sendable {
    struct Region: Decodable, Sendable {
        let x: Int
        let y: Int
        let width: Int
        let height: Int
    }
    let command: String
    let requestId: Int64
    let generation: Int64?
    let streamId: String?
    let region: Region?
    let outputWidth: Int?
    let outputHeight: Int?
}
