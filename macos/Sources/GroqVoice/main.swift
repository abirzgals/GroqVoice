import Cocoa

// Headless test/maintenance mode: downloads the local Whisper model and, if
// last.wav exists, transcribes it. Useful for debugging and pre-downloading.
//   GroqVoice --download-model
if CommandLine.arguments.contains("--download-model") {
    let cfg = Config.load()
    let stt = LocalSTT(model: cfg.localWhisperModel, unloadAfterMinutes: cfg.localUnloadAfterMinutes)
    let sem = DispatchSemaphore(value: 0)
    Task {
        do {
            try await stt.ensureLoaded()
            print("model ready")
            if FileManager.default.fileExists(atPath: Recorder.wavURL.path) {
                let text = try await stt.transcribe(wavPath: Recorder.wavURL.path, language: cfg.language)
                print("local transcript: \"\(text)\"")
            }
        } catch {
            print("FAILED: \(error.localizedDescription)")
        }
        sem.signal()
    }
    sem.wait()
    exit(0)
}

let app = NSApplication.shared
let controller = AppController()
app.delegate = controller
app.setActivationPolicy(.accessory)
app.run()
