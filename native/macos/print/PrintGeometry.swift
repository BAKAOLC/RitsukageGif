import Foundation

struct PrintGeometry: Encodable, Equatable {
    let paperWidthPoints: Double
    let paperHeightPoints: Double
    let printableXPoints: Double
    let printableYPoints: Double
    let printableWidthPoints: Double
    let printableHeightPoints: Double

    enum CodingKeys: String, CodingKey {
        case paperWidthPoints = "PaperWidthPoints"
        case paperHeightPoints = "PaperHeightPoints"
        case printableXPoints = "PrintableXPoints"
        case printableYPoints = "PrintableYPoints"
        case printableWidthPoints = "PrintableWidthPoints"
        case printableHeightPoints = "PrintableHeightPoints"
    }

    static func fromLowerLeft(paperWidth: Double, paperHeight: Double, x: Double, y: Double,
                              width: Double, height: Double) throws -> PrintGeometry {
        guard [paperWidth, paperHeight, x, y, width, height].allSatisfy({ $0.isFinite }),
              paperWidth > 0, paperHeight > 0, width > 0, height > 0 else {
            throw PrintFailure.invalidGeometry
        }
        // Borderless drivers can report imageable bounds outside the physical sheet.
        let left = max(0, x)
        let bottom = max(0, y)
        let right = min(paperWidth, x + width)
        let top = min(paperHeight, y + height)
        guard right > left, top > bottom else { throw PrintFailure.invalidGeometry }
        return PrintGeometry(paperWidthPoints: paperWidth, paperHeightPoints: paperHeight,
                             printableXPoints: left, printableYPoints: paperHeight - top,
                             printableWidthPoints: right - left, printableHeightPoints: top - bottom)
    }
}
