import Foundation

struct Config: Codable {
    var groqApiKey = ""
    /// Priority order: strongest first. On a rate limit the next model is used;
    /// the stronger one is retried automatically once its cooldown expires.
    var transcriptionModels = ["whisper-large-v3", "whisper-large-v3-turbo"]
    var chatModels = ["llama-3.3-70b-versatile", "openai/gpt-oss-120b", "llama-3.1-8b-instant"]
    var language = ""
    var taskKeywords = ["task", "задача", "задание"]
    var taskKeywordMaxWordPosition = 4
    var minRecordingSeconds = 1.0
    var silencePeakPercent = 1.0
    var saveLastWav = true
    var playFeedbackSounds = true
    var taskSystemPrompt = ""
    var pttHoldMs = 250.0
    var doubleTapWindowMs = 400.0

    static var supportDir: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        let dir = base.appendingPathComponent("GroqVoice", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    static var fileURL: URL { supportDir.appendingPathComponent("config.json") }

    enum CodingKeys: String, CodingKey {
        case groqApiKey, transcriptionModels, chatModels, language
        case taskKeywords, taskKeywordMaxWordPosition
        case minRecordingSeconds, silencePeakPercent
        case saveLastWav, playFeedbackSounds, taskSystemPrompt
        case pttHoldMs, doubleTapWindowMs
        // Legacy single-model keys, migrated to the list fields on load.
        case transcriptionModel, chatModel
    }

    init() {}

    // Tolerate missing keys in an existing config.json so upgrades don't reset settings.
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = Config()
        groqApiKey = try c.decodeIfPresent(String.self, forKey: .groqApiKey) ?? d.groqApiKey

        if let list = try c.decodeIfPresent([String].self, forKey: .transcriptionModels) {
            transcriptionModels = list
        } else if let legacy = try c.decodeIfPresent(String.self, forKey: .transcriptionModel) {
            transcriptionModels = [legacy] + d.transcriptionModels.filter { $0 != legacy }
        }
        if let list = try c.decodeIfPresent([String].self, forKey: .chatModels) {
            chatModels = list
        } else if let legacy = try c.decodeIfPresent(String.self, forKey: .chatModel) {
            chatModels = [legacy] + d.chatModels.filter { $0 != legacy }
        }

        language = try c.decodeIfPresent(String.self, forKey: .language) ?? d.language
        taskKeywords = try c.decodeIfPresent([String].self, forKey: .taskKeywords) ?? d.taskKeywords
        taskKeywordMaxWordPosition = try c.decodeIfPresent(Int.self, forKey: .taskKeywordMaxWordPosition) ?? d.taskKeywordMaxWordPosition
        minRecordingSeconds = try c.decodeIfPresent(Double.self, forKey: .minRecordingSeconds) ?? d.minRecordingSeconds
        silencePeakPercent = try c.decodeIfPresent(Double.self, forKey: .silencePeakPercent) ?? d.silencePeakPercent
        saveLastWav = try c.decodeIfPresent(Bool.self, forKey: .saveLastWav) ?? d.saveLastWav
        playFeedbackSounds = try c.decodeIfPresent(Bool.self, forKey: .playFeedbackSounds) ?? d.playFeedbackSounds
        taskSystemPrompt = try c.decodeIfPresent(String.self, forKey: .taskSystemPrompt) ?? d.taskSystemPrompt
        pttHoldMs = try c.decodeIfPresent(Double.self, forKey: .pttHoldMs) ?? d.pttHoldMs
        doubleTapWindowMs = try c.decodeIfPresent(Double.self, forKey: .doubleTapWindowMs) ?? d.doubleTapWindowMs
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(groqApiKey, forKey: .groqApiKey)
        try c.encode(transcriptionModels, forKey: .transcriptionModels)
        try c.encode(chatModels, forKey: .chatModels)
        try c.encode(language, forKey: .language)
        try c.encode(taskKeywords, forKey: .taskKeywords)
        try c.encode(taskKeywordMaxWordPosition, forKey: .taskKeywordMaxWordPosition)
        try c.encode(minRecordingSeconds, forKey: .minRecordingSeconds)
        try c.encode(silencePeakPercent, forKey: .silencePeakPercent)
        try c.encode(saveLastWav, forKey: .saveLastWav)
        try c.encode(playFeedbackSounds, forKey: .playFeedbackSounds)
        try c.encode(taskSystemPrompt, forKey: .taskSystemPrompt)
        try c.encode(pttHoldMs, forKey: .pttHoldMs)
        try c.encode(doubleTapWindowMs, forKey: .doubleTapWindowMs)
    }

    static func load() -> Config {
        if let data = try? Data(contentsOf: fileURL),
           let cfg = try? JSONDecoder().decode(Config.self, from: data) {
            cfg.save()  // rewrite in the current schema (migrates legacy keys)
            return cfg
        }
        let cfg = Config()
        cfg.save()
        return cfg
    }

    func save() {
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        if let data = try? enc.encode(self) {
            try? data.write(to: Config.fileURL)
        }
    }
}
