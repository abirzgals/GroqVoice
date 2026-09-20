import Foundation

struct HistoryEntry: Codable {
    enum Kind: String, Codable {
        case dictation, snippet, edit, translate, prompt, task

        /// SF Symbol shown next to the entry; plain dictation has none.
        var symbolName: String? {
            switch self {
            case .dictation: return nil
            case .snippet: return "text.badge.plus"
            case .edit: return "pencil"
            case .translate: return "globe"
            case .prompt: return "wand.and.stars"
            case .task: return "sparkles"
            }
        }

        // A kind written by some other version still loads.
        init(from decoder: Decoder) throws {
            self = Kind(rawValue: try decoder.singleValueContainer().decode(String.self)) ?? .dictation
        }
    }

    let time: Date
    let kind: Kind
    let text: String
    /// What went into the language model (the speech, or the selection it
    /// replaced) when `text` is the model's answer — so a bad rewrite or
    /// translation never costs the original.
    var source: String?

    /// Single-line, shortened form for menus.
    var menuTitle: String {
        let oneLine = text.replacingOccurrences(of: "\n", with: " ")
            .replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression)
            .trimmingCharacters(in: .whitespaces)
        return oneLine.count > 60 ? String(oneLine.prefix(57)) + "…" : oneLine
    }
}

/// Keeps the last N outputs (dictations and task answers) in history.jsonl so
/// a paste that landed in the wrong window can be recovered from the menu.
final class History {
    static var fileURL: URL { Config.supportDir.appendingPathComponent("history.jsonl") }

    /// Oldest first.
    private(set) var entries: [HistoryEntry] = [] {
        didSet { onChange?() }
    }
    /// Called on the main thread after any change.
    var onChange: (() -> Void)?
    var limit: Int {
        didSet {
            limit = max(1, limit)
            guard entries.count > limit else { return }
            entries.removeFirst(entries.count - limit)
            save()
        }
    }
    private let encoder = JSONEncoder()
    private let decoder = JSONDecoder()

    init(limit: Int) {
        self.limit = max(1, limit)
        encoder.dateEncodingStrategy = .iso8601
        decoder.dateDecodingStrategy = .iso8601
        load()
    }

    var latest: HistoryEntry? { entries.last }

    func add(_ text: String, kind: HistoryEntry.Kind, source: String? = nil) {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return }
        let original = source?.trimmingCharacters(in: .whitespacesAndNewlines)
        entries.append(HistoryEntry(time: Date(), kind: kind, text: trimmed,
                                    source: original == trimmed || original?.isEmpty == true ? nil : original))
        if entries.count > limit { entries.removeFirst(entries.count - limit) }
        save()
    }

    func clear() {
        entries = []
        try? FileManager.default.removeItem(at: History.fileURL)
    }

    private func load() {
        guard let raw = try? String(contentsOf: History.fileURL, encoding: .utf8) else { return }
        entries = raw.split(separator: "\n").compactMap { line in
            try? decoder.decode(HistoryEntry.self, from: Data(line.utf8))
        }
        if entries.count > limit { entries.removeFirst(entries.count - limit) }
    }

    private func save() {
        let lines = entries.compactMap { entry -> String? in
            guard let data = try? encoder.encode(entry) else { return nil }
            return String(decoding: data, as: UTF8.self)
        }
        try? (lines.joined(separator: "\n") + "\n").data(using: .utf8)?.write(to: History.fileURL)
    }
}
