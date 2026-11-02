import Foundation

enum PrintWire {
    private static let outputLock = NSLock()

    static func json<T: Encodable>(_ value: T) throws {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        let data = try encoder.encode(value)
        try line(String(decoding: data, as: UTF8.self))
    }

    static func line(_ value: String) throws {
        outputLock.lock()
        defer { outputLock.unlock() }
        try FileHandle.standardOutput.write(contentsOf: Data((value + "\n").utf8))
    }

    static func error(_ error: Error) {
        try? FileHandle.standardError.write(contentsOf: Data((error.localizedDescription + "\n").utf8))
    }
}
