#!/bin/sh
set -eu

if [ "$#" -ne 1 ]; then
    echo "Usage: $0 PATH_TO_DMG" >&2
    exit 2
fi
DMG_PATH="$1"
WORK_DIR="$(mktemp -d /tmp/switchlang-verify-dmg.XXXXXX)"
MOUNT_POINT="$WORK_DIR/volume"
mkdir -p "$MOUNT_POINT"
ATTACHED=false
cleanup() {
    if [ "$ATTACHED" = true ]; then hdiutil detach "$MOUNT_POINT" -quiet || true; fi
    /bin/rm -r "$WORK_DIR"
}
trap cleanup EXIT INT TERM

hdiutil verify "$DMG_PATH"
hdiutil attach "$DMG_PATH" -readonly -nobrowse -mountpoint "$MOUNT_POINT" -quiet
ATTACHED=true
APP="$MOUNT_POINT/SwitchLang.app"
test -L "$MOUNT_POINT/Applications"
test "$(readlink "$MOUNT_POINT/Applications")" = /Applications
plutil -lint "$APP/Contents/Info.plist"
lipo "$APP/Contents/MacOS/SwitchLang" -verify_arch arm64 x86_64
codesign --verify --deep --strict --verbose=2 "$APP"
echo "DMG checksum, bundle signature, resources and both architectures: PASS"
