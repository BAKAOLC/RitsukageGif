import AppKit
import Foundation

final class PrintOperationCompletion: NSObject, @unchecked Sendable {
    private let lock = NSLock()
    private var result: Bool?

    @objc func printOperationDidRun(_ operation: NSPrintOperation, success: Bool,
                                   contextInfo: UnsafeMutableRawPointer?) {
        lock.lock()
        result = success
        lock.unlock()
    }

    func completed() -> Bool? {
        lock.lock()
        defer { lock.unlock() }
        return result
    }
}
