#!/bin/bash
# Builds GroqVoice.app (release, ad-hoc signed) into the repo root.
set -euo pipefail
cd "$(dirname "$0")"

swift build -c release

APP=GroqVoice.app
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS"
cp .build/release/GroqVoice "$APP/Contents/MacOS/GroqVoice"
cp Info.plist "$APP/Contents/Info.plist"

codesign --force --sign - "$APP"

echo
echo "Built: $(pwd)/$APP"
echo "Move it to /Applications and launch it."
