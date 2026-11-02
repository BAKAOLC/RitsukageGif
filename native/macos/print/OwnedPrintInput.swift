import Foundation

struct OwnedPrintInput {
    private let originalDirectory: URL
    private let directory: URL

    init(directory path: String) throws {
        guard path.hasPrefix("/") else { throw PrintFailure.invalidOwnedDirectory }
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: path, isDirectory: &isDirectory), isDirectory.boolValue else {
            throw PrintFailure.invalidOwnedDirectory
        }
        originalDirectory = URL(fileURLWithPath: path, isDirectory: true).standardizedFileURL
        directory = originalDirectory.resolvingSymlinksInPath()
    }

    func pdf(_ path: String) throws -> URL {
        guard path.hasPrefix("/") else { throw PrintFailure.invalidOwnedPDF }
        let input = URL(fileURLWithPath: path).standardizedFileURL
        let expected = directory.appendingPathComponent("document.pdf")
        let original = originalDirectory.appendingPathComponent("document.pdf")
        guard input.path == expected.path || input.path == original.path,
              input.resolvingSymlinksInPath().path == expected.path else {
            throw PrintFailure.invalidOwnedPDF
        }
        let attributes = try FileManager.default.attributesOfItem(atPath: input.path)
        guard attributes[.type] as? FileAttributeType == .typeRegular else {
            throw PrintFailure.invalidOwnedPDF
        }
        return input
    }
}
