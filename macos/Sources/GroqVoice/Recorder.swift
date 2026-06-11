import AVFoundation
import Foundation

/// Records from the default input device to a 16 kHz mono 16-bit WAV file.
final class Recorder {
    struct Result {
        let duration: TimeInterval
        let peakPercent: Double
        let data: Data
    }

    private var recorder: AVAudioRecorder?
    private var startedAt: Date?

    static var wavURL: URL { Config.supportDir.appendingPathComponent("last.wav") }

    var isRecording: Bool { recorder != nil }

    func start() throws {
        let settings: [String: Any] = [
            AVFormatIDKey: kAudioFormatLinearPCM,
            AVSampleRateKey: 16000.0,
            AVNumberOfChannelsKey: 1,
            AVLinearPCMBitDepthKey: 16,
            AVLinearPCMIsFloatKey: false,
            AVLinearPCMIsBigEndianKey: false,
        ]
        let rec = try AVAudioRecorder(url: Recorder.wavURL, settings: settings)
        guard rec.record() else {
            throw NSError(domain: "GroqVoice", code: 1,
                          userInfo: [NSLocalizedDescriptionKey: "AVAudioRecorder.record() returned false (no microphone access?)"])
        }
        recorder = rec
        startedAt = Date()
    }

    func stop() -> Result? {
        guard let rec = recorder, let t0 = startedAt else { return nil }
        rec.stop()
        recorder = nil
        startedAt = nil

        let duration = Date().timeIntervalSince(t0)
        guard let data = try? Data(contentsOf: Recorder.wavURL) else { return nil }

        // Peak amplitude over the PCM payload. CoreAudio inserts extra chunks
        // (e.g. FLLR) before "data", so walk the RIFF chunk list properly.
        var peak = 0
        if let range = Recorder.dataChunkRange(in: data) {
            var i = range.lowerBound
            while i + 1 < range.upperBound {
                let raw = UInt16(data[i]) | (UInt16(data[i + 1]) << 8)
                let v = abs(Int(Int16(bitPattern: raw)))
                if v > peak { peak = v }
                i += 2
            }
        }
        let peakPercent = Double(peak) / 32768.0 * 100.0
        return Result(duration: duration, peakPercent: peakPercent, data: data)
    }

    func discard() {
        recorder?.stop()
        recorder = nil
        startedAt = nil
    }

    /// Returns the byte range of the "data" chunk payload in a RIFF/WAVE file.
    static func dataChunkRange(in data: Data) -> Range<Int>? {
        guard data.count > 12 else { return nil }
        var offset = 12  // past "RIFF" + size + "WAVE"
        while offset + 8 <= data.count {
            let id = data.subdata(in: offset..<offset + 4)
            let size = Int(UInt32(data[offset + 4])
                | (UInt32(data[offset + 5]) << 8)
                | (UInt32(data[offset + 6]) << 16)
                | (UInt32(data[offset + 7]) << 24))
            if id == Data("data".utf8) {
                let start = offset + 8
                return start..<min(start + size, data.count)
            }
            offset += 8 + size + (size & 1)  // chunks are word-aligned
        }
        return nil
    }
}
