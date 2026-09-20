import Foundation

final class Log {
    static let shared = Log()
    static var fileURL: URL { Config.supportDir.appendingPathComponent("log.txt") }

    private let queue = DispatchQueue(label: "groqvoice.log")
    private let maxBytes = 1_000_000
    private var handle: FileHandle?
    private var written = 0
    /// Millisecond timestamps: latency work needs to see where a take's time goes.
    private let formatter: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    static func write(_ message: String) {
        let now = Date()
        shared.queue.async { shared.append(message, at: now) }
    }

    private func append(_ message: String, at time: Date) {
        let line = Data("[\(formatter.string(from: time))] \(message)\n".utf8)
        if handle == nil { open() }
        if written + line.count > maxBytes { rotate() }
        handle?.write(line)
        written += line.count
    }

    /// The log holds everything that was dictated — owner-only, like the rest
    /// of the data folder.
    private func open() {
        let url = Log.fileURL
        if !FileManager.default.fileExists(atPath: url.path) {
            FileManager.default.createFile(atPath: url.path, contents: nil, attributes: [.posixPermissions: 0o600])
        }
        handle = try? FileHandle(forWritingTo: url)
        written = Int((try? handle?.seekToEnd()) ?? 0)
    }

    private func rotate() {
        try? handle?.close()
        handle = nil
        let rotated = Config.supportDir.appendingPathComponent("log.1.txt")
        try? FileManager.default.removeItem(at: rotated)
        try? FileManager.default.moveItem(at: Log.fileURL, to: rotated)
        open()
    }
}
