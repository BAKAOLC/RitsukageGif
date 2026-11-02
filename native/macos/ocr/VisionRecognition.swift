import Foundation
import CoreGraphics
import Vision

enum VisionRecognition {
    static func request() -> VNRecognizeTextRequest {
        let result = VNRecognizeTextRequest()
        result.recognitionLevel = .accurate
        result.usesLanguageCorrection = true
        return result
    }

    static func recognize(pixels: Data, width: Int, height: Int, language: String) throws -> NativeOcrResult {
        let request = Self.request()
        guard try request.supportedRecognitionLanguages().contains(language) else { throw NativeOcrFailure.language }
        request.recognitionLanguages = [language]
        request.automaticallyDetectsLanguage = false
        guard let provider = CGDataProvider(data: pixels as CFData),
              let space = CGColorSpace(name: CGColorSpace.sRGB),
              let image = CGImage(width: width, height: height, bitsPerComponent: 8, bitsPerPixel: 32,
                                  bytesPerRow: width * 4, space: space,
                                  bitmapInfo: CGBitmapInfo(rawValue: CGBitmapInfo.byteOrder32Little.rawValue |
                                      CGImageAlphaInfo.noneSkipFirst.rawValue),
                                  provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent)
        else { throw NativeOcrFailure.image }
        try VNImageRequestHandler(cgImage: image, options: [:]).perform([request])
        var lines: [NativeOcrLine] = []
        for observation in request.results ?? [] {
            guard let candidate = observation.topCandidates(1).first else { continue }
            let text = candidate.string
            let lineBounds = bounds(observation.boundingBox, width: width, height: height)
            var ranges: [Range<String.Index>] = []
            text.enumerateSubstrings(in: text.startIndex..<text.endIndex, options: [.byWords, .localized]) {
                _, range, _, _ in ranges.append(range)
            }
            var words: [NativeOcrWord] = []
            for range in ranges {
                let box = try candidate.boundingBox(for: range)
                words.append(NativeOcrWord(text: String(text[range]),
                    bounds: box.map { bounds($0.boundingBox, width: width, height: height) } ?? lineBounds))
            }
            if words.isEmpty && !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                words.append(NativeOcrWord(text: text, bounds: lineBounds))
            }
            lines.append(NativeOcrLine(text: text, bounds: lineBounds, words: words))
        }
        return NativeOcrResult(text: lines.map(\.text).joined(separator: "\n"), languageTag: language,
                               width: width, height: height, textAngle: nil, lines: lines)
    }

    private static func bounds(_ rectangle: CGRect, width: Int, height: Int) -> NativeOcrBounds {
        let left = min(Double(width), max(0, rectangle.minX * Double(width)))
        let top = min(Double(height), max(0, (1 - rectangle.maxY) * Double(height)))
        let right = min(Double(width), max(0, rectangle.maxX * Double(width)))
        let bottom = min(Double(height), max(0, (1 - rectangle.minY) * Double(height)))
        return NativeOcrBounds(x: left, y: top, width: max(0, right - left), height: max(0, bottom - top))
    }
}
