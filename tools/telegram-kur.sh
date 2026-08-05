#!/usr/bin/env bash
# Configures and tests the Telegram channel without opening the UI.
#
#   ./tools/telegram-kur.sh <BOT_TOKEN> <CHAT_ID> [SUNUCU_URL] [KULLANICI] [PAROLA]
#
# Example:
#   ./tools/telegram-kur.sh 8123456789:AAH... 123456789
set -euo pipefail

TOKEN_TG="${1:?Kullanim: telegram-kur.sh <BOT_TOKEN> <CHAT_ID> [SUNUCU_URL] [KULLANICI] [PAROLA]}"
CHAT_ID="${2:?chat id gerekli — once: node tools/telegram-chatid.mjs <BOT_TOKEN>}"
BASE="${3:-http://localhost:5199}"
USER_EMAIL="${4:-admin@local}"
USER_PASS="${5:-Test1234567!}"

echo "→ Giris yapiliyor: $BASE"
ACCESS=$(curl -sS -X POST "$BASE/api/auth/login" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"$USER_EMAIL\",\"password\":\"$USER_PASS\"}" \
  | python3 -c 'import sys,json; print(json.load(sys.stdin)["accessToken"])')

echo "→ Telegram kanali yapilandiriliyor"
curl -sS -X PUT "$BASE/api/notifications/channels/telegram" \
  -H "Authorization: Bearer $ACCESS" \
  -H 'Content-Type: application/json' \
  -d "{\"enabled\":true,\"minimumSeverity\":1,\"sendRecoveries\":true,\"values\":{\"botToken\":\"$TOKEN_TG\",\"chatId\":\"$CHAT_ID\"}}" \
  -o /dev/null -w '   ayar: HTTP %{http_code}\n'

echo "→ Test mesaji gonderiliyor"
RESULT=$(curl -sS -X POST "$BASE/api/notifications/channels/telegram/test" -H "Authorization: Bearer $ACCESS")

if echo "$RESULT" | grep -q '"ok":true'; then
	echo
	echo "✅ Gonderildi. Telegram'i kontrol edin."
	echo "   Mesaj gelmediyse chat id yanlis olabilir: node tools/telegram-chatid.mjs $TOKEN_TG"
else
	echo
	echo "❌ Basarisiz:"
	echo "$RESULT" | python3 -m json.tool 2>/dev/null || echo "$RESULT"
fi
