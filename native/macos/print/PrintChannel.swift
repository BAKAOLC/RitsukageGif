import Foundation
import Darwin

final class PrintChannel: @unchecked Sendable {
    private enum Phase: Equatable { case selecting, awaitingPDF, printing, finished }
    private let lock = NSLock()
    private var phase = Phase.selecting
    private var pdfPath: String?
    private var canceled = false
    private var protocolError = false
    private var parentExited = false
    private var cancelHandler: (@Sendable () -> Void)?

    func setCancellationHandler(_ handler: @escaping @Sendable () -> Void) {
        lock.lock()
        cancelHandler = handler
        lock.unlock()
    }

    func startReader() {
        Thread.detachNewThread { [self] in
            while let line = readLine(strippingNewline: true) { receive(line) }
            cancel()
        }
    }

    func receive(_ line: String) {
        if line == "cancel" { cancel(); return }
        lock.lock()
        if phase == .finished || canceled { lock.unlock(); return }
        if phase == .awaitingPDF && pdfPath == nil {
            pdfPath = line
            lock.unlock()
            return
        }
        protocolError = true
        lock.unlock()
        cancel()
    }

    func beginPDF() {
        lock.lock()
        if !canceled { phase = .awaitingPDF }
        lock.unlock()
    }

    func takePDF() throws -> String? {
        lock.lock()
        defer { lock.unlock() }
        if protocolError { throw PrintFailure.invalidProtocol }
        if canceled { return nil }
        guard let path = pdfPath else { return nil }
        pdfPath = nil
        phase = .printing
        return path
    }

    func isCanceled() throws -> Bool {
        lock.lock()
        defer { lock.unlock() }
        if protocolError { throw PrintFailure.invalidProtocol }
        return canceled
    }

    func cancel(parentDied: Bool = false) {
        lock.lock()
        if parentDied { parentExited = true }
        if canceled || phase == .finished { lock.unlock(); return }
        canceled = true
        let handler = cancelHandler
        lock.unlock()
        handler?()
        DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + 1) { [self] in
            lock.lock()
            let mustExit = phase != .finished
            let reply = !parentExited
            let failed = protocolError
            if mustExit { phase = .finished }
            lock.unlock()
            if mustExit {
                if reply {
                    if failed { PrintWire.error(PrintFailure.invalidProtocol) }
                    else { try? PrintWire.line("canceled") }
                }
                _exit(failed ? 1 : 0)
            }
        }
    }

    func terminal(printed: Bool) -> String? {
        lock.lock()
        defer { lock.unlock() }
        if phase == .finished { return nil }
        phase = .finished
        if parentExited { return nil }
        return printed && !canceled ? "printed" : "canceled"
    }

    func finish() {
        lock.lock()
        phase = .finished
        lock.unlock()
    }
}
