#!/bin/sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
cd "$ROOT"

swift build -c release --triple arm64-apple-macosx13.0
ARM_BIN="$(swift build -c release --triple arm64-apple-macosx13.0 --show-bin-path)"
swift build -c release --triple x86_64-apple-macosx13.0
INTEL_BIN="$(swift build -c release --triple x86_64-apple-macosx13.0 --show-bin-path)"
mkdir -p "$ROOT/dist"
WORK_DIR="$(mktemp -d "$ROOT/dist/.app-build.XXXXXX")"
APP_DIR="$WORK_DIR/SwitchLang.app"
APP_PATH="$ROOT/dist/SwitchLang.app"
SIGN_IDENTITY="${SWITCHLANG_SIGN_IDENTITY:--}"

cleanup() {
    # This is a freshly-created, task-specific staging directory.
    if [ -d "$WORK_DIR" ]; then /bin/rm -r "$WORK_DIR"; fi
}
trap cleanup EXIT INT TERM

mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
lipo -create "$ARM_BIN/SwitchLang" "$INTEL_BIN/SwitchLang" -output "$APP_DIR/Contents/MacOS/SwitchLang"
cp "$ROOT/../icon.png" "$APP_DIR/Contents/Resources/icon.png"
cp "$ROOT/Info.plist" "$APP_DIR/Contents/Info.plist"

ICONSET_DIR="$WORK_DIR/SwitchLang.iconset"
mkdir -p "$ICONSET_DIR"
for SIZE in 16 32 128 256 512; do
    sips -z "$SIZE" "$SIZE" "$ROOT/../icon.png" --out "$ICONSET_DIR/icon_${SIZE}x${SIZE}.png" >/dev/null
    DOUBLE_SIZE=$((SIZE * 2))
    sips -z "$DOUBLE_SIZE" "$DOUBLE_SIZE" "$ROOT/../icon.png" --out "$ICONSET_DIR/icon_${SIZE}x${SIZE}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET_DIR" -o "$APP_DIR/Contents/Resources/SwitchLang.icns"

plutil -lint "$APP_DIR/Contents/Info.plist"
lipo "$APP_DIR/Contents/MacOS/SwitchLang" -verify_arch arm64 x86_64

# Sign only after Info.plist and every resource are in their final locations.
# Swift's linker signature alone is not a valid signature for the .app bundle.
if [ "$SIGN_IDENTITY" = "-" ]; then
    codesign --force --sign - --identifier com.switchlang.app --options runtime \
        --entitlements "$ROOT/entitlements.plist" "$APP_DIR"
    echo "Local test signature: Developer ID and Apple notarization are not present."
else
    codesign --force --sign "$SIGN_IDENTITY" --identifier com.switchlang.app \
        --options runtime --timestamp --entitlements "$ROOT/entitlements.plist" "$APP_DIR"
fi
codesign --verify --deep --strict --verbose=2 "$APP_DIR"

# Preserve the previous build until the new, complete bundle has been verified.
if [ -e "$APP_PATH" ]; then
    BACKUP_DIR="$(mktemp -d "$ROOT/dist/previous-app.XXXXXX")"
    mv "$APP_PATH" "$BACKUP_DIR/SwitchLang.app"
    echo "Previous build saved to $BACKUP_DIR/SwitchLang.app"
fi
mv "$APP_DIR" "$APP_PATH"
echo "Built $APP_PATH"
