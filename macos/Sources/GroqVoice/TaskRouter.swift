import Foundation

enum TaskRouter {
    static let defaultSystemPrompt = """
    You are the command processor of a voice-dictation tool. The user held a key, \
    spoke a command, and it was transcribed. Your entire output is pasted directly \
    into whatever text field the user is focused on, so it must be ONLY the final \
    result to paste — no preamble, no explanations, no surrounding quotes, no \
    markdown code fences (unless the user explicitly asked for code).

    Do EXACTLY what the command says, applied to the text or content in the command — \
    nothing more, nothing less. If it is an instruction to transform text (translate, \
    rephrase, shorten, fix grammar, reformat), transform exactly the given text and \
    output only the transformed text. If it asks you to produce something, output only \
    that. Never answer a question about the command instead of performing it.

    Reply in the language the command implies: translations use the requested target \
    language; otherwise match the language of the command. Do not refuse, do not ask \
    clarifying questions, do not add notes or sign-offs — output only the text to paste.
    """

    /// Task mode triggers ONLY when the transcript's FIRST word is a task keyword
    /// (e.g. "задание переведи …"). This is deliberately strict: a keyword that
    /// merely appears mid-sentence ("а если задание, то…") must NOT be executed —
    /// it is plain dictation and should be transcribed verbatim.
    ///
    /// `maxPosition` bounds how many leading words may be scanned (default 1 = the
    /// first word only); raise it in config to allow a short lead-in before the keyword.
    /// Returns the command text after the keyword, or nil if this isn't a task.
    static func taskQuery(from transcript: String, keywords: [String], maxPosition: Int) -> String? {
        let words = transcript.split(whereSeparator: { $0.isWhitespace })
        let lowered = keywords.map { $0.lowercased() }
        let scan = max(1, maxPosition)

        for (i, word) in words.prefix(scan).enumerated() {
            let clean = word.trimmingCharacters(in: .punctuationCharacters).lowercased()
            guard lowered.contains(clean) else { continue }

            let rest = words.dropFirst(i + 1).joined(separator: " ")
            let trimmed = rest.trimmingCharacters(in: CharacterSet(charactersIn: " \t:,.—–-"))
            return trimmed.isEmpty ? nil : trimmed
        }
        return nil
    }
}
