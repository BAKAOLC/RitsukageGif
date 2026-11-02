import AVFoundation
import CoreMedia
import Foundation

final class PCMOutput {
    private let output = FileHandle.standardOutput
    private let format = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 48000,
                                       channels: 2, interleaved: true)!
    private let origin = CMClockGetTime(CMClockGetHostTimeClock())
    private var converter: AVAudioConverter?
    private var framesWritten: Int64 = 0

    func fillSilence() throws {
        let elapsed = CMTimeGetSeconds(CMClockGetTime(CMClockGetHostTimeClock()) - origin) - 0.1
        try silence(until: max(0, Int64(elapsed * 48000)))
    }

    private func silence(until target: Int64) throws {
        while framesWritten < target {
            let count = min(target - framesWritten, 4800)
            try output.write(contentsOf: Data(count: Int(count) * 8))
            framesWritten += count
        }
    }

    func write(_ sample: CMSampleBuffer) throws {
        guard sample.isValid, CMSampleBufferDataIsReady(sample),
              let description = CMSampleBufferGetFormatDescription(sample) else {
            return
        }
        let inputFormat = AVAudioFormat(cmAudioFormatDescription: description)
        let count = CMSampleBufferGetNumSamples(sample)
        guard count > 0,
              let input = AVAudioPCMBuffer(pcmFormat: inputFormat, frameCapacity: AVAudioFrameCount(count)) else {
            return
        }
        input.frameLength = AVAudioFrameCount(count)
        let status = CMSampleBufferCopyPCMDataIntoAudioBufferList(sample, at: 0,
                                                                  frameCount: Int32(count),
                                                                  into: input.mutableAudioBufferList)
        guard status == noErr else {
            throw AudioFailure.message("Could not read captured PCM audio (\(status)).")
        }
        if converter?.inputFormat != inputFormat {
            converter = AVAudioConverter(from: inputFormat, to: format)
        }
        guard let converter else { throw AudioFailure.message("Audio format conversion is unavailable.") }
        let capacity = AVAudioFrameCount(ceil(Double(count) * format.sampleRate / inputFormat.sampleRate)) + 64
        guard let converted = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: capacity) else {
            throw AudioFailure.message("Could not allocate audio conversion buffer.")
        }
        var supplied = false
        var error: NSError?
        let result = converter.convert(to: converted, error: &error) { _, state in
            if supplied {
                state.pointee = .noDataNow
                return nil
            }
            supplied = true
            state.pointee = .haveData
            return input
        }
        if let error { throw error }
        guard result != .error else { throw AudioFailure.message("Audio format conversion failed.") }
        let position = CMSampleBufferGetPresentationTimeStamp(sample)
        guard position.isNumeric else { throw AudioFailure.message("The audio device returned an invalid timestamp.") }
        let startFrame = max(0, Int64((CMTimeGetSeconds(position - origin) * 48000).rounded()))
        try silence(until: startFrame)
        let skip = min(Int64(converted.frameLength), max(0, framesWritten - startFrame))
        let frameCount = Int64(converted.frameLength) - skip
        guard frameCount > 0 else { return }
        let buffers = UnsafeMutableAudioBufferListPointer(converted.mutableAudioBufferList)
        for buffer in buffers {
            guard let data = buffer.mData, buffer.mDataByteSize > 0 else { continue }
            try output.write(contentsOf: Data(bytes: data.advanced(by: Int(skip) * 8), count: Int(frameCount) * 8))
        }
        framesWritten += frameCount
    }
}
