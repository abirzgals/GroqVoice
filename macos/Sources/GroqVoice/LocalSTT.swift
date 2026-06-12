import Foundation
import WhisperKit

/// On-device Whisper via WhisperKit (CoreML / Neural Engine).
/// The model loads lazily on first use and unloads after an idle period to
/// free memory. Fallback mode never downloads by itself — only an explicit
/// "Download local model" (or "always" mode) triggers the download.
final class LocalSTT {
    static var modelsDir: URL { Config.supportDir.appendingPathComponent("models", isDirectory: true) }
    private static var markerURL: URL { modelsDir.appendingPathComponent("model-ready.txt") }

    private var whisper: WhisperKit?
    private var unloadTimer: Timer?
    private var loadTask: Task<WhisperKit, Error>?
    private let configuredModel: String
    private let unloadAfterSeconds: TimeInterval

    init(model: String, unloadAfterMinutes: Double) {
        configuredModel = model
        unloadAfterSeconds = max(60, unloadAfterMinutes * 60)
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
            let downloaded = self.isModelDownloaded
            Log.write("local STT: \(downloaded ? "loading" : "downloading + loading") model\(model.isEmpty ? " (auto)" : " \(model)")…")
            let started = Date()
            let cfg = WhisperKitConfig(
                model: model.isEmpty ? nil : model,
                downloadBase: LocalSTT.modelsDir,
                verbose: false,
                logLevel: .error,
                prewarm: true,
                load: true,
                download: true
            )
            let pipe = try await WhisperKit(cfg)
            let name = pipe.modelFolder?.lastPathComponent ?? (model.isEmpty ? "auto" : model)
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
        let options = DecodingOptions(
            task: .transcribe,
            language: language.isEmpty ? nil : language
        )
        let results: [TranscriptionResult] = try await pipe.transcribe(audioPath: wavPath, decodeOptions: options)
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
