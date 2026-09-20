# GroqVoice — заметки по macOS-приложению

Корень репозитория — Windows-приложение на C# (трей, Groq API). **Вся macOS-часть — в `macos/`**
(Swift Package, AppKit, без Xcode-проекта); она пришла из ветки crcknaka
([crcknaka/GroqVoice](https://github.com/crcknaka/GroqVoice), remote `crcknaka`) и влита сюда.
Заметки ниже описывают именно macOS-приложение.

- Движок по умолчанию — Parakeet TDT v3 через FluidAudio (локально, Neural Engine).
  Groq — облачная опция/фолбэк и LLM для task-режима и чистки транскрипта.
- Сборка: `cd macos && ./build-app.sh` → `GroqVoice.app`; установка — `ditto` в `/Applications`.
  Только Command Line Tools, без Xcode (`xcodebuild` недоступен). Тесты: `./test.sh` (Swift Testing;
  XCTest в CLT нет). Иконка: `Resources/make-icns.sh`.
- Подпись ad-hoc, но с designated requirement `identifier "com.abirzgals.groqvoice"`
  (см. `build-app.sh`), чтобы TCC-разрешения переживали пересборки. Если Accessibility всё же
  слетело: `tccutil reset Accessibility com.abirzgals.groqvoice` и перезапуск.
- Обратная связь только иконкой в menu bar — никаких оверлеев (так решил пользователь). Настройки — окно
  Settings (`SettingsWindow.swift`, AppKit программно, NSGridView), история — `HistoryWindow.swift`.
- LLM для перевода/task/чистки: любой OpenAI-совместимый endpoint (`chatBaseURL`), по умолчанию Groq.
- Репозитории: origin = github.com/abirzgals/GroqVoice, crcknaka = github.com/crcknaka/GroqVoice
  (источник macOS-части). Коммиты — английские императивные, без упоминания AI.
- Словарь: `Term: alias1, alias2` в vocabulary.txt → детерминированная замена алиасов в тексте.
  Акустический CTC-бустинг FluidAudio удалён (на русской речи давал ложные замены).
- Стартовый словарь — `DefaultVocabulary.swift`; у пользователя файл уже заполнен им.
- Сниппеты (`Snippets.swift`): `фраза = текст` раскрывается мгновенно без LLM при точном совпадении
  всей фразы, `фраза => инструкция` — только для LLM в task-режиме. Оба списка редактируются в окне
  Dictionary (`DictionaryWindow.swift`), правки сохраняют комментарии и порядок в файлах.
- Данные приложения: `~/Library/Application Support/GroqVoice/` (config.json, history.jsonl,
  vocabulary.txt, snippets.txt, log.txt, models/).
- Headless-режимы для отладки: `--transcribe file.wav [--vocab terms.txt]`, `--download-model`,
  `--snapshot-ui dir` (PNG окон Settings/History без Screen Recording — свои окна можно снимать),
  `--probe-ax <bundle-id> [--manual]` (что приложение отдаёт Accessibility; запускать через
  `open -n -W -o out.txt GroqVoice.app --args …`, иначе нет TCC-гранта).
- Логика без UI вынесена и покрыта тестами: `PushToTalk.swift` (правила клавиши), `TakePlan.swift`
  (маршрутизация дубля), `Config.decode(from:)`. Новая настройка = одно свойство в `Config`.
- Accessibility: VS Code и форки не отдают поле в фокусе, и просить их нельзя
  (`AXManualAccessibility` включает у них screen-reader-режим) — они в списке `monacoApps`
  как «точно редактируемые». Остальным Electron-приложениям атрибут выставляется один раз на процесс.
- В логе миллисекунды и разбивка задержки дубля по этапам (`take: … (stop · probe · stt · llm · context · paste)`).
- Подробности и меню — `macos/README.md`.
