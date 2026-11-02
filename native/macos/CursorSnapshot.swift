import AppKit
import CoreGraphics
import Foundation

struct CursorSnapshot {
    let metadata: [String: Any]
    let pixels: Data?

    @MainActor private static var cachedCursor: NSCursor?
    @MainActor private static var cachedBitmap: CursorSnapshot?

    @MainActor
    static func capture(bitmapId: UInt32, reuseBitmap: Bool = false) -> CursorSnapshot {
        read(bitmapId: bitmapId, reuseBitmap: reuseBitmap)
    }

    @MainActor
    private static func read(bitmapId: UInt32, reuseBitmap: Bool) -> CursorSnapshot {
        let observed = Int64(DispatchTime.now().uptimeNanoseconds)
        guard rgc_cursor_is_visible() else {
            return CursorSnapshot(metadata: ["valid": false, "visible": false, "observedNanoseconds": observed], pixels: nil)
        }
        guard let cursor = NSCursor.currentSystem,
              let position = CGEvent(source: nil)?.location else {
            return CursorSnapshot(metadata: ["valid": false, "visible": false,
                                               "observedNanoseconds": observed], pixels: nil)
        }
        if reuseBitmap, cachedCursor === cursor, let cached = cachedBitmap {
            var metadata = cached.metadata
            metadata["x"] = Double(position.x); metadata["y"] = Double(position.y)
            metadata["observedNanoseconds"] = observed
            return CursorSnapshot(metadata: metadata, pixels: nil)
        }
        guard let image = cursor.image.cgImage(forProposedRect: nil, context: nil, hints: nil),
              image.width > 0, image.height > 0, image.width <= 4096, image.height <= 4096 else {
            return CursorSnapshot(metadata: ["valid": false, "visible": false,
                                               "observedNanoseconds": observed], pixels: nil)
        }
        let scale = cursor.image.size.width > 0 ? CGFloat(image.width) / cursor.image.size.width : 1
        var pixels = Data(count: image.width * image.height * 4)
        let drawn = pixels.withUnsafeMutableBytes { bytes -> Bool in
            guard let context = CGContext(data: bytes.baseAddress, width: image.width, height: image.height,
                                          bitsPerComponent: 8, bytesPerRow: image.width * 4,
                                          space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                          bitmapInfo: CGBitmapInfo.byteOrder32Little.rawValue |
                                            CGImageAlphaInfo.premultipliedFirst.rawValue) else { return false }
            context.translateBy(x: 0, y: CGFloat(image.height))
            context.scaleBy(x: 1, y: -1)
            context.draw(image, in: CGRect(x: 0, y: 0, width: CGFloat(image.width), height: CGFloat(image.height)))
            let output = bytes.bindMemory(to: UInt8.self)
            for index in stride(from: 0, to: output.count, by: 4) {
                let alpha = Int(output[index + 3])
                if alpha == 0 {
                    output[index] = 0; output[index + 1] = 0; output[index + 2] = 0
                } else if alpha < 255 {
                    for channel in 0..<3 {
                        output[index + channel] = UInt8(min(255, (Int(output[index + channel]) * 255 + alpha / 2) / alpha))
                    }
                }
            }
            return true
        }
        guard drawn else { return CursorSnapshot(metadata: ["valid": false, "visible": false], pixels: nil) }
        let result = CursorSnapshot(metadata: ["valid": true, "visible": true, "bitmapId": bitmapId,
                                           "width": image.width, "height": image.height,
                                           "hotspotX": Int((cursor.hotSpot.x * scale).rounded()),
                                           "hotspotY": Int((cursor.hotSpot.y * scale).rounded()),
                                           "x": Double(position.x), "y": Double(position.y), "pixelScale": Double(scale),
                                           "observedNanoseconds": observed], pixels: pixels)
        cachedCursor = cursor
        cachedBitmap = result
        return result
    }
}
