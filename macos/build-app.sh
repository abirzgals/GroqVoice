#!/bin/bash
# Builds GroqVoice.app (release, ad-hoc signed) into the repo root.
#   ./build-app.sh              native arch (fast, for local use)
#   ./build-app.sh --universal  arm64 + x86_64 (for distribution)
#   ./build-app.sh --dist       universal + GroqVoice-mac.zip + GroqVoice-mac.dmg
set -euo pipefail
cd "$(dirname "$0")"

UNIVERSAL=0
MAKE_DIST=0
for arg in "$@"; do
  case "$arg" in
    --universal) UNIVERSAL=1 ;;
    --dist|--zip) UNIVERSAL=1; MAKE_DIST=1 ;;
  esac
done

if [ "$UNIVERSAL" = 1 ]; then
  # Per-arch builds + lipo: works with Command Line Tools alone
  # (swift build --arch … needs full Xcode).
  swift build -c release --triple arm64-apple-macosx13.0
  swift build -c release --triple x86_64-apple-macosx13.0
  BIN=.build/GroqVoice-universal
  lipo -create -output "$BIN" \
    .build/arm64-apple-macosx/release/GroqVoice \
    .build/x86_64-apple-macosx/release/GroqVoice
else
  swift build -c release
  BIN=.build/release/GroqVoice
fi

APP=GroqVoice.app
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS"
cp "$BIN" "$APP/Contents/MacOS/GroqVoice"
cp Info.plist "$APP/Contents/Info.plist"

codesign --force --sign - "$APP"

echo
echo "Built: $(pwd)/$APP ($(lipo -archs "$APP/Contents/MacOS/GroqVoice" 2>/dev/null || echo native))"

if [ "$MAKE_DIST" = 1 ]; then
  rm -f GroqVoice-mac.zip GroqVoice-mac.dmg
  ditto -c -k --keepParent "$APP" GroqVoice-mac.zip
  echo "Release artifact: $(pwd)/GroqVoice-mac.zip ($(du -h GroqVoice-mac.zip | cut -f1))"

  # Drag-to-install DMG: the app + an /Applications symlink.
  DMGROOT=$(mktemp -d)
  ditto "$APP" "$DMGROOT/GroqVoice.app"
  ln -s /Applications "$DMGROOT/Applications"
  hdiutil create -volname "GroqVoice" -srcfolder "$DMGROOT" -ov -quiet -format UDZO GroqVoice-mac.dmg
  rm -rf "$DMGROOT"
  echo "Release artifact: $(pwd)/GroqVoice-mac.dmg ($(du -h GroqVoice-mac.dmg | cut -f1))"
fi
