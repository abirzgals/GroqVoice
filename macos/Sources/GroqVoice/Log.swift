import Foundation

final class Log {
    static let shared = Log()
    static var fileURL: URL { Config.supportDir.appendingPathComponent("log.txt") }

    private let queue = DispatchQueue(label: "groqvoice.log")
    private let maxBytes = 1_000_000

    static func write(_ message: String) {
        shared.queue.async {
            let ts = ISO8601DateFormatter().string(from: Date())
            let line = "[\(ts)] \(message)\n"
            let url = fileURL
            shared.rotateIfNeeded(url)
            if let handle = try? FileHandle(forWritingTo: url) {
                handle.seekToEndOfFile()
                handle.write(line.data(using: .utf8)!)
                try? handle.close()
            } else {
                try? line.data(using: .utf8)!.write(to: url)
            }
        }
    }

    private func rotateIfNeeded(_ url: URL) {
        guard let size = try? FileManager.default.attributesOfItem(atPath: url.path)[.size] as? Int,
              size > maxBytes else { return }
        let rotated = Config.supportDir.appendingPathComponent("log.1.txt")
        try? FileManager.default.removeItem(at: rotated)
        try? FileManager.default.moveItem(at: url, to: rotated)
    }
}
