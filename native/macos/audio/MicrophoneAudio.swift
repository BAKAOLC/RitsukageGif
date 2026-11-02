import AVFoundation
import Foundation

final class MicrophoneAudio: NSObject, AVCaptureAudioDataOutputSampleBufferDelegate {
    private let output = PCMOutput()
    private let queue = DispatchQueue(label: "com.ritsukage.audio.microphone")
    private let session = AVCaptureSession()
    private var silenceTimer: DispatchSourceTimer?

    static var devices: [AVCaptureDevice] {
        if #available(macOS 14.0, *) {
            return AVCaptureDevice.DiscoverySession(deviceTypes: [.microphone, .external],
                                                      mediaType: .audio, position: .unspecified).devices
        }
        return AVCaptureDevice.DiscoverySession(deviceTypes: [.builtInMicrophone, .externalUnknown],
                                          mediaType: .audio, position: .unspecified).devices
    }

    func start(device id: String) async throws {
        let permitted = await AVCaptureDevice.requestAccess(for: .audio)
        guard permitted else { throw AudioFailure.message("Microphone permission was denied in System Settings.") }
        let device = id.isEmpty ? AVCaptureDevice.default(for: .audio) : Self.devices.first { $0.uniqueID == id }
        guard let device else { throw AudioFailure.message("The selected microphone is unavailable.") }
        let input = try AVCaptureDeviceInput(device: device)
        let capture = AVCaptureAudioDataOutput()
        capture.setSampleBufferDelegate(self, queue: queue)
        session.beginConfiguration()
        guard session.canAddInput(input), session.canAddOutput(capture) else {
            session.commitConfiguration()
            throw AudioFailure.message("The microphone cannot be opened for capture.")
        }
        session.addInput(input)
        session.addOutput(capture)
        session.commitConfiguration()
        NotificationCenter.default.addObserver(forName: AVCaptureSession.runtimeErrorNotification,
                                                object: session, queue: nil) { notification in
            let error = notification.userInfo?[AVCaptureSessionErrorKey] as? Error
            AudioMain.fail(error ?? AudioFailure.message("Microphone capture stopped."))
        }
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            DispatchQueue.global(qos: .userInitiated).async {
                self.session.startRunning()
                continuation.resume()
            }
        }
        guard session.isRunning else { throw AudioFailure.message("The microphone could not start.") }
        let timer = DispatchSource.makeTimerSource(queue: queue)
        timer.schedule(deadline: .now() + .milliseconds(100), repeating: .milliseconds(100))
        timer.setEventHandler { [weak self] in
            do { try self?.output.fillSilence() } catch { AudioMain.fail(error) }
        }
        silenceTimer = timer
        timer.resume()
    }

    func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer,
                       from connection: AVCaptureConnection) {
        do {
            try self.output.write(sampleBuffer)
        } catch {
            AudioMain.fail(error)
        }
    }
}
