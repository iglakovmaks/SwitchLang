#!/bin/sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
cd "$ROOT"
swift build -c release

APP_NAME="SwitchLang.app"
APP_DIR="$ROOT/dist/$APP_NAME"
rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
cp ".build/release/SwitchLang" "$APP_DIR/Contents/MacOS/SwitchLang"
cp "$ROOT/../icon.png" "$APP_DIR/Contents/Resources/icon.png"

ICONSET_DIR="$APP_DIR/Contents/Resources/SwitchLang.iconset"
mkdir -p "$ICONSET_DIR"
for SIZE in 16 32 128 256 512; do
    sips -z "$SIZE" "$SIZE" "$ROOT/../icon.png" --out "$ICONSET_DIR/icon_${SIZE}x${SIZE}.png" >/dev/null
    DOUBLE_SIZE=$((SIZE * 2))
    sips -z "$DOUBLE_SIZE" "$DOUBLE_SIZE" "$ROOT/../icon.png" --out "$ICONSET_DIR/icon_${SIZE}x${SIZE}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET_DIR" -o "$APP_DIR/Contents/Resources/SwitchLang.icns"
rm -rf "$ICONSET_DIR"

cat > "$APP_DIR/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDisplayName</key><string>SwitchLang</string>
    <key>CFBundleExecutable</key><string>SwitchLang</string>
    <key>CFBundleIdentifier</key><string>com.switchlang.app</string>
    <key>CFBundleName</key><string>SwitchLang</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>0.2.0</string>
    <key>CFBundleVersion</key><string>2</string>
    <key>CFBundleIconFile</key><string>SwitchLang.icns</string>
    <key>LSUIElement</key><true/>
    <key>LSMultipleInstancesProhibited</key><true/>
    <key>NSAppleEventsUsageDescription</key><string>SwitchLang использует системное копирование и вставку для исправления выделенного текста.</string>
</dict>
</plist>
PLIST

echo "Built $APP_DIR"
