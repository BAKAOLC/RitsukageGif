import Foundation
import Darwin

final class PrintParentMonitor {
    private let timer: DispatchSourceTimer

    init(parent: pid_t, channel: PrintChannel) {
        timer = DispatchSource.makeTimerSource(queue: .global(qos: .utility))
        timer.schedule(deadline: .now(), repeating: .milliseconds(200))
        timer.setEventHandler {
            if kill(parent, 0) != 0 && errno == ESRCH { channel.cancel(parentDied: true) }
        }
        timer.resume()
    }

    deinit { timer.cancel() }
}
