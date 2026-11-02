import CoreMedia
import Foundation
import ScreenCaptureKit

@available(macOS 13.0, *)
final class SystemAudio: NSObject, SCStreamOutput, SCStreamDelegate {
    private let output = PCMOutput()
    private let queue = DispatchQueue(label: "com.ritsukage.audio.system")
    private var stream: SCStream?
    private var silenceTimer: DispatchSourceTimer?

    func start(device: String) async throws {
        guard device.isEmpty || device == "system" else {
            throw AudioFailure.message("The selected system audio source is unavailable.")
        }
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
        guard let display = content.displays.first else {
            throw AudioFailure.message("No display is available for system audio capture.")
        }
        let filter = SCContentFilter(display: display, excludingWindows: [])
        let configuration = SCStreamConfiguration()
        configuration.width = 2
        configuration.height = 2
        configuration.minimumFrameInterval = CMTime(value: 1, timescale: 1)
        configuration.capturesAudio = true
        configuration.sampleRate = 48000
        configuration.channelCount = 2
        configuration.excludesCurrentProcessAudio = false
        let capture = SCStream(filter: filter, configuration: configuration, delegate: self)
        try capture.addStreamOutput(self, type: .audio, sampleHandlerQueue: queue)
        stream = capture
        try await capture.startCapture()
        let timer = DispatchSource.makeTimerSource(queue: queue)
        timer.schedule(deadline: .now() + .milliseconds(100), repeating: .milliseconds(100))
        timer.setEventHandler { [weak self] in
            do { try self?.output.fillSilence() } catch { AudioMain.fail(error) }
        }
        silenceTimer = timer
        timer.resume()
    }

    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer,
                of type: SCStreamOutputType) {
        guard type == .audio else { return }
        do {
            try output.write(sampleBuffer)
        } catch {
            AudioMain.fail(error)
        }
    }

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        AudioMain.fail(error)
    }
}
