import Foundation

/// Priority-ordered model list with per-model rate-limit cooldowns.
/// The strongest model is always tried first once its cooldown expires.
final class ModelChain {
    private let lock = NSLock()
    private let models: [String]
    private var cooldownUntil: [String: Date] = [:]

    init(_ models: [String]) {
        self.models = models
    }

    /// Models to try, in priority order: ready ones first, then still-cooling
    /// ones as a last resort (limits sometimes reset earlier than reported).
    func candidates() -> [String] {
        lock.lock(); defer { lock.unlock() }
        let now = Date()
        let ready = models.filter { (cooldownUntil[$0] ?? .distantPast) <= now }
        let cooling = models.filter { (cooldownUntil[$0] ?? .distantPast) > now }
        return ready + cooling
    }

    func markUnavailable(_ model: String, for seconds: TimeInterval) {
        lock.lock(); defer { lock.unlock() }
        cooldownUntil[model] = Date().addingTimeInterval(seconds)
    }
}
