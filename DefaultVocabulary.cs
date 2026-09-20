namespace GroqVoice;

/// <summary>
/// Starter vocabulary.txt. Left of the colon: how the word must be written.
/// Right: what the recognizer actually produces for it in Russian speech —
/// English terms come back spelled in Cyrillic ("по ракет" for Parakeet), and
/// these lines put them back. Shared format with the macOS build.
/// </summary>
public static class DefaultVocabulary
{
    public const string Text = """
        # GroqVoice vocabulary — one entry per line, case matters on the left side.
        #
        #   Term: alias, alias, …
        #
        # Left of the colon: the exact spelling you want pasted.
        # Right of the colon: what the recognizer actually writes for it (see "STT result"
        # in the log), separated by commas. Whole words only, case-insensitive.
        # Lines starting with # are ignored, and the file hot-reloads — no restart.
        #
        # A term without aliases just fixes its own casing ("github" → "GitHub"). A
        # lower-case term is an ordinary word and keeps its capital at the start of a
        # sentence. Cyrillic aliases of five letters or more also match inflected forms
        # ("в телеграмме"), so keep short look-alikes of real words off the right side.
        #
        # With the Groq engine these terms are also sent as the recognition prompt.

        # This app
        Parakeet: паракет, по ракет, поракет, паракит, пара кит
        Groq: грок, грок ай
        GroqVoice: грок войс, гроквойс
        Whisper: виспер, вispер
        ONNX: оникс, о эн эн экс

        # Dev slang the recognizer mishears
        прод: прот
        закоммитить: закамитить, закомитить
        деплой: деплои
        коммит: комит, камит

        # Hosting, deploy, infrastructure
        Docker: докер, декер
        nginx: энджинкс, энжинкс, нгинкс
        Cloudflare: клаудфлер, клауд флер, клаудфлэр
        Tailscale: тейлскейл, тэйлскейл
        Hetzner: хетцнер, хецнер
        Supabase: супабейс, супабейз
        SSH: эсэсэйч
        DNS: днс
        URL: урл
        API: апи
        JSON: джейсон
        README: ридми

        # Git and builds
        GitHub: гитхаб, гит хаб, гитхап
        GitLab: гитлаб, гит лаб
        git: гит
        pull request: пул реквест, пулреквест, пул-реквест
        npm: энпиэм, нпм
        Node.js: нод джиэс, nodejs

        # Stack
        Python: пайтон, питон
        React: реакт
        TypeScript: тайпскрипт, тайп скрипт
        JavaScript: джаваскрипт, джава скрипт
        PHP: пхп, пиэйчпи
        Postgres: постгрес, постгре
        Redis: редис
        SQLite: эскюлайт, сиквел лайт
        Godot: годо, годот

        # Platforms and services
        Telegram: телеграм, телеграмм, телега
        Figma: фигма
        Notion: ноушен, ноушн
        Google: гугл
        YouTube: ютуб, ютьюб
        Claude: клод
        Cursor: курсор
        ChatGPT: чат джипити, чатгпт, чат гпт, чат джи пи ти
        OpenAI: опенэйай, опен эйай

        # Devices and OS
        Windows: виндовс, винда
        macOS: макос, мак ос
        iPhone: айфон
        Android: андроид
        Wi-Fi: вайфай, вай фай, вай-фай
        Bluetooth: блютус, блютуз
        """;
}
