# GroqVoice for macOS

Нативный macOS-порт [GroqVoice](https://github.com/abirzgals/GroqVoice) — menu bar утилита для голосовой диктовки и voice-driven LLM-задач через [Groq](https://groq.com).

**Удерживай Fn (🌐), говори, отпусти** — речь транскрибируется (Whisper) и вставляется в активное окно. Начни фразу с `task` / `задача` / `задание` — транскрипт уйдёт в Llama 3.3 70B, и вставится ответ модели.

Swift + AppKit, без зависимостей. Бинарник ~300 KB.

## Установка (готовый билд)

```bash
curl -fsSL https://raw.githubusercontent.com/abirzgals/GroqVoice/main/macos/install.sh | bash
```

Скачивает [GroqVoice-mac.zip](https://github.com/abirzgals/GroqVoice/releases/latest/download/GroqVoice-mac.zip) (universal: Apple Silicon + Intel), ставит в /Applications, снимает quarantine и запускает. Дальше только yes/yes на промпты разрешений.

## Сборка из исходников

```bash
./build-app.sh            # нативная архитектура → GroqVoice.app
./build-app.sh --zip      # universal (arm64+x86_64) + GroqVoice-mac.zip
mv GroqVoice.app /Applications/
open /Applications/GroqVoice.app
```

Требуется только Xcode Command Line Tools (`xcode-select --install`), macOS 13+.

## Первый запуск

1. **API-ключ** — приложение само попросит; ключ с [console.groq.com](https://console.groq.com) (бесплатный tier достаточен). Хранится в `~/Library/Application Support/GroqVoice/config.json`.
2. **Accessibility** — System Settings → Privacy & Security → Accessibility → включи GroqVoice. Нужно для глобального перехвата Fn и синтеза Cmd+V.
3. **Microphone** — разреши при первом запросе.
4. **Важно:** System Settings → Keyboard → **"Press 🌐 key to" → "Do Nothing"** — иначе двойной тап Fn открывает системную диктовку/эмодзи-пикер.

## Использование

| Действие | Что делает |
|---|---|
| **Hold Fn**, говори, отпусти | Push-to-talk: STT → paste в активное окно |
| **Double-tap Fn** | Lock-режим: запись держится; любой следующий тап Fn останавливает |
| Начни с `task: …` / `задача: …` | LLM-ответ вместо транскрипта |
| **Fn + другая клавиша** | OS shortcut работает как обычно; запись отбрасывается |
| Иконка в menu bar | 🎙 ready → 🔴 recording → 🟠 locked → ⏳ processing |

Граница «тап»/«hold» — 250 мс (`pttHoldMs`), окно двойного тапа — 400 мс (`doubleTapWindowMs`).

## Локальный режим (offline)

Tray → **Local Whisper**:

- **Off (Groq only)** — всё через облако
- **Fallback when offline** *(по умолчанию)* — обычно Groq, но если сети нет или все Groq-модели упали по лимитам, транскрипция идёт через локальный Whisper (WhisperKit / CoreML на Neural Engine). Включается только если модель уже скачана — жми **Download local model…** один раз (по умолчанию `large-v3-turbo` quantized, ~626 MB с Hugging Face; имя настраивается через `localWhisperModel`, список — [argmaxinc/whisperkit-coreml](https://huggingface.co/argmaxinc/whisperkit-coreml))
- **Always local** — без интернета вообще: STT локально, task-режим через встроенную модель Apple Intelligence (macOS 26+, Foundation Models, ноль скачиваний)

Модель загружается в память лениво при первом использовании и выгружается после простоя (`localUnloadAfterMinutes`, по умолчанию 10 мин) — память не занята, пока не диктуешь.

Headless-проверка/предзагрузка: `GroqVoice.app/Contents/MacOS/GroqVoice --download-model`

## Меню (right-click / click иконки)

Set API Key, Open Config / Vocabulary / Snippets / Log, Local Whisper, Launch at Login, Quit.

## Файлы

Всё в `~/Library/Application Support/GroqVoice/`:

- `config.json` — настройки (те же поля, что в Windows-версии: `taskKeywords`, `language`, `minRecordingSeconds`, `silencePeakPercent`, `taskSystemPrompt`, …)

  Вместо одиночных `transcriptionModel`/`chatModel` здесь **списки моделей по приоритету** с автоматическим fallback при rate limit (429): модель получает cooldown из `retry-after`, запрос уходит в следующую; когда cooldown истекает — снова пробуется более сильная.

  ```json
  "transcriptionModels": ["whisper-large-v3", "whisper-large-v3-turbo"],
  "chatModels": ["llama-3.3-70b-versatile", "openai/gpt-oss-120b", "llama-3.1-8b-instant"]
  ```
- `vocabulary.txt` — словарь редких слов/имён для биаса Whisper, hot-reload по mtime, лимит ~700 символов
- `snippets.txt` — голосовые шорткаты для task-режима (`команда = текст или инструкция`). Говоришь «задание напиши мою рабочую почту» — вставляется значение из таблички; матчинг фаззи (делает LLM), правая часть может быть и инструкцией («переведи = переведи на английский, выведи только перевод»). Hot-reload, открывается через Tray → Open Snippets
- `log.txt` — лог с ротацией на 1 MB
- `last.wav` — последняя запись для отладки (`"saveLastWav": false` чтобы отключить)

## Отличия от Windows-версии

- Хоткей: **Fn** вместо Win+Ctrl (настроено под macOS-привычку — как push-to-talk в системной диктовке)
- Микрофон: системный default (выбирается в System Settings → Sound → Input)
- Snipping-режим (Win+Ctrl+Alt) не портирован — на Mac есть встроенный Cmd+Shift+4
- Autostart через `SMAppService` (Login Items) вместо реестра

## Privacy

Как в оригинале: ключ только локально, аудио уходит только в `api.groq.com`, никакой телеметрии.

## License

MIT
