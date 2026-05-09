# GroqVoice

Lightweight Windows tray app for fast voice dictation and voice-driven LLM tasks via [Groq](https://groq.com).
Hold **Win+Ctrl**, speak, release — your speech gets transcribed (Whisper) and pasted into the focused window. Start your sentence with `task` / `задача` / `задание` and the transcript is routed through Llama 3.3 70B instead, so you can say *"task: draw an ASCII cat"* and have the result pasted.

Russian + English mixed dictation works out of the box.

![status: tray app](https://img.shields.io/badge/runs%20in-system%20tray-blue)
![framework: .NET 8](https://img.shields.io/badge/.NET-8.0-512bd4)
![lang: C%23](https://img.shields.io/badge/C%23-WinForms-239120)

---

## Что это / What it does

- **Hot-key push-to-talk** — удерживаешь Win+Ctrl, говоришь, отпускаешь. Иконка в трее: 🟢 ready → 🔴 recording → 🟡 processing → 🟢.
- **Whisper STT через Groq** — `whisper-large-v3`, авто-определение языка, Russian/English code-switching без переключения настроек.
- **Vocabulary file** — словарь редких слов / имён собственных / терминов биасит распознавание (см. ниже).
- **Task mode** — если речь начинается с `task` / `задача` / `задание` (в первых 4 словах), transcript уходит в `llama-3.3-70b-versatile` и в фокусированное окно вставляется ответ модели, а не сама фраза.
- **Tray-only** — никаких окон, autostart с Windows опционально, ~40 MB RAM, ~720 KB self-contained .exe.
- **Filters** — пустые / слишком короткие записи (< 1 c, peak < 1%) не отправляются в Groq, экономя API-кредиты.

---

## Установка / Install

### Вариант 1 — Framework-dependent (рекомендуется, ~720 KB)

Требует [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (один раз ставится).

1. Скачай `GroqVoice-fd.exe` со страницы [Releases](https://github.com/abirzgals/GroqVoice/releases/latest).
2. Положи в любую постоянную папку (`%LOCALAPPDATA%\Programs\GroqVoice\` подходит).
3. Запусти. Создастся `%APPDATA%\GroqVoice\config.json` — вставь в `groqApiKey` свой ключ с [console.groq.com](https://console.groq.com) и перезапусти приложение (Tray → Quit, потом запусти .exe ещё раз).

### Вариант 2 — Self-contained (~70 MB)

Не требует ничего ставить дополнительно. Скачай `GroqVoice-sc.exe` из Releases.

### Сборка из исходников

```powershell
git clone https://github.com/abirzgals/GroqVoice.git
cd GroqVoice
.\publish.cmd
# результат: publish-fd\GroqVoice.exe (framework-dependent)
#           publish-sc\GroqVoice.exe (self-contained)
```

---

## Использование / How to use

| Действие | Что делает |
|---|---|
| Hold **Win+Ctrl**, speak, release | STT → paste транскрипта в активное окно |
| Begin with `task: …` / `задача: …` | LLM-ответ → paste в активное окно |
| **Win+Ctrl + любая клавиша** | OS shortcut работает обычно (Win+Ctrl+D, Win+Ctrl+→ и т.д.); запись отбрасывается |
| Right-click tray | Open config, Open vocabulary, Open log, Microphone picker, Start with Windows, Quit |

### Примеры task-команд

- *«задача: переведи на английский — привет, как дела»* → "Hello, how are you"
- *«task: write a regex for an email address in python»* → `^[\w\.-]+@[\w\.-]+\.\w+$`
- *«задача нарисуй ASCII-кошку»* → ASCII art пастится в редактор
- *«task: explain stale closures in react in one sentence»* → пастится одно предложение

Ключевик ловится только если он среди **первых 4 слов** транскрипта (настраивается `taskKeywordMaxWordPosition`). Так длинная фраза, в которой слово «задача» случайно встречается в середине, не превратится в LLM-запрос.

---

## Конфигурация / Configuration

`%APPDATA%\GroqVoice\config.json` (создаётся автоматически на первом старте; шаблон есть в [config.example.json](config.example.json)):

| Поле | По умолчанию | Описание |
|---|---|---|
| `groqApiKey` | `""` | Ключ с [console.groq.com](https://console.groq.com). Хранится локально в `%APPDATA%`, в репозиторий не попадает. |
| `transcriptionModel` | `whisper-large-v3` | STT-модель Groq. |
| `chatModel` | `llama-3.3-70b-versatile` | LLM для task-режима. |
| `language` | `""` (auto) | ISO-код (`ru`, `en`); пусто = автоопределение Whisper. |
| `taskKeywords` | `["task","задача","задание"]` | Триггеры task-режима. Whole-word, регистронезависимо. |
| `taskKeywordMaxWordPosition` | `4` | Сколько первых слов проверять на ключевик. |
| `inputDeviceContains` | `""` | Substring имени микрофона. Пусто = system default. Удобнее переключать через Tray → Microphone. |
| `minRecordingSeconds` | `1.0` | Записи короче этого не уходят в Groq. |
| `silencePeakPercent` | `1.0` | Порог тишины в % от full-scale 16-bit. Записи тише этого отбрасываются. |
| `saveLastWav` | `true` | Сохранять последнюю запись в `last.wav` для отладки. |
| `playFeedbackSounds` | `true` | Системные звуки на старт/стоп записи. |
| `autostart` | `true` | Запуск с Windows через `HKCU\…\Run`. |
| `taskSystemPrompt` | `""` | Кастомный system-prompt для LLM (пусто = дефолтный coder-friendly). |

---

## Словарь / Vocabulary

`%APPDATA%\GroqVoice\vocabulary.txt` — биасит Whisper к нужным словам через `prompt` параметр API.

```text
# Examples:
WhiteBIT
Resonance
FlashBot
gRPC
OAuth
Postgres
Tailscale
Dockup
```

- Одна запись на строку. `#` — комментарии.
- **Регистр имеет значение** — пишешь `WhiteBIT`, Whisper тоже так напишет.
- Hot-reload: после `Ctrl+S` следующий Win+Ctrl уже использует обновлённый список (отслеживается mtime).
- Лимит ~700 символов (Whisper принимает до 224 токенов в `prompt`); при переполнении срезается с конца по запятой.
- Открывается через **Tray → Open vocabulary…**

---

## Микрофон / Microphone

Если транскрибируется одинаковое `"you"` или `"спасибо за просмотр"` — Whisper hallucinates на тишине, потому что система выбрала виртуальный мик (Voicemod, VB-Cable, Steam Streaming, Virtual Desktop, и т.п.).

Лекарство:
1. **Tray → Microphone** — выбери реальный мик из списка.
2. ИЛИ в Windows: Settings → System → Sound → Input → выбери нужный, нажми "Test your microphone".
3. Проверь `%APPDATA%\GroqVoice\last.wav` — открывается двойным кликом, послушай что Groq получает.
4. В логе ищи строку `peak=… (NN%)` — если < 1%, это тишина; здоровая речь даёт 5–80%.

---

## Лог / Logging

`%APPDATA%\GroqVoice\log.txt`, ротация при ~1 MB → `log.1.txt`.

Открыть: **Tray → Open log**.

Что пишется:
- старт/конфиг/список устройств,
- каждая запись: `recording started` → `recording stopped: 1.74s, 55386 bytes, peak=8421 (25.71%)`,
- транскрипт: `STT result: "…"` / в task-режиме `task mode → chat: "…"` + `chat result: "…"`,
- ошибки сети / API (HTTP-код + первые 400 символов тела ответа), краши, mic-error.

---

## Архитектура

| Файл | Что делает |
|---|---|
| [Program.cs](Program.cs) | single-instance Mutex, message pump, AppDomain-level error logging |
| [TrayContext.cs](TrayContext.cs) | NotifyIcon + меню, оркестровка pipeline |
| [Hotkey.cs](Hotkey.cs) | `WH_KEYBOARD_LL` chord detector, не глотает клавиши (OS shortcuts работают) |
| [Recorder.cs](Recorder.cs) | NAudio `WaveInEvent`, 16 kHz mono PCM → in-memory WAV, peak amplitude |
| [Groq.cs](Groq.cs) | shared `HttpClient`, `/audio/transcriptions` + `/chat/completions` |
| [Paster.cs](Paster.cs) | clipboard + `SendInput` Ctrl+V; ждёт релиза модификаторов до 300 ms; восстанавливает клипборд |
| [Vocabulary.cs](Vocabulary.cs) | словарь с hot-reload по mtime |
| [Config.cs](Config.cs) / [Log.cs](Log.cs) / [Autostart.cs](Autostart.cs) | конфиг JSON, ротируемый лог, registry Run-key |

---

## Требования

- Windows 10 1809+ / Windows 11.
- .NET 8 Desktop Runtime (только для framework-dependent сборки).
- Groq API-ключ (бесплатный tier более чем достаточен для личного использования).
- Микрофон, у которого Windows реально есть сигнал.

---

## Privacy

- API-ключ хранится **только локально** в `%APPDATA%\GroqVoice\config.json`.
- Записи отправляются **только в Groq** (`api.groq.com`), нигде ещё.
- `last.wav` пишется локально для отладки, можешь отключить (`"saveLastWav": false`).
- Логи без ключей и без аудио-данных.
- Никакой телеметрии.

---

## License

MIT

