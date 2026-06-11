import Foundation

struct GroqError: LocalizedError {
    let status: Int
    let body: String
    var errorDescription: String? { "Groq HTTP \(status): \(body.prefix(400))" }
}

final class GroqClient {
    var apiKey: String

    init(apiKey: String) {
        self.apiKey = apiKey
    }

    func transcribe(wav: Data, model: String, language: String, prompt: String) async throws -> String {
        let url = URL(string: "https://api.groq.com/openai/v1/audio/transcriptions")!
        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization")

        let boundary = "GroqVoice-\(UUID().uuidString)"
        req.setValue("multipart/form-data; boundary=\(boundary)", forHTTPHeaderField: "Content-Type")

        var body = Data()
        func addField(_ name: String, _ value: String) {
            body.append("--\(boundary)\r\n".data(using: .utf8)!)
            body.append("Content-Disposition: form-data; name=\"\(name)\"\r\n\r\n".data(using: .utf8)!)
            body.append("\(value)\r\n".data(using: .utf8)!)
        }
        addField("model", model)
        addField("response_format", "json")
        if !language.isEmpty { addField("language", language) }
        if !prompt.isEmpty { addField("prompt", prompt) }

        body.append("--\(boundary)\r\n".data(using: .utf8)!)
        body.append("Content-Disposition: form-data; name=\"file\"; filename=\"audio.wav\"\r\n".data(using: .utf8)!)
        body.append("Content-Type: audio/wav\r\n\r\n".data(using: .utf8)!)
        body.append(wav)
        body.append("\r\n--\(boundary)--\r\n".data(using: .utf8)!)

        let (data, resp) = try await URLSession.shared.upload(for: req, from: body)
        let status = (resp as? HTTPURLResponse)?.statusCode ?? 0
        guard status == 200 else {
            throw GroqError(status: status, body: String(data: data, encoding: .utf8) ?? "")
        }
        struct R: Decodable { let text: String }
        let r = try JSONDecoder().decode(R.self, from: data)
        return r.text.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    func chat(userText: String, model: String, systemPrompt: String) async throws -> String {
        let url = URL(string: "https://api.groq.com/openai/v1/chat/completions")!
        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization")
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")

        let payload: [String: Any] = [
            "model": model,
            "temperature": 0.3,
            "messages": [
                ["role": "system", "content": systemPrompt],
                ["role": "user", "content": userText],
            ],
        ]
        req.httpBody = try JSONSerialization.data(withJSONObject: payload)

        let (data, resp) = try await URLSession.shared.data(for: req)
        let status = (resp as? HTTPURLResponse)?.statusCode ?? 0
        guard status == 200 else {
            throw GroqError(status: status, body: String(data: data, encoding: .utf8) ?? "")
        }
        struct R: Decodable {
            struct Choice: Decodable {
                struct Msg: Decodable { let content: String }
                let message: Msg
            }
            let choices: [Choice]
        }
        let r = try JSONDecoder().decode(R.self, from: data)
        return r.choices.first?.message.content.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
    }
}
