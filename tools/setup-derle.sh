#!/usr/bin/env bash
# Compiles the Windows installer (setup.exe) — on macOS/Linux too, no Windows machine.
#
#   ./tools/setup-derle.sh
#
# Inno Setup is a Windows program, but it runs fine under Wine, and amake/innosetup ships
# both in one image. Measured 2026-08-06: 0.12.0 compiled in 111 s on macOS/arm64.
#
# Run ./tools/windows-paketle.sh first — this script only packages what is already in
# windows-publish/.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if ! docker info >/dev/null 2>&1; then
	echo "✗ Docker calismiyor. Docker Desktop'i baslatin (ya da setup'i bir Windows" >&2
	echo "  makinede ISCC.exe ile derleyin)." >&2
	exit 1
fi

if [ ! -d "$ROOT/windows-publish" ]; then
	echo "✗ windows-publish/ yok. Once: ./tools/windows-paketle.sh" >&2
	exit 1
fi

# The .iss version must match the build, otherwise the installer ships new binaries under
# an old version number in Add/Remove Programs. Measured 2026-08-06: these had drifted.
PROPS_VERSION=$(grep -o '<VersionPrefix>[^<]*' "$ROOT/Directory.Build.props" | cut -d'>' -f2)
ISS_VERSION=$(grep -o '#define AppVersion "[^"]*' "$ROOT/setup/SunucuIzleme.iss" | cut -d'"' -f2)

if [ "$PROPS_VERSION" != "$ISS_VERSION" ]; then
	echo "✗ Surum uyusmuyor: Directory.Build.props=$PROPS_VERSION, SunucuIzleme.iss=$ISS_VERSION" >&2
	echo "  Ikisini esitleyin, sonra tekrar calistirin." >&2
	exit 1
fi

# Braces are required: the ellipsis that follows is multibyte and bash otherwise swallows
# it into the variable name ("PROPS_VERSION…: unbound variable").
echo "→ Inno Setup (Wine) ile derleniyor — surum ${PROPS_VERSION}…"
docker run --rm -v "$ROOT:/work" -w /work amake/innosetup setup/SunucuIzleme.iss

OUT="$ROOT/setup/output/SunucuIzleme-Setup-$PROPS_VERSION.exe"
SIZE=$(du -h "$OUT" | cut -f1)
echo
echo "✅ Hazir: setup/output/SunucuIzleme-Setup-$PROPS_VERSION.exe  ($SIZE)"
