#!/bin/bash
# GroqVoice for Mac — one-line installer:
#   curl -fsSL https://raw.githubusercontent.com/abirzgals/GroqVoice/main/macos/install.sh | bash
#
# Downloads the latest release, installs to /Applications, clears the
# quarantine flag (the app is not notarized) and launches it. On first launch
# macOS will ask for Microphone and Accessibility — approve both and you're done.
set -euo pipefail

REPO="abirzgals/GroqVoice"
ASSET="GroqVoice-mac.zip"
URL="https://github.com/$REPO/releases/latest/download/$ASSET"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

echo "Downloading GroqVoice…"
curl -fsSL -o "$TMP/$ASSET" "$URL"
ditto -x -k "$TMP/$ASSET" "$TMP"

echo "Installing to /Applications…"
osascript -e 'tell application "GroqVoice" to quit' >/dev/null 2>&1 || true
rm -rf /Applications/GroqVoice.app
ditto "$TMP/GroqVoice.app" /Applications/GroqVoice.app
xattr -cr /Applications/GroqVoice.app

echo "Launching…"
open /Applications/GroqVoice.app

cat <<'EOF'

Done! Two approvals are needed on first launch:
  1. Microphone — click OK on the system prompt.
  2. Accessibility — click "Open System Settings" on the prompt and
     enable GroqVoice in the list (needed for the Fn hotkey and paste).

Then paste your Groq API key (console.groq.com) into the dialog,
hold Fn (🌐), speak, release — the text appears in the focused window.

Tip: set System Settings → Keyboard → "Press 🌐 key to" → "Do Nothing"
so double-tap Fn doesn't trigger macOS dictation.
EOF
