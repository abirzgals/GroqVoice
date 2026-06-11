import Cocoa

/// Puts text on the pasteboard, synthesizes Cmd+V into the focused app,
/// then restores the previous string contents.
enum Paster {
    static func paste(_ text: String) {
        let pb = NSPasteboard.general
        let oldString = pb.string(forType: .string)

        pb.clearContents()
        pb.setString(text, forType: .string)

        waitForModifierRelease()

        let src = CGEventSource(stateID: .combinedSessionState)
        let kVK_V: CGKeyCode = 9
        guard let down = CGEvent(keyboardEventSource: src, virtualKey: kVK_V, keyDown: true),
              let up = CGEvent(keyboardEventSource: src, virtualKey: kVK_V, keyDown: false) else {
            Log.write("paste: failed to create CGEvent")
            return
        }
        down.flags = .maskCommand
        up.flags = .maskCommand
        down.post(tap: .cghidEventTap)
        up.post(tap: .cghidEventTap)

        if let oldString {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                pb.clearContents()
                pb.setString(oldString, forType: .string)
            }
        }
    }

    /// Wait up to 300 ms for the user to release Fn/modifiers so they don't
    /// combine with the synthesized Cmd+V.
    private static func waitForModifierRelease() {
        let blocked: NSEvent.ModifierFlags = [.function, .command, .option, .control, .shift]
        for _ in 0..<30 {
            if NSEvent.modifierFlags.intersection(blocked).isEmpty { return }
            Thread.sleep(forTimeInterval: 0.01)
        }
    }
}
