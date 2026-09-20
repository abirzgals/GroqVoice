import Foundation

protocol LineFileEntry {
    /// Line of the file this entry was parsed from.
    var lineIndex: Int { get }
    /// The entry as it is written to the file.
    var fileLine: String { get }
}

/// A text file with one entry per line, where `#` comments and blank lines
/// belong to the user: edits rewrite only the lines they touch. Re-read
/// whenever the file's modification time changes, so hand edits apply at once.
final class LineFile<Entry: LineFileEntry> {
    let url: URL
    private let parse: ([String]) -> [Entry]
    private(set) var lines: [String] = []
    private var cached: [Entry] = []
    private var mtime: Date?
    private var loaded = false
    /// Called after every (re)load, before `entries` returns.
    var onLoad: (([Entry]) -> Void)?

    init(url: URL, parse: @escaping ([String]) -> [Entry]) {
        self.url = url
        self.parse = parse
    }

    var entries: [Entry] {
        reloadIfNeeded()
        return cached
    }

    /// `lineIndex` nil appends.
    func insert(_ entry: Entry, atLine lineIndex: Int? = nil) {
        reloadIfNeeded()
        lines.insert(entry.fileLine, at: min(lineIndex ?? lines.count, lines.count))
        save()
    }

    /// `index` counts entries, not lines; the entry keeps its line.
    func replace(at index: Int, with entry: Entry) {
        reloadIfNeeded()
        guard cached.indices.contains(index) else { return }
        lines[cached[index].lineIndex] = entry.fileLine
        save()
    }

    func remove(at index: Int) {
        reloadIfNeeded()
        guard cached.indices.contains(index) else { return }
        lines.remove(at: cached[index].lineIndex)
        save()
    }

    private func save() {
        try? Data((lines.joined(separator: "\n") + "\n").utf8).write(to: url)
        loaded = false  // line numbers shifted
        reloadIfNeeded()
    }

    private func reloadIfNeeded() {
        let current = (try? FileManager.default.attributesOfItem(atPath: url.path)[.modificationDate] as? Date) ?? nil
        if loaded, current == mtime { return }
        loaded = true
        mtime = current
        lines = (try? String(contentsOf: url, encoding: .utf8))?.components(separatedBy: "\n") ?? []
        if lines.last == "" { lines.removeLast() }
        cached = parse(lines)
        onLoad?(cached)
    }
}
