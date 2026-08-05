#!/usr/bin/env bash
# Diagnoses a 502 between Nginx Proxy Manager and the monitoring container.
# Run on the Docker host. Each step narrows down where the request stops.
#
#   bash tools/502-teshis.sh [NPM_KONTEYNER_ADI] [HOST_IP]

NPM_NAME="${1:-nginx-app-1}"
HOST_IP="${2:-192.168.2.240}"

line() { printf '\n\033[1m%s\033[0m\n' "$1"; }

line "1) Konteyner calisiyor mu?"
docker ps --filter name=mssqlrealtime --format '   {{.Names}}  {{.Status}}  portlar: {{.Ports}}' \
	|| echo '   ✗ docker ps basarisiz'
docker ps --filter name=mssqlrealtime -q | grep -q . \
	|| echo '   ✗ KONTEYNER YOK — stack deploy edilmemis ya da build hatasi almis'

line "2) Port host'ta hangi adreste dinleniyor?"
docker port mssqlrealtime 2>/dev/null || echo '   ✗ port yayinlanmamis'
echo '   → 127.0.0.1:5199 goruyorsan sorun bu: NPM konteynerden oraya ULASAMAZ.'
echo '     Cozum: BIND_ADDRESS='"$HOST_IP"' verip stack-i yeniden deploy edin.'

line "3) Host uzerinden erisim (uygulama ayakta mi?)"
curl -sS -m 5 "http://127.0.0.1:5199/api/health" && echo || echo '   ✗ host localhost uzerinden de cevap yok'

line "4) Host LAN IP uzerinden erisim"
curl -sS -m 5 "http://$HOST_IP:5199/api/health" && echo || echo "   ✗ $HOST_IP:5199 kapali — BIND_ADDRESS muhtemelen 127.0.0.1"

line "5) NPM KONTEYNERININ ICINDEN erisim  ← asil belirleyici test"
if docker exec "$NPM_NAME" sh -c "command -v curl >/dev/null 2>&1"; then
	docker exec "$NPM_NAME" curl -sS -m 5 "http://$HOST_IP:5199/api/health" && echo \
		|| echo "   ✗ NPM konteyneri $HOST_IP:5199 adresine ULASAMIYOR → 502'nin sebebi bu"
else
	docker exec "$NPM_NAME" sh -c "wget -qO- --timeout=5 http://$HOST_IP:5199/api/health" && echo \
		|| echo "   ✗ NPM konteyneri $HOST_IP:5199 adresine ULASAMIYOR → 502'nin sebebi bu"
fi

line "6) Konteyner loglari (son 15 satir)"
docker logs --tail 15 mssqlrealtime 2>&1 | sed 's/^/   /' || echo '   (log yok)'

line "Ozet"
echo "   1-2 basarisiz  → stack deploy edilmemis veya build hatasi: Portainer → Stacks → Logs"
echo "   3 basarili, 4 basarisiz → BIND_ADDRESS=127.0.0.1 kalmis; $HOST_IP yapin"
echo "   4 basarili, 5 basarisiz → ag izolasyonu; en saglami ayni Docker agina almak:"
echo "                             docker network connect \$(docker inspect $NPM_NAME \\"
echo "                               --format '{{range \$k,\$v := .NetworkSettings.Networks}}{{\$k}}{{end}}') mssqlrealtime"
echo "                             ve NPM'de Forward Hostname: mssqlrealtime, Port: 8080"
