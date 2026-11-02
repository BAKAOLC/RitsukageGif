import Foundation

enum PrintFailure: Error, LocalizedError {
    case invalidArguments
    case unavailable(String)
    case invalidGeometry
    case invalidOwnedDirectory
    case invalidOwnedPDF
    case invalidProtocol
    case invalidPDF
    case mismatchedPaper
    case operationUnavailable
    case operationFailed

    var errorDescription: String? {
        switch self {
        case .invalidArguments: return "Invalid native print helper arguments."
        case .unavailable(let reason): return reason
        case .invalidGeometry: return "The selected printer returned no usable paper geometry."
        case .invalidOwnedDirectory: return "The owned print directory is missing or is not absolute."
        case .invalidOwnedPDF: return "The print input must be the owned document.pdf regular file."
        case .invalidProtocol: return "The native print helper received an unexpected protocol command."
        case .invalidPDF: return "The owned print PDF has no readable pages."
        case .mismatchedPaper: return "A PDF page does not match the selected paper size."
        case .operationUnavailable: return "PDFKit could not create a native print operation."
        case .operationFailed: return "The native print operation did not complete."
        }
    }
}
