import Foundation

/// One line of vocabulary.txt: a canonical spelling plus optional aliases —
/// the ways the recognizer tends to write it ("Coolify: кулифай, кулифи").
struct VocabularyEntry: Equatable, LineFileEntry {
    var term: String
    var aliases: [String]
    /// Line in the file (kept so edits preserve comments and sections).
    var lineIndex: Int = 0

    var fileLine: String { aliases.isEmpty ? term : "\(term): \(aliases.joined(separator: ", "))" }
}

/// Loads vocabulary.txt (one term per line, `#` comments, optional
/// `Term: alias, alias`). Provides (1) a Whisper `prompt` string of canonical
/// terms for the Groq engine, (2) deterministic alias → term replacement for
/// any transcript, (3) the term count for the menu. Hot-reloads on mtime.
final class Vocabulary {
    static var fileURL: URL { Config.supportDir.appendingPathComponent("vocabulary.txt") }

    let fileURL: URL
    private let file: LineFile<VocabularyEntry>
    private var matchers: [(regex: NSRegularExpression, term: String)] = []
    private var termsPrompt = ""
    private static let maxPromptChars = 700

    init(fileURL: URL = Vocabulary.fileURL) {
        self.fileURL = fileURL
        if fileURL == Vocabulary.fileURL, !FileManager.default.fileExists(atPath: fileURL.path) {
            try? DefaultVocabulary.text.data(using: .utf8)!.write(to: fileURL)
        }
        file = LineFile(url: fileURL, parse: Vocabulary.parse)
        file.onLoad = { [unowned self] entries in
            matchers = Vocabulary.matchers(for: entries)
            var joined = entries.map(\.term).joined(separator: ", ")
            if joined.count > Vocabulary.maxPromptChars {
                let cut = joined.prefix(Vocabulary.maxPromptChars)
                joined = cut.lastIndex(of: ",").map { String(cut[..<$0]) } ?? String(cut)
            }
            termsPrompt = joined
            if !entries.isEmpty {
                Log.write("vocabulary loaded: \(entries.count) terms, \(entries.reduce(0) { $0 + $1.aliases.count }) aliases")
            }
        }
    }

    var entries: [VocabularyEntry] { file.entries }

    /// Number of real entries (ignores comments and blank lines).
    var termCount: Int { entries.count }

    /// Canonical terms joined for Whisper's `prompt` parameter (≤ ~700 chars).
    func prompt() -> String {
        _ = file.entries  // picks up a changed file
        return termsPrompt
    }

    /// Replaces whole-word occurrences of any alias (and of the term itself in
    /// other casing) with the canonical term. Returns the text and the list of
    /// replacements made, for the log.
    func applyAliases(to text: String) -> (text: String, changes: [String]) {
        _ = file.entries  // picks up a changed file
        var result = text
        var changes: [String] = []
        for matcher in matchers {
            let matches = matcher.regex.matches(in: result, range: NSRange(result.startIndex..., in: result))
            guard !matches.isEmpty else { continue }
            for m in matches.reversed() {
                guard let r = Range(m.range, in: result) else { continue }
                // A lower-case term is an ordinary word ("прод"), not a name:
                // it keeps the capital it had at the start of a sentence.
                var replacement = matcher.term
                if result[r].first?.isUppercase == true, matcher.term == matcher.term.lowercased() {
                    replacement = matcher.term.prefix(1).uppercased() + matcher.term.dropFirst()
                }
                // Skip matches that are already spelled right.
                guard result[r] != replacement else { continue }
                changes.append("\(result[r]) → \(replacement)")
                result.replaceSubrange(r, with: replacement)
            }
        }
        return (result, changes)
    }

    /// One compiled pattern per alias (and per term, for casing). Russian
    /// inflects borrowed names ("в телеграмме", "на гитхабе"), so a Cyrillic
    /// alias of five letters or more also matches with up to three trailing
    /// letters; short aliases stay exact to avoid false hits. The term itself
    /// is always exact: it is a real word, and a loose match would eat its
    /// longer neighbours ("проде" must not swallow "проделал").
    private static func matchers(for entries: [VocabularyEntry]) -> [(regex: NSRegularExpression, term: String)] {
        var out: [(NSRegularExpression, String)] = []
        for entry in entries {
            for (word, isAlias) in entry.aliases.map({ ($0, true) }) + [(entry.term, false)] where !word.isEmpty {
                let isCyrillic = word.unicodeScalars.contains { (0x0400...0x04FF).contains($0.value) }
                let suffix = (isAlias && isCyrillic && word.count >= 5) ? "[\\p{Cyrillic}]{0,3}" : ""
                let pattern = "(?<![\\p{L}\\p{N}])" + NSRegularExpression.escapedPattern(for: word) + suffix + "(?![\\p{L}\\p{N}])"
                if let re = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]) {
                    out.append((re, entry.term))
                }
            }
        }
        return out
    }

    static func parse(_ lines: [String]) -> [VocabularyEntry] {
        var out: [VocabularyEntry] = []
        for (i, raw) in lines.enumerated() {
            let line = raw.trimmingCharacters(in: .whitespaces)
            guard !line.isEmpty, !line.hasPrefix("#") else { continue }
            guard let colon = line.firstIndex(of: ":") else {
                out.append(VocabularyEntry(term: line, aliases: [], lineIndex: i))
                continue
            }
            let term = line[..<colon].trimmingCharacters(in: .whitespaces)
            let aliases = line[line.index(after: colon)...].split(separator: ",")
                .map { $0.trimmingCharacters(in: .whitespaces) }
                .filter { !$0.isEmpty }
            if !term.isEmpty { out.append(VocabularyEntry(term: term, aliases: aliases, lineIndex: i)) }
        }
        return out
    }

    // MARK: - Editing (preserves comments and sections)

    private static func entry(term: String, aliases: [String]) -> VocabularyEntry? {
        let entry = VocabularyEntry(term: term.trimmingCharacters(in: .whitespaces),
                                    aliases: aliases.map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty })
        return entry.term.isEmpty ? nil : entry
    }

    /// Appends under "# Your entries" when that section exists, else at the end.
    func add(term: String, aliases: [String]) {
        guard let entry = Vocabulary.entry(term: term, aliases: aliases) else { return }
        _ = file.entries
        let lines = file.lines
        var insertAt: Int?
        if let header = lines.firstIndex(where: { $0.trimmingCharacters(in: .whitespaces).lowercased() == "# your entries" }) {
            var i = header + 1
            while i < lines.count, !lines[i].trimmingCharacters(in: .whitespaces).isEmpty, !lines[i].hasPrefix("#") { i += 1 }
            insertAt = i
        }
        file.insert(entry, atLine: insertAt)
    }

    func update(at index: Int, term: String, aliases: [String]) {
        guard let entry = Vocabulary.entry(term: term, aliases: aliases) else { return }
        file.replace(at: index, with: entry)
    }

    func remove(at index: Int) { file.remove(at: index) }
}
