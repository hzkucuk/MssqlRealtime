#!/usr/bin/env bash
# Builds a self-contained Windows package of the monitoring panel.
#
#   ./tools/windows-paketle.sh [cikti-klasoru]
#
# For customers with no Docker: the panel runs as a Windows service on the same box as
# SQL Server, or on any Windows machine on their LAN.
#
# Self-contained on purpose — the .NET runtime is bundled, so nothing has to be installed
# on the customer's server.
set -euo pipefail

OUT="${1:-./windows-publish}"

echo "→ Ön yüz derleniyor…"
(cd app && npm ci --silent && npm run build --silent)

echo "→ Sunucu yayinlaniyor (win-x64, self-contained)…"
dotnet publish src/MssqlRealtime.Api/MssqlRealtime.Api.csproj \
	-c Release \
	-r win-x64 \
	--self-contained true \
	-p:DebugType=none \
	-o "$OUT"

echo "→ Ön yüz kopyalaniyor…"
mkdir -p "$OUT/wwwroot"
cp -r app/build/* "$OUT/wwwroot/"

cp tools/windows-kur.ps1 "$OUT/"

SIZE=$(du -sh "$OUT" | cut -f1)
echo
echo "✅ Hazir: $OUT  ($SIZE)"
echo
echo "Sonraki adimlar:"
echo "  1. $OUT klasorunu Windows sunucuya kopyalayin (or. C:\\SunucuIzleme)"
echo "  2. Yonetici PowerShell'de:  .\\windows-kur.ps1 -AdminPassword 'guclu-parola'"
