import AVFoundation
import AppKit

/// Records the active display to a timestamped .mov on the Desktop, toggled on
/// and off. Video only (no audio) — the mic stays free for push-to-talk.
/// Requires the Screen Recording permission (System Settings → Privacy).
final class ScreenRecorder: NSObject, AVCaptureFileOutputRecordingDelegate {
    private let queue = DispatchQueue(label: "groqvoice.screen")
    private var session: AVCaptureSession?
    private var output: AVCaptureMovieFileOutput?

    private(set) var isRecording = false
    private(set) var currentURL: URL?

    /// Called on the main thread when recording finishes; nil URL means failure.
    var onFinish: ((URL?) -> Void)?

    static var outputDir: URL {
        FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Desktop", isDirectory: true)
    }

    /// The display currently under the mouse — the "active" screen.
    static func activeDisplayID() -> CGDirectDisplayID {
        let mouse = NSEvent.mouseLocation
        for screen in NSScreen.screens where screen.frame.contains(mouse) {
            if let num = screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber {
                return CGDirectDisplayID(num.uint32Value)
            }
        }
        return CGMainDisplayID()
    }

    /// True once the user has granted Screen Recording. When false, the caller
    /// should prompt (requestAccess) and not start — capture would be black.
    static var hasPermission: Bool { CGPreflightScreenCaptureAccess() }
    static func requestPermission() { CGRequestScreenCaptureAccess() }

    /// Begins recording the given display. Returns false if setup fails.
    func start(displayID: CGDirectDisplayID) -> Bool {
        guard !isRecording else { return false }

        let session = AVCaptureSession()
        session.sessionPreset = .high

        guard let input = AVCaptureScreenInput(displayID: displayID) else { return false }
        input.capturesCursor = true
        input.capturesMouseClicks = true
        input.minFrameDuration = CMTime(value: 1, timescale: 30)  // 30 fps
        guard session.canAddInput(input) else { return false }
        session.addInput(input)

        let output = AVCaptureMovieFileOutput()
        guard session.canAddOutput(output) else { return false }
        session.addOutput(output)

        let url = ScreenRecorder.outputDir
            .appendingPathComponent("GroqVoice-Screen-\(ScreenRecorder.timestamp()).mov")

        self.session = session
        self.output = output
        self.currentURL = url
        self.isRecording = true

        // startRunning() can block briefly — keep it off the main thread.
        queue.async {
            session.startRunning()
            output.startRecording(to: url, recordingDelegate: self)
        }
        return true
    }

    func stop() {
        guard isRecording else { return }
        queue.async { self.output?.stopRecording() }
    }

    // MARK: - AVCaptureFileOutputRecordingDelegate

    func fileOutput(_ output: AVCaptureFileOutput,
                    didFinishRecordingTo outputFileURL: URL,
                    from connections: [AVCaptureConnection],
                    error: Error?) {
        session?.stopRunning()
        session = nil
        self.output = nil
        isRecording = false
        currentURL = nil
        if let error { Log.write("screen recording error: \(error.localizedDescription)") }
        DispatchQueue.main.async { self.onFinish?(error == nil ? outputFileURL : nil) }
    }

    private static func timestamp() -> String {
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd_HH-mm-ss"
        return f.string(from: Date())
    }
}
