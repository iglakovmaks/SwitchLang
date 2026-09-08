#!/bin/sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
cd "$ROOT"

./build-app.sh

APP_PATH="$ROOT/dist/SwitchLang.app"
DMG_PATH="$ROOT/dist/SwitchLang.dmg"
WORK_DIR="$(mktemp -d /tmp/switchlang-dmg.XXXXXX)"
STAGING="$WORK_DIR/SwitchLang"
RW_DMG="$WORK_DIR/SwitchLang-rw.dmg"
BACKGROUND="$WORK_DIR/background.png"

cleanup() {
    if [ -n "${MOUNT_POINT:-}" ] && mount | grep -Fq "on $MOUNT_POINT "; then
        hdiutil detach "$MOUNT_POINT" -quiet || true
    fi
    rm -rf "$WORK_DIR"
}
trap cleanup EXIT INT TERM

mkdir -p "$STAGING"
cp -R "$APP_PATH" "$STAGING/SwitchLang.app"
ln -s /Applications "$STAGING/Applications"
swift "$ROOT/dmg-background.swift" "$BACKGROUND"

hdiutil create \
    -volname "SwitchLang" \
    -srcfolder "$STAGING" \
    -fs HFS+ \
    -format UDRW \
    -ov "$RW_DMG" >/dev/null

MOUNT_POINT="$(hdiutil attach "$RW_DMG" -nobrowse | awk '/\/Volumes\// { print substr($0, index($0, "/Volumes/")); exit }')"
if [ -z "$MOUNT_POINT" ]; then
    echo "Не удалось подключить временный образ" >&2
    exit 1
fi
VOLUME_NAME="$(basename "$MOUNT_POINT")"

if [ -f "$APP_PATH/Contents/Resources/SwitchLang.icns" ]; then
    cp "$APP_PATH/Contents/Resources/SwitchLang.icns" "$MOUNT_POINT/.VolumeIcon.icns"
    SetFile -a C "$MOUNT_POINT" || true
    SetFile -a C "$MOUNT_POINT/.VolumeIcon.icns" || true
fi

mkdir -p "$MOUNT_POINT/.background"
cp "$BACKGROUND" "$MOUNT_POINT/.background/background.png"
SetFile -a V "$MOUNT_POINT/.background" || true

osascript <<APPLESCRIPT
tell application "Finder"
    tell disk "$VOLUME_NAME"
        open
        delay 1
        set containerWindow to container window
        set current view of containerWindow to icon view
        set toolbar visible of containerWindow to false
        set statusbar visible of containerWindow to false
        -- Finder does not expose a writable resizable/zoomable property for
        -- folder windows. Keep the initial frame fixed and disable the green
        -- zoom action where Finder allows it.
        set zoomed of containerWindow to false
        set bounds of containerWindow to {120, 120, 820, 580}
        set iconOptions to icon view options of containerWindow
        set background picture of iconOptions to POSIX file "$MOUNT_POINT/.background/background.png" as alias
        set icon size of iconOptions to 128
        set text size of iconOptions to 14
        set arrangement of iconOptions to not arranged
        set position of item "SwitchLang.app" to {190, 230}
        set position of item "Applications" to {510, 230}
        update without registering applications
        delay 1
        close containerWindow
    end tell
end tell
APPLESCRIPT

sync
hdiutil detach "$MOUNT_POINT" -quiet
MOUNT_POINT=""

hdiutil convert "$RW_DMG" \
    -format UDZO \
    -imagekey zlib-level=9 \
    -ov \
    -o "$DMG_PATH" >/dev/null

echo "Built $DMG_PATH"
