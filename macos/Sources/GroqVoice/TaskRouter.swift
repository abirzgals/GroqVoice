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

    /// Imperative words that may precede a task keyword and still form a command,
    /// e.g. "выполни задание …", "please do task …". Anything else before the
    /// keyword means it's ordinary speech, not a command.
    static let commandLeadIns: Set<String> = [
        "выполни", "выполнить", "сделай", "сделать", "запусти", "запустить",
        "дай", "пожалуйста",
        "do", "run", "execute", "perform", "make", "please",
    ]

    /// Task mode triggers when a keyword ("задание"/"task"/…) appears at the start,
    /// optionally after imperative lead-in words ("выполни задание …"). It does NOT
    /// trigger when the keyword merely appears inside a normal sentence
    /// ("а если задание, то…") — that stays plain dictation, transcribed verbatim.
    ///
    /// `maxPosition` bounds how many leading words are scanned. Returns the command
    /// text after the keyword, or nil if this isn't a task.
    static func taskQuery(from transcript: String, keywords: [String], maxPosition: Int) -> String? {
        let words = transcript.split(whereSeparator: { $0.isWhitespace })
        let lowered = Set(keywords.map { $0.lowercased() })
        let scan = max(1, maxPosition)

        func clean(_ s: Substring) -> String {
            s.trimmingCharacters(in: .punctuationCharacters).lowercased()
        }

        for (i, word) in words.prefix(scan).enumerated() {
            guard lowered.contains(clean(word)) else { continue }
            // Command only if every word before the keyword is a lead-in.
            let preceding = words.prefix(i).map(clean)
            guard preceding.allSatisfy({ commandLeadIns.contains($0) }) else { continue }

            let rest = words.dropFirst(i + 1).joined(separator: " ")
            let trimmed = rest.trimmingCharacters(in: CharacterSet(charactersIn: " \t:,.—–-"))
            return trimmed.isEmpty ? nil : trimmed
        }
        return nil
    }
}
