import Cocoa

/// Draws menu-bar status images: a radial progress ring (determinate) and a
/// rotating arc (indeterminate spinner). Rendered fresh each update so the
/// status button can show live progress without any subviews.
enum ProgressIcon {
    private static let size: CGFloat = 15
    private static let lineWidth: CGFloat = 2.2

    /// Filled ring for known progress (0...1), with a faint track behind it.
    static func ring(fraction: Double, color: NSColor) -> NSImage {
        let f = CGFloat(max(0, min(1, fraction)))
        return draw { center, radius in
            let track = NSBezierPath()
            track.appendArc(withCenter: center, radius: radius, startAngle: 0, endAngle: 360)
            track.lineWidth = lineWidth
            color.withAlphaComponent(0.22).setStroke()
            track.stroke()

            guard f > 0 else { return }
            let arc = NSBezierPath()
            arc.appendArc(withCenter: center, radius: radius, startAngle: 90, endAngle: 90 - 360 * f, clockwise: true)
            arc.lineWidth = lineWidth
            arc.lineCapStyle = .round
            color.setStroke()
            arc.stroke()
        }
    }

    /// A 270° arc at the given rotation — animate `angle` for an indeterminate spinner.
    static func spinner(angle: CGFloat, color: NSColor) -> NSImage {
        draw { center, radius in
            let arc = NSBezierPath()
            arc.appendArc(withCenter: center, radius: radius, startAngle: angle, endAngle: angle - 270, clockwise: true)
            arc.lineWidth = lineWidth
            arc.lineCapStyle = .round
            color.setStroke()
            arc.stroke()
        }
    }

    private static func draw(_ body: (_ center: NSPoint, _ radius: CGFloat) -> Void) -> NSImage {
        let image = NSImage(size: NSSize(width: size, height: size))
        image.lockFocus()
        let center = NSPoint(x: size / 2, y: size / 2)
        let radius = (size - lineWidth) / 2
        body(center, radius)
        image.unlockFocus()
        image.isTemplate = false
        return image
    }
}
