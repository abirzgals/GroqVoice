import Foundation

/// User-defined voice shortcuts for task mode: "команда = текст или инструкция".
/// The table is injected into the LLM system prompt, so matching is fuzzy —
/// "напиши мою рабочую почту" finds the "моя рабочая почта" entry.
/// Hot-reloads on file mtime change.
final class Snippets {
    static var fileURL: URL { Config.supportDir.appendingPathComponent("snippets.txt") }

    private var cachedSection = ""
    private var cachedMtime: Date?

    init() {
        if !FileManager.default.fileExists(atPath: Snippets.fileURL.path) {
            let template = """
            # GroqVoice snippets — голосовые шорткаты для task-режима.
            # Формат: команда = текст или инструкция. Строки с # игнорируются.
            # Говоришь: «задание напиши мою рабочую почту» → вставится значение.
            #
            # моя почта = ivan@example.com
            # моя рабочая почта = ivan@company.com
            # мой телефон = +371 12345678
            # мой адрес = Рига, ул. Бривибас 1
            # переведи = переведи текст на английский и выведи только перевод
            """
            try? template.data(using: .utf8)!.write(to: Snippets.fileURL)
        }
    }

    /// Returns a system-prompt section describing the shortcuts, or "" if none defined.
    func systemPromptSection() -> String {
        let url = Snippets.fileURL
        let mtime = (try? FileManager.default.attributesOfItem(atPath: url.path)[.modificationDate] as? Date) ?? nil
        if mtime == cachedMtime { return cachedSection }
        cachedMtime = mtime

        guard let text = try? String(contentsOf: url, encoding: .utf8) else {
            cachedSection = ""
            return ""
        }
        let entries = text.split(separator: "\n")
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty && !$0.hasPrefix("#") && $0.contains("=") }

        guard !entries.isEmpty else {
            cachedSection = ""
            return ""
        }

        cachedSection = """

        The user defined these personal shortcuts (spoken command = output or instruction). \
        When the request matches one of them — even approximately, in any language — use it:
        - if the right-hand side is literal data (email, phone, address, text), output it EXACTLY as written and nothing else;
        - if it is an instruction, follow that instruction for the rest of the request.
        Shortcuts:
        \(entries.joined(separator: "\n"))
        """
        Log.write("snippets loaded: \(entries.count) entries")
        return cachedSection
    }
}
