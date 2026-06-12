import AVFoundation
import Cocoa
import ServiceManagement

final class AppController: NSObject, NSApplicationDelegate {
    private enum Phase {
        case idle
        case recording(locked: Bool)
        case processing
    }

    private enum IconState {
        case ready, recording, locked, processing, noKey
    }

    private var statusItem: NSStatusItem!
    private var config = Config.load()
    private let recorder = Recorder()
    private let vocabulary = Vocabulary()
    private let snippets = Snippets()
    private lazy var groq = GroqClient(apiKey: config.groqApiKey,
                                       transcriptionModels: config.transcriptionModels,
                                       chatModels: config.chatModels)
    private let hotkey = HotkeyMonitor()

    private var phase: Phase = .idle
    private var fnDownAt: Date?
    private var lastQuickTapAt: Date?
    private var chordCancelled = false
    private var ignoreNextFnUp = false
    private var accessibilityRetryTimer: Timer?

    // MARK: - Lifecycle

    func applicationDidFinishLaunching(_ notification: Notification) {
        Log.write("=== GroqVoice for macOS started ===")
        setupStatusItem()
        setIcon(config.groqApiKey.isEmpty ? .noKey : .ready)

        requestMicAccess()
        startHotkeyWhenTrusted()
        syncAutostart()

        if config.groqApiKey.isEmpty {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { self.promptForApiKey(firstRun: true) }
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        hotkey.stop()
        Log.write("=== GroqVoice quit ===")
    }

    // MARK: - Permissions

    private func requestMicAccess() {
        switch AVCaptureDevice.authorizationStatus(for: .audio) {
        case .authorized:
            break
        case .notDetermined:
            AVCaptureDevice.requestAccess(for: .audio) { granted in
                Log.write("microphone access \(granted ? "granted" : "denied")")
            }
        default:
            Log.write("microphone access denied — enable in System Settings → Privacy & Security → Microphone")
        }
    }

    private func startHotkeyWhenTrusted() {
        let promptKey = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        let trusted = AXIsProcessTrustedWithOptions([promptKey: true] as CFDictionary)

        if trusted, hotkey.start() {
            wireHotkey()
            Log.write("event tap started (Fn key monitor active)")
            return
        }

        Log.write("waiting for Accessibility permission…")
        accessibilityRetryTimer = Timer.scheduledTimer(withTimeInterval: 2.0, repeats: true) { [weak self] timer in
            guard let self else { timer.invalidate(); return }
            if AXIsProcessTrusted(), self.hotkey.start() {
                timer.invalidate()
                self.accessibilityRetryTimer = nil
                self.wireHotkey()
                Log.write("event tap started after permission grant")
            }
        }
    }

    /// Mirrors config.autostart into SMAppService (Login Items). Registers only
    /// from .notRegistered so a user who disabled us in System Settings
    /// (.requiresApproval) isn't re-nagged on every launch.
    private func syncAutostart() {
        let status = SMAppService.mainApp.status
        guard config.autostart, status == .notRegistered else {
            Log.write("launch-at-login status: \(status.rawValue) (autostart=\(config.autostart))")
            return
        }
        do {
            try SMAppService.mainApp.register()
            Log.write("launch-at-login registered")
        } catch {
            Log.write("launch-at-login registration failed: \(error.localizedDescription)")
        }
    }

    // MARK: - Fn state machine

    private func wireHotkey() {
        hotkey.onFnDown = { [weak self] in self?.fnDown() }
        hotkey.onFnUp = { [weak self] in self?.fnUp() }
        hotkey.onChordKey = { [weak self] in self?.chordKey() }
    }

    private func fnDown() {
        switch phase {
        case .recording(locked: true):
            // Any Fn press stops a locked recording.
            ignoreNextFnUp = true
            finishRecording()
        case .idle:
            fnDownAt = Date()
            chordCancelled = false
            startRecording(locked: false)
        case .recording(locked: false), .processing:
            break
        }
    }

    private func fnUp() {
        if ignoreNextFnUp {
            ignoreNextFnUp = false
            return
        }
        guard case .recording(locked: false) = phase, !chordCancelled, let t0 = fnDownAt else { return }

        let heldMs = Date().timeIntervalSince(t0) * 1000
        if heldMs >= config.pttHoldMs {
            finishRecording()
            return
        }

        // Quick tap: second tap within the window locks the recording on.
        let now = Date()
        if let prev = lastQuickTapAt, now.timeIntervalSince(prev) * 1000 < config.doubleTapWindowMs {
            lastQuickTapAt = nil
            phase = .recording(locked: true)
            setIcon(.locked)
            Log.write("double-tap → recording locked on")
        } else {
            lastQuickTapAt = now
            discardRecording(reason: "single tap")
        }
    }

    private func chordKey() {
        // Fn+<key> is an OS shortcut — drop the recording.
        if case .recording = phase, !chordCancelled {
            chordCancelled = true
            ignoreNextFnUp = true
            discardRecording(reason: "Fn chord with another key")
        }
    }

    // MARK: - Recording pipeline

    private func startRecording(locked: Bool) {
        do {
            try recorder.start()
            phase = .recording(locked: locked)
            setIcon(.recording)
            playSound("Pop")
            Log.write("recording started")
        } catch {
            phase = .idle
            Log.write("mic error: \(error.localizedDescription)")
            setIcon(config.groqApiKey.isEmpty ? .noKey : .ready)
        }
    }

    private func discardRecording(reason: String) {
        recorder.discard()
        phase = .idle
        setIcon(config.groqApiKey.isEmpty ? .noKey : .ready)
        Log.write("recording discarded (\(reason))")
    }

    private func finishRecording() {
        guard let result = recorder.stop() else {
            phase = .idle
            setIcon(.ready)
            return
        }
        playSound("Tink")
        Log.write(String(format: "recording stopped: %.2fs, %d bytes, peak=%.2f%%",
                         result.duration, result.data.count, result.peakPercent))

        if result.duration < config.minRecordingSeconds {
            discardRecording(reason: "too short (< \(config.minRecordingSeconds)s)")
            return
        }
        if result.peakPercent < config.silencePeakPercent {
            discardRecording(reason: "silence (peak < \(config.silencePeakPercent)%)")
            return
        }
        if config.groqApiKey.isEmpty {
            phase = .idle
            setIcon(.noKey)
            promptForApiKey(firstRun: false)
            return
        }
        if !config.saveLastWav {
            try? FileManager.default.removeItem(at: Recorder.wavURL)
        }

        phase = .processing
        setIcon(.processing)

        let cfg = config
        let vocabPrompt = vocabulary.prompt()
        let wav = result.data

        Task { [weak self] in
            guard let self else { return }
            do {
                let transcript = try await self.groq.transcribe(
                    wav: wav, language: cfg.language, prompt: vocabPrompt)
                Log.write("STT result: \"\(transcript)\"")

                let output: String
                if let query = TaskRouter.taskQuery(from: transcript,
                                                    keywords: cfg.taskKeywords,
                                                    maxPosition: cfg.taskKeywordMaxWordPosition) {
                    Log.write("task mode → chat: \"\(query)\"")
                    let base = cfg.taskSystemPrompt.isEmpty ? TaskRouter.defaultSystemPrompt : cfg.taskSystemPrompt
                    let system = base + self.snippets.systemPromptSection()
                    output = try await self.groq.chat(userText: query, systemPrompt: system)
                    Log.write("chat result: \"\(output.prefix(200))\"")
                } else {
                    output = transcript
                }

                await MainActor.run {
                    if !output.isEmpty { Paster.paste(output) }
                    self.phase = .idle
                    self.setIcon(.ready)
                }
            } catch {
                Log.write("error: \(error.localizedDescription)")
                await MainActor.run {
                    self.phase = .idle
                    self.setIcon(.ready)
                }
            }
        }
    }

    private func playSound(_ name: String) {
        guard config.playFeedbackSounds else { return }
        NSSound(named: name)?.play()
    }

    // MARK: - Status item / menu

    private func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)

        let menu = NSMenu()
        let header = NSMenuItem(title: "GroqVoice — hold Fn to talk", action: nil, keyEquivalent: "")
        header.isEnabled = false
        menu.addItem(header)
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Set API Key…", action: #selector(menuSetApiKey), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Open Config", action: #selector(menuOpenConfig), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Open Vocabulary", action: #selector(menuOpenVocabulary), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Open Snippets", action: #selector(menuOpenSnippets), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Open Log", action: #selector(menuOpenLog), keyEquivalent: ""))
        menu.addItem(.separator())
        let login = NSMenuItem(title: "Launch at Login", action: #selector(menuToggleLogin), keyEquivalent: "")
        login.state = SMAppService.mainApp.status == .enabled ? .on : .off
        menu.addItem(login)
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Quit GroqVoice", action: #selector(menuQuit), keyEquivalent: "q"))

        for item in menu.items where item.action != nil {
            item.target = self
        }
        statusItem.menu = menu
    }

    private func setIcon(_ state: IconState) {
        guard let button = statusItem.button else { return }
        let (symbol, tint): (String, NSColor?) = {
            switch state {
            case .ready: return ("mic", nil)
            case .recording: return ("mic.fill", .systemRed)
            case .locked: return ("mic.fill", .systemOrange)
            case .processing: return ("hourglass", .systemYellow)
            case .noKey: return ("mic.slash", .systemGray)
            }
        }()
        let image = NSImage(systemSymbolName: symbol, accessibilityDescription: "GroqVoice")
        image?.isTemplate = (tint == nil)
        button.image = image
        button.contentTintColor = tint
    }

    // MARK: - Menu actions

    @objc private func menuSetApiKey() { promptForApiKey(firstRun: false) }

    @objc private func menuOpenConfig() { NSWorkspace.shared.open(Config.fileURL) }
    @objc private func menuOpenVocabulary() { NSWorkspace.shared.open(Vocabulary.fileURL) }
    @objc private func menuOpenSnippets() { NSWorkspace.shared.open(Snippets.fileURL) }
    @objc private func menuOpenLog() { NSWorkspace.shared.open(Log.fileURL) }

    @objc private func menuToggleLogin(_ sender: NSMenuItem) {
        do {
            if SMAppService.mainApp.status == .enabled {
                try SMAppService.mainApp.unregister()
                config.autostart = false
                sender.state = .off
                Log.write("launch-at-login disabled")
            } else {
                try SMAppService.mainApp.register()
                config.autostart = true
                sender.state = .on
                Log.write("launch-at-login enabled")
            }
            config.save()
        } catch {
            Log.write("launch-at-login toggle failed: \(error.localizedDescription)")
            NSApp.activate(ignoringOtherApps: true)
            let alert = NSAlert()
            alert.messageText = "Launch at Login failed"
            alert.informativeText = "macOS rejected the Login Item change: \(error.localizedDescription)\n\nMake sure GroqVoice.app is in /Applications, or toggle it manually in System Settings → General → Login Items."
            alert.runModal()
        }
    }

    @objc private func menuQuit() { NSApp.terminate(nil) }

    private func promptForApiKey(firstRun: Bool) {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = firstRun ? "Welcome to GroqVoice" : "Groq API Key"
        alert.informativeText = firstRun
            ? "Paste your Groq API key (free at console.groq.com).\n\nThen grant Accessibility and Microphone permissions when macOS asks — both are required for the Fn hotkey and recording.\n\nTip: set System Settings → Keyboard → “Press 🌐 key to” → “Do Nothing”, otherwise double-tap Fn opens macOS dictation."
            : "Paste your Groq API key from console.groq.com."
        let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 360, height: 24))
        field.placeholderString = "gsk_…"
        field.stringValue = config.groqApiKey
        alert.accessoryView = field
        alert.addButton(withTitle: "Save")
        alert.addButton(withTitle: "Cancel")
        alert.window.initialFirstResponder = field

        if alert.runModal() == .alertFirstButtonReturn {
            config.groqApiKey = field.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            config.save()
            groq.apiKey = config.groqApiKey
            setIcon(config.groqApiKey.isEmpty ? .noKey : .ready)
            Log.write("API key updated (\(config.groqApiKey.isEmpty ? "empty" : "set"))")
        }
    }
}
