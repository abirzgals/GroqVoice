import Foundation
import WhisperKit

/// On-device Whisper via WhisperKit (CoreML / Neural Engine).
/// The model loads lazily on first use and unloads after an idle period to
/// free memory. Fallback mode never downloads by itself — only an explicit
/// "Download local model" (or "always" mode) triggers the download.
final class LocalSTT {
    /// Reported to the UI so the menu-bar icon can show progress. Only fires
    /// during on-device transcription — the Groq path never emits these.
    enum Stage {
        case downloadingModel(Double)  // 0...1 — real Hugging Face download progress
        case loadingModel              // indeterminate — CoreML compile + prewarm
        case transcribing(Double)      // 0...1
    }

    static var modelsDir: URL { Config.supportDir.appendingPathComponent("models", isDirectory: true) }
    private static var markerURL: URL { modelsDir.appendingPathComponent("model-ready.txt") }

    /// Called on the main thread with the current stage.
    var onStage: ((Stage) -> Void)?

    private var whisper: WhisperKit?
    private var unloadTimer: Timer?
    private var loadTask: Task<WhisperKit, Error>?
    private let configuredModel: String
    private let unloadAfterSeconds: TimeInterval

    init(model: String, unloadAfterMinutes: Double) {
        configuredModel = model
        unloadAfterSeconds = max(60, unloadAfterMinutes * 60)
    }

    private func emit(_ stage: Stage) {
        DispatchQueue.main.async { [weak self] in self?.onStage?(stage) }
    }

    var isModelDownloaded: Bool {
        FileManager.default.fileExists(atPath: LocalSTT.markerURL.path)
    }

    var isLoaded: Bool { whisper != nil }

    /// Downloads (if needed) and loads the model. Safe to call concurrently.
    @discardableResult
    func ensureLoaded() async throws -> WhisperKit {
        if let whisper { return whisper }
        if let loadTask { return try await loadTask.value }

        let model = configuredModel
        let task = Task<WhisperKit, Error> {
            let started = Date()
            let variant = model.isEmpty ? WhisperKit.recommendedModels().default : model

            // 1) Ensure the model is on disk, reporting real download progress.
            var downloadedFolder: URL?
            if !self.isModelDownloaded {
                Log.write("local STT: downloading model \(variant)…")
                self.emit(.downloadingModel(0))
                downloadedFolder = try await WhisperKit.download(
                    variant: variant,
                    downloadBase: LocalSTT.modelsDir
                ) { progress in
                    let f = progress.fractionCompleted
                    if f.isFinite { self.emit(.downloadingModel(f)) }
                }
                Log.write(String(format: "local STT: downloaded in %.1fs", Date().timeIntervalSince(started)))
            }

            // 2) Compile + prewarm into memory (no fine-grained progress available).
            self.emit(.loadingModel)
            Log.write("local STT: loading model \(variant)…")
            let cfg = WhisperKitConfig(
                model: model.isEmpty ? nil : model,
                downloadBase: LocalSTT.modelsDir,
                modelFolder: downloadedFolder?.path,
                verbose: false,
                logLevel: .error,
                prewarm: true,
                load: true,
                download: true
            )
            let pipe = try await WhisperKit(cfg)
            let name = pipe.modelFolder?.lastPathComponent ?? variant
            try? name.data(using: .utf8)!.write(to: LocalSTT.markerURL)
            Log.write(String(format: "local STT: model %@ ready in %.1fs", name, Date().timeIntervalSince(started)))
            return pipe
        }
        loadTask = task
        defer { loadTask = nil }

        do {
            let pipe = try await task.value
            whisper = pipe
            return pipe
        } catch {
            Log.write("local STT: model load failed: \(error.localizedDescription)")
            throw error
        }
    }

    func transcribe(wavPath: String, language: String) async throws -> String {
        let pipe = try await ensureLoaded()
        // Unlike the Groq API, WhisperKit does NOT auto-detect language by
        // default — with no language it force-feeds the <|en|> token.
        let options = DecodingOptions(
            task: .transcribe,
            language: language.isEmpty ? nil : language,
            detectLanguage: language.isEmpty
        )

        // Poll WhisperKit's Progress (advances as it seeks through the audio)
        // and report percentage until transcription returns.
        emit(.transcribing(0))
        let poller = Task { [weak self] in
            while !Task.isCancelled {
                let f = pipe.progress.fractionCompleted
                if f.isFinite { self?.emit(.transcribing(f)) }
                try? await Task.sleep(nanoseconds: 120_000_000)
            }
        }
        defer { poller.cancel() }

        let results: [TranscriptionResult] = try await pipe.transcribe(audioPath: wavPath, decodeOptions: options)
        emit(.transcribing(1))
        scheduleUnload()
        return results.map(\.text).joined(separator: " ")
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// Frees the model after a period of inactivity to save memory.
    private func scheduleUnload() {
        DispatchQueue.main.async { [self] in
            unloadTimer?.invalidate()
            unloadTimer = Timer.scheduledTimer(withTimeInterval: unloadAfterSeconds, repeats: false) { [weak self] _ in
                guard let self, self.whisper != nil else { return }
                self.whisper = nil
                Log.write("local STT: model unloaded after \(Int(self.unloadAfterSeconds / 60)) min idle")
            }
        }
    }
}
