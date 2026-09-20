import Foundation

/// The rules of the push-to-talk keys — hold to talk, double-tap to lock,
/// a chord or a lone tap cancels — as events in, commands out. It keeps only
/// what it needs to know about the keys; whether a recording is running is the
/// app's business and is passed in, so the two can never disagree.
struct PushToTalk {
    enum Phase: Equatable {
        case idle
        case recording(locked: Bool)
        case processing
    }

    enum Command: Equatable {
        case start(HotkeyKey)   // begin a take
        case resume             // pressed again during the release tail: the take goes on
        case finish             // stop (after the release tail) and process
        case lock               // second quick tap: record until the next press
        case discard(String)    // drop the take; the reason is for the log
    }

    private var activeKey: HotkeyKey?
    private var keyDownAt: Date?
    private var lastQuickTapAt: Date?
    private var chordCancelled = false
    private var ignoreNextKeyUp = false

    /// `inTail`: the key was released and the take is about to stop.
    mutating func keyDown(_ key: HotkeyKey, phase: Phase, inTail: Bool, now: Date = Date()) -> Command? {
        switch phase {
        case .recording(locked: true):
            // The key that started a locked recording also stops it.
            guard key == activeKey else { return nil }
            ignoreNextKeyUp = true
            return .finish
        case .idle:
            activeKey = key
            keyDownAt = now
            chordCancelled = false
            return .start(key)
        case .recording(locked: false):
            guard key == activeKey, inTail else { return nil }
            keyDownAt = now
            chordCancelled = false
            return .resume
        case .processing:
            return nil
        }
    }

    /// `actionHasSelection` is asked only after a quick tap: on an action key
    /// with text selected, the tap applies the action — no need to say anything.
    mutating func keyUp(_ key: HotkeyKey, phase: Phase, cfg: Config, now: Date = Date(),
                        actionHasSelection: () -> Bool) -> Command? {
        guard key == activeKey else { return nil }
        if ignoreNextKeyUp {
            ignoreNextKeyUp = false
            return nil
        }
        guard phase == .recording(locked: false), !chordCancelled, let t0 = keyDownAt else { return nil }

        if now.timeIntervalSince(t0) * 1000 >= cfg.pttHoldMs { return .finish }
        if actionHasSelection() { return .finish }

        // Quick tap: a second one within the window locks the recording on.
        if let prev = lastQuickTapAt, now.timeIntervalSince(prev) * 1000 < cfg.doubleTapWindowMs {
            lastQuickTapAt = nil
            return .lock
        }
        lastQuickTapAt = now
        return .discard("single tap")
    }

    /// Another key or a click while a hotkey is held: it was a modifier for an
    /// OS shortcut, not a take.
    mutating func chord(phase: Phase) -> Command? {
        guard case .recording = phase, !chordCancelled else { return nil }
        chordCancelled = true
        ignoreNextKeyUp = true
        return .discard("chord with another key")
    }
}
