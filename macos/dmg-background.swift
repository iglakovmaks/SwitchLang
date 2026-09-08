import AppKit
import Foundation

guard CommandLine.arguments.count == 2 else {
    fputs("Usage: dmg-background.swift OUTPUT.png\n", stderr)
    exit(2)
}

let outputURL = URL(fileURLWithPath: CommandLine.arguments[1])
// Finder displays the DMG window in points. On a Retina display that window
// is rendered at 2x, so a 1x background becomes visibly blurry. Render the
// artwork at Retina resolution while keeping its logical size unchanged.
let scale: CGFloat = 2
let size = NSSize(width: 700, height: 460)
let pixelWidth = Int(size.width * scale)
let pixelHeight = Int(size.height * scale)
guard let bitmap = NSBitmapImageRep(
    bitmapDataPlanes: nil,
    pixelsWide: pixelWidth,
    pixelsHigh: pixelHeight,
    bitsPerSample: 8,
    samplesPerPixel: 4,
    hasAlpha: true,
    isPlanar: false,
    colorSpaceName: .deviceRGB,
    bitmapFormat: [],
    bytesPerRow: 0,
    bitsPerPixel: 0
) else {
    fputs("Could not create bitmap\n", stderr)
    exit(1)
}
bitmap.size = size

NSGraphicsContext.saveGraphicsState()
guard let graphicsContext = NSGraphicsContext(bitmapImageRep: bitmap) else {
    fputs("Could not create graphics context\n", stderr)
    exit(1)
}
NSGraphicsContext.current = graphicsContext
graphicsContext.cgContext.interpolationQuality = .high

NSColor(calibratedWhite: 0.97, alpha: 1).setFill()
NSBezierPath(rect: NSRect(origin: .zero, size: size)).fill()

let title = "Перетащите приложение в Applications"
let subtitle = "для установки"
let footer = "Developed by iglakovmaks"

let titleAttributes: [NSAttributedString.Key: Any] = [
    .font: NSFont.systemFont(ofSize: 22, weight: .semibold),
    .foregroundColor: NSColor(calibratedWhite: 0.12, alpha: 1)
]
let subtitleAttributes: [NSAttributedString.Key: Any] = [
    .font: NSFont.systemFont(ofSize: 15),
    .foregroundColor: NSColor(calibratedWhite: 0.40, alpha: 1)
]
let footerAttributes: [NSAttributedString.Key: Any] = [
    .font: NSFont.systemFont(ofSize: 12),
    .foregroundColor: NSColor(calibratedWhite: 0.42, alpha: 1)
]

let titleSize = title.size(withAttributes: titleAttributes)
title.draw(
    at: NSPoint(x: (size.width - titleSize.width) / 2, y: size.height - 62),
    withAttributes: titleAttributes
)

let subtitleSize = subtitle.size(withAttributes: subtitleAttributes)
subtitle.draw(
    at: NSPoint(x: (size.width - subtitleSize.width) / 2, y: size.height - 88),
    withAttributes: subtitleAttributes
)

let arrowY: CGFloat = 218
let arrowStart: CGFloat = 270
let arrowEnd: CGFloat = 430
let arrowColor = NSColor.controlAccentColor
arrowColor.setStroke()
arrowColor.setFill()

let line = NSBezierPath()
line.lineWidth = 3
line.move(to: NSPoint(x: arrowStart, y: arrowY))
line.line(to: NSPoint(x: arrowEnd, y: arrowY))
line.stroke()

let arrowHead = NSBezierPath()
arrowHead.move(to: NSPoint(x: arrowEnd, y: arrowY))
arrowHead.line(to: NSPoint(x: arrowEnd - 13, y: arrowY + 8))
arrowHead.line(to: NSPoint(x: arrowEnd - 13, y: arrowY - 8))
arrowHead.close()
arrowHead.fill()

let footerSize = footer.size(withAttributes: footerAttributes)
footer.draw(
    // Finder's title bar is included in the saved window bounds, so move the
    // footer up slightly to keep it inside the visible content area.
    at: NSPoint(x: (size.width - footerSize.width) / 2, y: 52),
    withAttributes: footerAttributes
)

graphicsContext.flushGraphics()
NSGraphicsContext.restoreGraphicsState()

guard let png = bitmap.representation(using: .png, properties: [:]) else {
    fputs("Could not encode PNG\n", stderr)
    exit(1)
}

try png.write(to: outputURL)
