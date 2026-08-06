#!/usr/bin/env bash
# Builds and signs the Android APK.
#
#   ./tools/apk-derle.sh
#
# Signing prompts for the keystore password — it is never stored in the repo. Without a
# signature Android refuses to install the APK, so the unsigned Gradle output is not a
# deliverable on its own.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Measured 2026-08-06 11:35: with the default JDK 25 on this machine, Gradle fails at
# ":buildSrc" with a bare "> 25.0.3" — the Android Gradle Plugin does not know that
# version. JDK 21 (already installed via Homebrew) builds fine.
export JAVA_HOME="${JAVA_HOME_OVERRIDE:-/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home}"
export ANDROID_HOME="${ANDROID_HOME:-$HOME/Library/Android/sdk}"
export NDK_HOME="${NDK_HOME:-$ANDROID_HOME/ndk/27.0.12077973}"

KEYSTORE="${KEYSTORE:-$HOME/.android/sunucuizleme.keystore}"
BUILD_TOOLS="$ANDROID_HOME/build-tools/35.0.0"
VERSION=$(grep -o '"version": *"[^"]*"' "$ROOT/app/src-tauri/tauri.conf.json" | head -1 | cut -d'"' -f4)

for path in "$JAVA_HOME" "$ANDROID_HOME" "$NDK_HOME" "$BUILD_TOOLS"; do
	if [ ! -d "$path" ]; then
		echo "✗ Bulunamadi: $path" >&2
		exit 1
	fi
done

# arm64 only: universal derleme 36 MB, arm64 12 MB. Fark x86 (emulator) ve armv7
# (2015 oncesi telefonlar) — yayinlanan tum APK'lar arm64.
echo "→ APK derleniyor — surum ${VERSION} (JDK 21, arm64)…"
(cd "$ROOT/app" && npm run tauri android build -- --apk --target aarch64)

# Olculdu 2026-08-06 14:23: --target aarch64 verilse bile cikti apk/universal/ altina
# "app-universal-release-unsigned.apk" adiyla dusuyor; yalniz icerigi arm64'e daraliyor.
UNSIGNED=$(find "$ROOT/app/src-tauri/gen/android/app/build/outputs/apk" -name "*-release-unsigned.apk" | head -1)
OUT_DIR="$ROOT/setup/output"
ALIGNED="$OUT_DIR/SunucuIzleme-$VERSION.apk"

mkdir -p "$OUT_DIR"
"$BUILD_TOOLS/zipalign" -f -p 4 "$UNSIGNED" "$ALIGNED"

if [ ! -f "$KEYSTORE" ]; then
	echo "✗ Keystore yok: $KEYSTORE — APK imzasiz kaldi, Android kurmaz." >&2
	exit 1
fi

echo "→ Imzalaniyor (keystore parolasi sorulacak)…"
"$BUILD_TOOLS/apksigner" sign --ks "$KEYSTORE" "$ALIGNED"
"$BUILD_TOOLS/apksigner" verify --print-certs "$ALIGNED" | head -3

SIZE=$(du -h "$ALIGNED" | cut -f1)
echo
echo "✅ Hazir: setup/output/SunucuIzleme-$VERSION.apk  ($SIZE)"
