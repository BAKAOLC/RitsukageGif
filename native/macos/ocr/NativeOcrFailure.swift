import Foundation

enum NativeOcrFailure: Error, LocalizedError {
    case arguments
    case image
    case language

    var errorDescription: String? {
        switch self {
        case .arguments: return "Invalid OCR helper arguments."
        case .image: return "Invalid OCR image dimensions or BGRA data."
        case .language: return "The requested Vision recognition language is unavailable."
        }
    }
}
