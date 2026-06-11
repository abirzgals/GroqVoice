import Foundation

/// Loads vocabulary.txt (one term per line, # comments) and joins it into a
/// Whisper `prompt` string to bias recognition. Hot-reloads on file mtime change.
final class Vocabulary {
    static var fileURL: URL { Config.supportDir.appendingPathComponent("vocabulary.txt") }

    private var cachedPrompt = ""
    private var cachedMtime: Date?
    private let maxChars = 700

    init() {
        if !FileManager.default.fileExists(atPath: Vocabulary.fileURL.path) {
            let template = """
            # GroqVoice vocabulary — one term per line, case matters.
            # These words bias Whisper recognition (names, jargon, products).
            # Example:
            # WhiteBIT
            # gRPC
            """
            try? template.data(using: .utf8)!.write(to: Vocabulary.fileURL)
        }
    }

    func prompt() -> String {
        let url = Vocabulary.fileURL
        let mtime = (try? FileManager.default.attributesOfItem(atPath: url.path)[.modificationDate] as? Date) ?? nil
        if mtime == cachedMtime { return cachedPrompt }
        cachedMtime = mtime

        guard let text = try? String(contentsOf: url, encoding: .utf8) else {
            cachedPrompt = ""
            return ""
        }
        let terms = text.split(separator: "\n")
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty && !$0.hasPrefix("#") }

        var joined = terms.joined(separator: ", ")
        if joined.count > maxChars {
            let cut = joined.prefix(maxChars)
            if let lastComma = cut.lastIndex(of: ",") {
                joined = String(cut[..<lastComma])
            } else {
                joined = String(cut)
            }
        }
        cachedPrompt = joined
        if !joined.isEmpty {
            Log.write("vocabulary loaded: \(terms.count) terms, \(joined.count) chars")
        }
        return joined
    }
}
