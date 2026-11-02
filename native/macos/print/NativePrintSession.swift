import AppKit
import ApplicationServices
import Foundation
import PDFKit

@MainActor
final class NativePrintSession {
    private let ownedInput: OwnedPrintInput
    private let title: String
    private let channel: PrintChannel
    private let info: NSPrintInfo
    private var operation: NSPrintOperation?
    private var document: PDFDocument?

    init(ownedInput: OwnedPrintInput, title: String, channel: PrintChannel) {
        self.ownedInput = ownedInput
        self.title = title
        self.channel = channel
        info = NSPrintInfo.shared.copy() as! NSPrintInfo
        channel.setCancellationHandler { [weak self] in
            RunLoop.main.perform(inModes: [.default, .modalPanel, .eventTracking]) { [weak self] in
                MainActor.assumeIsolated { self?.cancelNative() }
            }
        }
    }

    func run() throws -> Bool {
        if try channel.isCanceled() { return false }
        if #available(macOS 14.0, *) { NSApplication.shared.activate() }
        else { NSApplication.shared.activate(ignoringOtherApps: true) }
        let panel = NSPrintPanel()
        panel.options = [.showsCopies, .showsPaperSize, .showsOrientation]
        let result = panel.runModal(with: info)
        if try channel.isCanceled() { return false }
        if result != NSApplication.ModalResponse.OK.rawValue { return false }

        let paper = info.paperSize
        let bounds = info.imageablePageBounds
        let geometry = try PrintGeometry.fromLowerLeft(
            paperWidth: Double(paper.width), paperHeight: Double(paper.height),
            x: Double(bounds.minX), y: Double(bounds.minY),
            width: Double(bounds.width), height: Double(bounds.height))
        info.leftMargin = 0
        info.rightMargin = 0
        info.topMargin = 0
        info.bottomMargin = 0
        info.isHorizontallyCentered = false
        info.isVerticallyCentered = false
        info.scalingFactor = 1
        info.dictionary()[NSPrintInfo.AttributeKey.allPages] = true

        channel.beginPDF()
        try PrintWire.json(geometry)
        var input: String?
        while input == nil {
            if try channel.isCanceled() { return false }
            input = try channel.takePDF()
            if input == nil { pumpEvents() }
        }
        if try channel.isCanceled() { return false }
        let url = try ownedInput.pdf(input!)
        guard let pdf = PDFDocument(url: url), pdf.pageCount > 0 else { throw PrintFailure.invalidPDF }
        document = pdf
        for index in 0..<pdf.pageCount {
            if try channel.isCanceled() { return false }
            guard let page = pdf.page(at: index) else { throw PrintFailure.invalidPDF }
            let size = page.bounds(for: .mediaBox).size
            guard abs(Double(size.width) - geometry.paperWidthPoints) < 0.01,
                  abs(Double(size.height) - geometry.paperHeightPoints) < 0.01 else {
                throw PrintFailure.mismatchedPaper
            }
        }
        guard let printOperation = pdf.printOperation(for: info, scalingMode: .pageScaleNone,
                                                      autoRotate: false) else {
            throw PrintFailure.operationUnavailable
        }
        operation = printOperation
        printOperation.jobTitle = title
        printOperation.showsPrintPanel = false
        printOperation.showsProgressPanel = false
        printOperation.canSpawnSeparateThread = true
        let completion = PrintOperationCompletion()
        let window = NSWindow(contentRect: .zero, styleMask: .borderless, backing: .buffered, defer: true)
        window.isReleasedWhenClosed = false
        window.isExcludedFromWindowsMenu = true
        window.collectionBehavior = [.ignoresCycle, .transient]
        defer {
            operation = nil
            document = nil
            window.close()
        }
        if try channel.isCanceled() { return false }
        printOperation.runModal(for: window, delegate: completion,
                                didRun: #selector(PrintOperationCompletion.printOperationDidRun(_:success:contextInfo:)),
                                contextInfo: nil)
        while completion.completed() == nil { pumpEvents() }
        if try channel.isCanceled() { return false }
        if printOperation.printInfo.jobDisposition == .cancel ||
            PMSessionError(PMPrintSession(printOperation.printInfo.pmPrintSession())) == OSStatus(kPMCancel) {
            return false
        }
        guard completion.completed() == true else { throw PrintFailure.operationFailed }
        return true
    }

    private func cancelNative() {
        info.jobDisposition = .cancel
        _ = PMSessionSetError(PMPrintSession(info.pmPrintSession()), OSStatus(kPMCancel))
        if let operation = operation {
            operation.printInfo.jobDisposition = .cancel
            _ = PMSessionSetError(PMPrintSession(operation.printInfo.pmPrintSession()), OSStatus(kPMCancel))
        }
        if let modal = NSApplication.shared.modalWindow {
            NSApplication.shared.stopModal(withCode: .cancel)
            modal.orderOut(nil)
        }
    }

    private func pumpEvents() {
        _ = RunLoop.main.run(mode: .default, before: Date(timeIntervalSinceNow: 0.05))
    }
}
