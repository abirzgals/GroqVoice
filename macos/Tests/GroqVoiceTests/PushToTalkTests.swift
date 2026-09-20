import Foundation
import Testing
@testable import GroqVoice

@Suite struct PushToTalkTests {
    private let cfg = Config()   // hold ≥ 250 ms, double-tap window 400 ms
    private let t0 = Date(timeIntervalSince1970: 1_000)
    private func at(_ ms: Double) -> Date { t0.addingTimeInterval(ms / 1000) }
    private let recording = PushToTalk.Phase.recording(locked: false)
    private let locked = PushToTalk.Phase.recording(locked: true)

    @Test func holdingTheKeyRecordsAndReleasingFinishes() {
        var ptt = PushToTalk()
        #expect(ptt.keyDown(.fn, phase: .idle, inTail: false, now: at(0)) == .start(.fn))
        #expect(ptt.keyUp(.fn, phase: recording, cfg: cfg, now: at(900)) { false } == .finish)
    }

    @Test func aLoneTapIsDiscardedAndASecondOneLocks() {
        var ptt = PushToTalk()
        _ = ptt.keyDown(.fn, phase: .idle, inTail: false, now: at(0))
        #expect(ptt.keyUp(.fn, phase: recording, cfg: cfg, now: at(80)) { false } == .discard("single tap"))
        #expect(ptt.keyDown(.fn, phase: .idle, inTail: false, now: at(200)) == .start(.fn))
        #expect(ptt.keyUp(.fn, phase: recording, cfg: cfg, now: at(280)) { false } == .lock)
        // Locked: the same key stops it, and that press's release means nothing.
        #expect(ptt.keyDown(.rightCommand, phase: locked, inTail: false, now: at(5_000)) == nil)
        #expect(ptt.keyDown(.fn, phase: locked, inTail: false, now: at(5_100)) == .finish)
        #expect(ptt.keyUp(.fn, phase: recording, cfg: cfg, now: at(5_200)) { false } == nil)
    }

    @Test func twoTapsFarApartAreTwoLoneTaps() {
        var ptt = PushToTalk()
        _ = ptt.keyDown(.fn, phase: .idle, inTail: false, now: at(0))
        _ = ptt.keyUp(.fn, phase: recording, cfg: cfg, now: at(80)) { false }
        _ = ptt.keyDown(.fn, phase: .idle, inTail: false, now: at(1_000))
        #expect(ptt.keyUp(.fn, phase: recording, cfg: cfg, now: at(1_080)) { false } == .discard("single tap"))
    }

    @Test func aChordCancelsTheTakeOnceAndSwallowsTheRelease() {
        var ptt = PushToTalk()
        _ = ptt.keyDown(.leftCommand, phase: .idle, inTail: false, now: at(0))
        #expect(ptt.chord(phase: recording) == .discard("chord with another key"))
        #expect(ptt.chord(phase: .idle) == nil)
        #expect(ptt.keyUp(.leftCommand, phase: .idle, cfg: cfg, now: at(600)) { false } == nil)
        // The next press is a fresh take.
        #expect(ptt.keyDown(.leftCommand, phase: .idle, inTail: false, now: at(2_000)) == .start(.leftCommand))
        #expect(ptt.keyUp(.leftCommand, phase: recording, cfg: cfg, now: at(3_000)) { false } == .finish)
    }

    @Test func pressingAgainDuringTheReleaseTailContinuesTheTake() {
        var ptt = PushToTalk()
        _ = ptt.keyDown(.fn, phase: .idle, inTail: false, now: at(0))
        _ = ptt.keyUp(.fn, phase: recording, cfg: cfg, now: at(900)) { false }
        #expect(ptt.keyDown(.fn, phase: recording, inTail: true, now: at(950)) == .resume)
        #expect(ptt.keyDown(.fn, phase: recording, inTail: false, now: at(960)) == nil)
        // Held again long enough → finishes; the hold is measured from the new press.
        #expect(ptt.keyUp(.fn, phase: recording, cfg: cfg, now: at(1_500)) { false } == .finish)
    }

    @Test func aTapOnAnActionKeyWithASelectionRunsTheAction() {
        var ptt = PushToTalk()
        _ = ptt.keyDown(.leftControl, phase: .idle, inTail: false, now: at(0))
        #expect(ptt.keyUp(.leftControl, phase: recording, cfg: cfg, now: at(60)) { true } == .finish)
    }

    @Test func otherKeysAndBusyPhasesAreIgnored() {
        var ptt = PushToTalk()
        #expect(ptt.keyDown(.fn, phase: .processing, inTail: false, now: at(0)) == nil)
        _ = ptt.keyDown(.fn, phase: .idle, inTail: false, now: at(100))
        #expect(ptt.keyUp(.rightOption, phase: recording, cfg: cfg, now: at(900)) { false } == nil)
        #expect(ptt.keyDown(.rightOption, phase: recording, inTail: true, now: at(900)) == nil)
    }
}
