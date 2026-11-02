import AppKit
import ApplicationServices
import Foundation
import Darwin

@main
struct ForegroundPolicyServer {
    static func main() {
        signal(SIGPIPE, SIG_IGN)
        _ = NSApplication.shared.setActivationPolicy(.prohibited)
        let observer = ForegroundObserver()
        observer.start()
        DispatchQueue.global(qos: .utility).async {
            while let command = readLine() {
                if command == "stop" { break }
            }
            DispatchQueue.main.async {
                observer.stop()
                CFRunLoopStop(CFRunLoopGetMain())
            }
        }
        CFRunLoopRun()
        observer.stop()
    }
}

private final class ForegroundObserver {
    private var activation: NSObjectProtocol?
    private var spaceChange: NSObjectProtocol?
    private var observer: AXObserver?
    private var application: AXUIElement?
    private var window: AXUIElement?
    private var previous: Data?
    private var focusedReadAvailable = false

    func start() {
        activation = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didActivateApplicationNotification, object: nil, queue: .main
        ) { [weak self] _ in self?.activate() }
        spaceChange = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.activeSpaceDidChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in self?.activate() }
        activate()
    }

    func stop() {
        if let activation {
            NSWorkspace.shared.notificationCenter.removeObserver(activation)
            self.activation = nil
        }
        if let spaceChange {
            NSWorkspace.shared.notificationCenter.removeObserver(spaceChange)
            self.spaceChange = nil
        }
        detachAccessibility()
    }

    private func detachAccessibility() {
        if let observer {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), AXObserverGetRunLoopSource(observer), .commonModes)
        }
        observer = nil
        application = nil
        window = nil
        focusedReadAvailable = false
    }

    private func activate() {
        detachAccessibility()
        guard let frontmost = NSWorkspace.shared.frontmostApplication else {
            write(program: nil, fullscreen: false, programAvailable: true, fullscreenAvailable: true)
            return
        }
        if AXIsProcessTrusted() {
            let app = AXUIElementCreateApplication(frontmost.processIdentifier)
            var candidate: AXObserver?
            let result = AXObserverCreate(frontmost.processIdentifier, { _, _, notification, context in
                guard let context else { return }
                let service = Unmanaged<ForegroundObserver>.fromOpaque(context).takeUnretainedValue()
                if notification as String == kAXFocusedWindowChangedNotification as String {
                    service.observeWindow()
                }
                service.publish()
            }, &candidate)
            if result == .success, let candidate {
                observer = candidate
                application = app
                let context = Unmanaged.passUnretained(self).toOpaque()
                _ = AXObserverAddNotification(candidate, app, kAXFocusedWindowChangedNotification as CFString, context)
                _ = AXObserverAddNotification(candidate, app, kAXWindowCreatedNotification as CFString, context)
                CFRunLoopAddSource(CFRunLoopGetMain(), AXObserverGetRunLoopSource(candidate), .commonModes)
                observeWindow()
            }
        }
        publish()
    }

    private func observeWindow() {
        guard let observer, let application else { return }
        if let window {
            _ = AXObserverRemoveNotification(observer, window, kAXResizedNotification as CFString)
            _ = AXObserverRemoveNotification(observer, window, kAXMovedNotification as CFString)
        }
        window = nil
        var focused: CFTypeRef?
        let result = AXUIElementCopyAttributeValue(application, kAXFocusedWindowAttribute as CFString, &focused)
        focusedReadAvailable = result == .success || result == .noValue
        if result == .success,
           let focused, CFGetTypeID(focused) == AXUIElementGetTypeID() {
            window = unsafeBitCast(focused, to: AXUIElement.self)
            let context = Unmanaged.passUnretained(self).toOpaque()
            _ = AXObserverAddNotification(observer, window!, kAXResizedNotification as CFString, context)
            _ = AXObserverAddNotification(observer, window!, kAXMovedNotification as CFString, context)
        }
    }

    private func publish() {
        let frontmost = NSWorkspace.shared.frontmostApplication
        let program = frontmost?.executableURL?.path
        var fullscreen = false
        var available = false
        if let window {
            var value: CFTypeRef?
            let result = AXUIElementCopyAttributeValue(window, "AXFullScreen" as CFString, &value)
            if result == .success, let flag = value as? NSNumber {
                fullscreen = flag.boolValue
                available = true
            } else if result == .attributeUnsupported {
                let geometry = fullscreenGeometry(window)
                fullscreen = geometry.0
                available = geometry.1
            }
        } else if observer != nil {
            available = focusedReadAvailable
        }
        write(program: program, fullscreen: fullscreen,
              programAvailable: frontmost == nil || program != nil, fullscreenAvailable: available)
    }

    private func fullscreenGeometry(_ window: AXUIElement) -> (Bool, Bool) {
        var position: CFTypeRef?
        var size: CFTypeRef?
        guard AXUIElementCopyAttributeValue(window, kAXPositionAttribute as CFString, &position) == .success,
              AXUIElementCopyAttributeValue(window, kAXSizeAttribute as CFString, &size) == .success,
              let position, let size,
              CFGetTypeID(position) == AXValueGetTypeID(), CFGetTypeID(size) == AXValueGetTypeID() else {
            return (false, false)
        }
        var point = CGPoint.zero
        var dimensions = CGSize.zero
        guard AXValueGetValue(unsafeBitCast(position, to: AXValue.self), .cgPoint, &point),
              AXValueGetValue(unsafeBitCast(size, to: AXValue.self), .cgSize, &dimensions) else {
            return (false, false)
        }
        var count: UInt32 = 0
        guard CGGetActiveDisplayList(0, nil, &count) == .success else { return (false, false) }
        var displays = [CGDirectDisplayID](repeating: 0, count: Int(count))
        guard CGGetActiveDisplayList(count, &displays, &count) == .success else { return (false, false) }
        let bounds = CGRect(origin: point, size: dimensions)
        return (displays.prefix(Int(count)).contains { CGDisplayBounds($0) == bounds }, true)
    }

    private func write(program: String?, fullscreen: Bool, programAvailable: Bool, fullscreenAvailable: Bool) {
        let value: [String: Any] = ["program": (program as Any?) ?? NSNull(), "fullscreen": fullscreen,
                                    "programAvailable": programAvailable,
                                    "fullscreenAvailable": fullscreenAvailable,
                                    "reason": AXIsProcessTrusted() ? "backend" : "permission"]
        do {
            var data = try JSONSerialization.data(withJSONObject: value, options: [.sortedKeys])
            if data == previous { return }
            previous = data
            data.append(10)
            try FileHandle.standardOutput.write(contentsOf: data)
        } catch {
            fputs("Foreground policy output failed: \(error)\n", stderr)
            exit(74)
        }
    }
}
