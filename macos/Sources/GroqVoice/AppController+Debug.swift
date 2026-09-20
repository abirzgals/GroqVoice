import Cocoa

extension AppController {
    /// Debug: `--snapshot-ui <dir>` renders the Settings tabs and the History
    /// window to PNGs and quits. Own windows can be captured without the
    /// Screen Recording permission.
    func snapshotUI(to dir: URL) {
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let settings = showSettings()
        let history = showHistory()
        let dictionary = showDictionary()

        func capture(_ window: NSWindow?, _ name: String) {
            guard let window,
                  let cg = CGWindowListCreateImage(.null, .optionIncludingWindow, CGWindowID(window.windowNumber),
                                                   [.boundsIgnoreFraming, .bestResolution]) else {
                print("capture failed: \(name)")
                return
            }
            let rep = NSBitmapImageRep(cgImage: cg)
            if let png = rep.representation(using: .png, properties: [:]) {
                let url = dir.appendingPathComponent(name + ".png")
                try? png.write(to: url)
                print("wrote \(url.path) (\(cg.width)×\(cg.height))")
            }
        }

        var step = 0
        func next() {
            switch step {
            case 0..<4:
                settings.selectTab(step)
                settings.window?.makeKeyAndOrderFront(nil)
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                    capture(settings.window, "settings-\(step)")
                    step += 1
                    next()
                }
            case 4:
                history.window?.makeKeyAndOrderFront(nil)
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                    capture(history.window, "history")
                    step += 1
                    next()
                }
            case 5, 6:
                dictionary.selectTab(step - 5)
                dictionary.window?.makeKeyAndOrderFront(nil)
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                    capture(dictionary.window, "dictionary-\(step - 5)")
                    step += 1
                    next()
                }
            default:
                exit(0)
            }
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.0) { next() }
    }
}
