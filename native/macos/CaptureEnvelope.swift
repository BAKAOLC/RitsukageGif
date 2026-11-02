import Foundation

final class CaptureEnvelope {
    private struct Packet {
        let kind: UInt8
        let metadata: Data
        let sdr: Data?
        let hdr: Data?
        let cursor: Data?
    }

    private let queue = DispatchQueue(label: "RitsukageGif.capture.envelope", qos: .userInteractive)
    private let lock = NSLock()
    private var pending: Packet?
    private var scheduled = false
    private var sequence: UInt64 = 0

    func frame(_ metadata: [String: Any], sdr: Data?, hdr: Data?, cursor: Data?) throws {
        let packet = try makePacket(kind: 1, metadata: metadata, sdr: sdr, hdr: hdr, cursor: cursor)
        lock.lock()
        pending = packet
        let start = !scheduled
        scheduled = true
        lock.unlock()
        if start { queue.async { self.drainOne() } }
    }

    func reply(_ metadata: [String: Any]) throws {
        let packet = try makePacket(kind: 2, metadata: metadata, sdr: nil, hdr: nil, cursor: nil)
        queue.async { self.write(packet) }
    }

    func capabilities(_ metadata: [String: Any]) throws {
        let packet = try makePacket(kind: 3, metadata: metadata, sdr: nil, hdr: nil, cursor: nil)
        queue.async { self.write(packet) }
    }

    func cursor(_ metadata: [String: Any], pixels: Data?) throws {
        let packet = try makePacket(kind: 4, metadata: metadata, sdr: nil, hdr: nil, cursor: pixels)
        queue.sync { self.write(packet) }
    }

    private func makePacket(kind: UInt8, metadata: [String: Any], sdr: Data?, hdr: Data?, cursor: Data?) throws -> Packet {
        let encoded = try JSONSerialization.data(withJSONObject: metadata, options: [.sortedKeys])
        let total = (sdr?.count ?? 0) + (hdr?.count ?? 0) + (cursor?.count ?? 0)
        guard !encoded.isEmpty, encoded.count <= 65536, total <= 512 * 1024 * 1024 else {
            throw CaptureFailure.captureFailed
        }
        return Packet(kind: kind, metadata: encoded, sdr: sdr, hdr: hdr, cursor: cursor)
    }

    private func drainOne() {
        lock.lock()
        let packet = pending
        pending = nil
        lock.unlock()
        if let packet { write(packet) }
        lock.lock()
        let again = pending != nil
        if !again { scheduled = false }
        lock.unlock()
        if again { queue.async { self.drainOne() } }
    }

    private func write(_ packet: Packet) {
        var header = Data(capacity: 32)
        append(UInt32(0x32434752), to: &header)
        header.append(packet.kind)
        header.append(0)
        append(UInt16(32), to: &header)
        append(UInt32(packet.metadata.count), to: &header)
        append(UInt32(packet.sdr?.count ?? 0), to: &header)
        append(UInt32(packet.hdr?.count ?? 0), to: &header)
        append(UInt32(packet.cursor?.count ?? 0), to: &header)
        sequence &+= 1
        append(sequence, to: &header)
        do {
            try FileHandle.standardOutput.write(contentsOf: header)
            try FileHandle.standardOutput.write(contentsOf: packet.metadata)
            if let bytes = packet.sdr { try FileHandle.standardOutput.write(contentsOf: bytes) }
            if let bytes = packet.hdr { try FileHandle.standardOutput.write(contentsOf: bytes) }
            if let bytes = packet.cursor { try FileHandle.standardOutput.write(contentsOf: bytes) }
        } catch {
            CaptureFailure.terminate(error)
        }
    }

    private func append<T: FixedWidthInteger>(_ value: T, to output: inout Data) {
        var littleEndian = value.littleEndian
        withUnsafeBytes(of: &littleEndian) { output.append(contentsOf: $0) }
    }
}
