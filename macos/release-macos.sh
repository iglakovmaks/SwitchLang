#!/bin/sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
cd "$ROOT"

# Use notarytool store-credentials to keep credentials in the keychain.
if [ -z "${SWITCHLANG_SIGN_IDENTITY:-}" ] || [ "${SWITCHLANG_SIGN_IDENTITY:-}" = "-" ] || \
   [ -z "${SWITCHLANG_NOTARY_PROFILE:-}" ]; then
    echo "A public notarized release needs SWITCHLANG_SIGN_IDENTITY (Developer ID Application)" >&2
    echo "and SWITCHLANG_NOTARY_PROFILE (an existing notarytool keychain profile)." >&2
    echo "Use create-dmg.sh for a local test build that requires manual opening." >&2
    exit 1
fi

./create-dmg.sh
APP="$ROOT/dist/SwitchLang.app"
DMG="$ROOT/dist/SwitchLang.dmg"
if ! codesign -dvv "$APP" 2>&1 | /usr/bin/grep -q 'Authority=Developer ID Application:'; then
    echo "The supplied signing identity is not Developer ID Application." >&2
    exit 1
fi
codesign --sign "$SWITCHLANG_SIGN_IDENTITY" --timestamp "$DMG"
xcrun notarytool submit "$DMG" --keychain-profile "$SWITCHLANG_NOTARY_PROFILE" --wait
xcrun stapler staple "$DMG"
xcrun stapler validate "$DMG"
./verify-dmg.sh "$DMG"
spctl --assess --type open --context context:primary-signature --verbose=2 "$DMG"
echo "Notarized release ready: $DMG"
