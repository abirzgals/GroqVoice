import Foundation

enum TaskRouter {
    static let defaultSystemPrompt = """
    You are a concise assistant invoked by voice dictation. Output only the result — \
    no preamble, no explanations, no markdown fences unless explicitly asked for code. \
    If asked to translate, output only the translation. \
    Reply in the language of the request unless told otherwise.
    """

    /// If the transcript starts with a task keyword (within the first `maxPosition`
    /// words), returns the remaining text to send to the LLM. Otherwise nil.
    static func taskQuery(from transcript: String, keywords: [String], maxPosition: Int) -> String? {
        let words = transcript.split(whereSeparator: { $0.isWhitespace })
        let lowered = keywords.map { $0.lowercased() }

        for (i, word) in words.prefix(maxPosition).enumerated() {
            let clean = word.trimmingCharacters(in: .punctuationCharacters).lowercased()
            guard lowered.contains(clean) else { continue }

            let rest = words.dropFirst(i + 1).joined(separator: " ")
            let trimmed = rest.trimmingCharacters(in: CharacterSet(charactersIn: " \t:,.—–-"))
            return trimmed.isEmpty ? nil : trimmed
        }
        return nil
    }
}
