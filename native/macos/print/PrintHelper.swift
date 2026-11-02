import AppKit
import Darwin
import Foundation

@main
struct PrintHelper {
    @MainActor
    static func main() {
        signal(SIGPIPE, SIG_IGN)
        let arguments = Array(CommandLine.arguments.dropFirst())
        if arguments == ["capabilities"] {
            do { try PrintWire.json(PrintCapabilities.current()) }
            catch { PrintWire.error(error); exit(1) }
            return
        }
        guard arguments.count == 5, arguments[0] == "--native-print-worker",
              let parent = pid_t(arguments[3]), parent > 1 else {
            PrintWire.error(PrintFailure.invalidArguments)
            exit(2)
        }
        // OWNER belongs to another process; it is deliberately never interpreted as an NSWindow.
        let channel = PrintChannel()
        defer { channel.finish() }
        do {
            let owned = try OwnedPrintInput(directory: arguments[1])
            let monitor = PrintParentMonitor(parent: parent, channel: channel)
            channel.startReader()
            let capability = PrintCapabilities.current()
            guard capability.available else {
                throw PrintFailure.unavailable(capability.failureReason ?? "Native printing is unavailable.")
            }
            let application = NSApplication.shared
            application.setActivationPolicy(.accessory)
            application.finishLaunching()
            let session = NativePrintSession(ownedInput: owned, title: arguments[4], channel: channel)
            let printed = try session.run()
            withExtendedLifetime(monitor) {}
            if let terminal = channel.terminal(printed: printed) { try PrintWire.line(terminal) }
        } catch {
            channel.finish()
            PrintWire.error(error)
            exit(1)
        }
    }
}
