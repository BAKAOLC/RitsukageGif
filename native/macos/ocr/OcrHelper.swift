import Foundation
import Darwin

@main
struct OcrHelper {
    static func main() {
        do { try run() }
        catch {
            FileHandle.standardError.write(Data(("OCR helper failed: " + error.localizedDescription + "\n").utf8))
            exit(1)
        }
    }

    private static func run() throws {
        let arguments = CommandLine.arguments
        guard arguments.count >= 2 else { throw NativeOcrFailure.arguments }
        if arguments[1] == "capabilities" && arguments.count == 2 {
            try output(NativeOcrCapabilities(maxImageDimension: 32768,
                languages: VisionRecognition.request().supportedRecognitionLanguages()))
            return
        }
        guard arguments.count == 5, arguments[1] == "recognize", let width = Int(arguments[2]),
              let height = Int(arguments[3]), width > 0, height > 0, width <= 32768, height <= 32768,
              width * height <= 16 * 1024 * 1024 else { throw NativeOcrFailure.arguments }
        let pixels = FileHandle.standardInput.readDataToEndOfFile()
        guard pixels.count == width * height * 4 else { throw NativeOcrFailure.image }
        try output(VisionRecognition.recognize(pixels: pixels, width: width, height: height, language: arguments[4]))
    }

    private static func output<T: Encodable>(_ result: T) throws {
        FileHandle.standardOutput.write(try JSONEncoder().encode(result))
        FileHandle.standardOutput.write(Data([10]))
    }
}
