import Foundation

struct Config: Codable {
    var groqApiKey = ""
    var transcriptionModel = "whisper-large-v3"
    var chatModel = "llama-3.3-70b-versatile"
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

    // Tolerate missing keys in an existing config.json so upgrades don't reset settings.
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = Config()
        groqApiKey = try c.decodeIfPresent(String.self, forKey: .groqApiKey) ?? d.groqApiKey
        transcriptionModel = try c.decodeIfPresent(String.self, forKey: .transcriptionModel) ?? d.transcriptionModel
        chatModel = try c.decodeIfPresent(String.self, forKey: .chatModel) ?? d.chatModel
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

    init() {}

    static func load() -> Config {
        if let data = try? Data(contentsOf: fileURL),
           let cfg = try? JSONDecoder().decode(Config.self, from: data) {
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
