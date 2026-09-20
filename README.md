# GroqVoice

# ⬇️ Download

### 👉 **[GroqVoice-fd.exe](https://github.com/abirzgals/GroqVoice/releases/latest/download/GroqVoice-fd.exe)** &nbsp;&nbsp; *(22 MB, recommended — needs [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0))*

### 👉 **[GroqVoice-sc.exe](https://github.com/abirzgals/GroqVoice/releases/latest/download/GroqVoice-sc.exe)** &nbsp;&nbsp; *(77 MB, self-contained — nothing else to install)*

[![Download recommended](https://img.shields.io/badge/⬇-GroqVoice--fd.exe%20%E2%80%94%2022%20MB-2ea44f?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/abirzgals/GroqVoice/releases/latest/download/GroqVoice-fd.exe)
[![Download self-contained](https://img.shields.io/badge/⬇-GroqVoice--sc.exe%20%E2%80%94%2077%20MB-1f6feb?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/abirzgals/GroqVoice/releases/latest/download/GroqVoice-sc.exe)
[![All releases](https://img.shields.io/badge/All-Releases-6e7681?style=for-the-badge&logo=github&logoColor=white)](https://github.com/abirzgals/GroqVoice/releases)

> Click any link above and the .exe starts downloading immediately — no need to dig through GitHub's UI.
> **Windows SmartScreen** may warn the .exe is from an unidentified publisher (it isn't code-signed yet). Click **More info → Run anyway**.

### 🍎 macOS (Apple Silicon, macOS 14+)

Нативная Mac-версия (Swift, menu bar, push-to-talk на **Fn/🌐**). Распознаёт **на самом Маке** —
NVIDIA Parakeet TDT v3 через [FluidAudio](https://github.com/FluidInference/FluidAudio) (CoreML /
Neural Engine): офлайн, без аккаунта, ~0.2 с на фразу. Groq остаётся облачной опцией и мозгом для
task-режима, перевода и сниппетов. Сборка из исходников (нужны Command Line Tools):

```bash
cd macos && ./build-app.sh && ditto GroqVoice.app /Applications/GroqVoice.app && open /Applications/GroqVoice.app
```

При первом запуске разреши Microphone и Accessibility (промпты откроются сами).

> Готовые **[GroqVoice-mac.zip](https://github.com/abirzgals/GroqVoice/releases/latest/download/GroqVoice-mac.zip)** / `.dmg` в релизах — это ещё прошлая, облачная сборка (июнь); локальный Parakeet пока только из исходников.

Возможности, настройки, словарь и сниппеты — в [macos/README.md](macos/README.md). Страница загрузки: **[abirzgals.github.io/GroqVoice](https://abirzgals.github.io/GroqVoice/)**

---

## What it is

Lightweight Windows tray app for fast voice dictation and voice-driven LLM tasks via [Groq](https://groq.com).
Hold **Win+Ctrl**, speak, release — your speech gets transcribed (Whisper) and pasted into the focused window. Start your sentence with `task` / `задача` / `задание` and the transcript is routed through Llama 3.3 70B instead, so you can say *"task: draw an ASCII cat"* and have the result pasted.

Russian + English mixed dictation works out of the box.

![framework: .NET 8](https://img.shields.io/badge/.NET-8.0-512bd4) ![lang: C%23](https://img.shields.io/badge/C%23-WinForms-239120) ![runs in: system tray](https://img.shields.io/badge/runs%20in-system%20tray-blue)

---

## Что это / What it does

- **Hot-key push-to-talk** — удерживаешь Win+Ctrl, говоришь, отпускаешь. Иконка в трее: 🟢 ready → 🔴 recording → 🟡 processing → 🟢.
- **Два движка распознавания** — Whisper через Groq (`whisper-large-v3`, облако) или **Parakeet TDT v3 прямо на этом ПК** (офлайн, без API-ключа, ~0.35 с на фразу). Переключается в трее: Recognition → Parakeet v3; модель (460 МБ) скачивается по запросу, с подтверждением и процентами. Оба движка держат смешанную русско-английскую речь без смены настроек.
- **Vocabulary file** — словарь редких слов / имён собственных / терминов биасит распознавание (см. ниже).
- **Task mode** — если речь начинается с `task` / `задача` / `задание` (в первых 4 словах), transcript уходит в `llama-3.3-70b-versatile` и в фокусированное окно вставляется ответ модели, а не сама фраза.
- **Tray-only** — никаких окон, autostart с Windows опционально, ~40 MB RAM (~740 MB, пока загружена локальная модель).
- **Filters** — пустые / слишком короткие записи (< 1 c, peak < 1%) не отправляются в распознавание, экономя API-кредиты и время.

---

## Установка / Install

### Вариант 1 — Framework-dependent (рекомендуется, ~22 MB)

Требует [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (один раз ставится).

1. Скачай `GroqVoice-fd.exe` со страницы [Releases](https://github.com/abirzgals/GroqVoice/releases/latest).
2. Положи в любую постоянную папку (`%LOCALAPPDATA%\Programs\GroqVoice\` подходит).
3. Запусти. Создастся `%APPDATA%\GroqVoice\config.json` — вставь в `groqApiKey` свой ключ с [console.groq.com](https://console.groq.com) и перезапусти приложение (Tray → Quit, потом запусти .exe ещё раз).

### Вариант 2 — Self-contained (~77 MB)

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
| **Hold Win+Ctrl**, speak, release | Push-to-talk: STT → paste транскрипта в активное окно |
| **Double-tap Win+Ctrl** (быстро два раза) | Toggle: запись стартует и держится. Любой следующий tap её останавливает. |
| **Win+Ctrl+Alt** (все три вместе) | Snipping: экран фризится, мышкой выделяешь область → картинка в clipboard |
| Begin with `task: …` / `задача: …` | LLM-ответ → paste в активное окно |
| **Win+Ctrl + любая клавиша** | OS shortcut работает обычно (Win+Ctrl+D, Win+Ctrl+→ и т.д.); запись отбрасывается |
| Right-click tray | Setup, Open config / vocabulary / log, Microphone picker, Start with Windows, Quit |

**Hotkey-режимы.** Удерживание = push-to-talk (для коротких реплик, как сейчас). Быстрый двойной тап Win+Ctrl = lock-on: можно отпустить клавиши и говорить хоть минуту, любой одиночный нажим клавишь Win+Ctrl завершает. Граница между «тап» и «hold» — 250 мс (`pttHoldMs`); окно второго тапа — 400 мс (`doubleTapWindowMs`).

### Примеры task-команд

- *«задача: переведи на английский — привет, как дела»* → "Hello, how are you"
- *«task: write a regex for an email address in python»* → `^[\w\.-]+@[\w\.-]+\.\w+$`
- *«задача нарисуй ASCII-кошку»* → ASCII art пастится в редактор
- *«task: explain stale closures in react in one sentence»* → пастится одно предложение

Ключевик ловится только если он среди **первых 4 слов** транскрипта (настраивается `taskKeywordMaxWordPosition`). Так длинная фраза, в которой слово «задача» случайно встречается в середине, не превратится в LLM-запрос.

---

## Локальное распознавание / On-device recognition

Трей → **Recognition** → **Parakeet v3 (on this PC)**. Если модели ещё нет, приложение спросит,
качать ли её (~460 МБ), и покажет окно с процентами; отказ оставляет облачный движок. Модель
ложится в `%APPDATA%\GroqVoice\models\` — **в репозиторий и в .exe она не входит** и переживает
обновления приложения. Удалить — там же в меню, «Delete downloaded model…».

- **NVIDIA Parakeet TDT v3** (sherpa-onnx / ONNX Runtime, CPU): 25 языков, русский и английский
  вперемешку внутри одной фразы, знаки препинания и заглавные расставляются сами.
- Офлайн и без API-ключа: если Groq-ключ не введён, а модель скачана, приложение больше не просит ключ
  при старте (он нужен только для task-режима).
- Замерено здесь: 4.4 с речи → 0.35 с распознавания, загрузка модели ~2.4 с однократно (4 потока).
  Модель держится в памяти (~740 МБ) ради мгновенного старта; `localUnloadAfterMinutes` это меняет.
- Словарь работает и здесь: у локального движка нет «подсказки», поэтому термины применяются
  к готовому тексту — строка `Coolify: кулифай, кулифи` чинит то, как распознаватель их пишет.
- Проверить без микрофона: `GroqVoice.exe --transcribe запись.wav` печатает распознанный текст.

---

## Конфигурация / Configuration

`%APPDATA%\GroqVoice\config.json` (создаётся автоматически на первом старте; шаблон есть в [config.example.json](config.example.json)):

| Поле | По умолчанию | Описание |
|---|---|---|
| `groqApiKey` | `""` | Ключ с [console.groq.com](https://console.groq.com). Хранится локально в `%APPDATA%`, в репозиторий не попадает. |
| `transcriptionModel` | `whisper-large-v3` | STT-модель Groq. |
| `chatModel` | `llama-3.3-70b-versatile` | LLM для task-режима. |
| `sttEngine` | `groq` | `groq` — Whisper в облаке; `parakeet` — локально на этом ПК. Те же значения, что в macOS-сборке. |
| `sttFallback` | `true` | Если выбранный движок не смог (нет модели, нет сети, ошибка API) — попробовать второй, а не терять диктовку. |
| `localUnloadAfterMinutes` | `0` | Через сколько минут простоя выгрузить локальную модель из памяти. `0` = держать загруженной (быстрее). |
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

`%APPDATA%\GroqVoice\vocabulary.txt` — биасит Whisper к нужным словам через `prompt` параметр API,
а для локального движка (у которого `prompt` нет) правит уже распознанный текст.

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

# Term: alias, alias — как распознаватель это пишет → как надо
Coolify: кулифай, кулифи
Dockup: докап, докапп
```

- Одна запись на строку. `#` — комментарии.
- **`Term: alias, alias`** — необязательные алиасы: их вхождения заменяются на каноническое
  написание. Русский склоняет заимствования («в телеграмме», «на гитхабе»), поэтому кириллический
  алиас от пяти букв матчится и с тремя хвостовыми буквами; короткие — только точно. Формат общий
  с macOS-сборкой.
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

