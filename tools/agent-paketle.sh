#!/usr/bin/env bash
# Builds a self-contained agent package for Windows Server.
#
#   ./tools/agent-paketle.sh [cikti-klasoru]
#
# Self-contained on purpose: the .NET runtime is bundled, so nothing has to be installed on
# the customer's production server. Asking a customer to install a runtime on their SQL box
# is a conversation; copying a folder is not.
set -euo pipefail

OUT="${1:-./agent-publish}"
RID="win-x64"

echo "→ Yayinlaniyor ($RID, self-contained)…"
dotnet publish src/MssqlRealtime.Agent/MssqlRealtime.Agent.csproj \
	-c Release \
	-r "$RID" \
	--self-contained true \
	-p:PublishSingleFile=false \
	-p:DebugType=none \
	-o "$OUT"

# The published appsettings.json carries placeholder values; make the two that matter obvious.
cat > "$OUT/appsettings.json" <<'JSON'
{
  "Agent": {
    "HubUrl": "https://izleme.marmaracloud.net",
    "EnrollmentKey": "BURAYA-KAYIT-ANAHTARINI-YAPISTIRIN"
  },

  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": { "Microsoft": "Warning", "System": "Warning" }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/agent-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7
        }
      }
    ]
  }
}
JSON

cp tools/agent-kur.ps1 "$OUT/" 2>/dev/null || true

SIZE=$(du -sh "$OUT" | cut -f1)
echo
echo "✅ Hazir: $OUT  ($SIZE)"
echo
echo "Sonraki adimlar:"
echo "  1. $OUT klasorunu Windows Server'a kopyalayin (or. C:\\MssqlRealtimeAgent)"
echo "  2. appsettings.json icinde EnrollmentKey'i doldurun"
echo "  3. Sunucuda yonetici PowerShell'de:  .\\agent-kur.ps1"
