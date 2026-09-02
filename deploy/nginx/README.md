# nginx + Let's Encrypt

> ⚠️ **Bu belge kaldırılmış bir dağıtım şeklini anlatıyor.** Docker/Linux yolu v0.8.0'da
> kaldırıldı: ürün artık **Windows servisi** olarak kuruluyor ve önündeki vekil **Nginx Proxy
> Manager**. Aşağıdaki `docker compose`, konteyner ağı ve `ufw` adımlarının bugünkü kurulumda
> karşılığı yok. Güncel yol: `docs/03-kurulum.md` ve `CLAUDE.md` → *Nginx Proxy Manager*.
> Burada yalnız nginx davranışına dair iki şey hâlâ doğru: WebSocket yükseltme başlıkları
> olmadan SignalR **sessizce** long-polling'e düşer, ve `/hubs/` için ayrı bir `location`
> gerekiyorsa sebebi zaman aşımıdır.
>
> 🔴 Aşağıdaki doğrulama bloğundaki WebSocket testi **yanlış**: `/hubs/tools` yetkilendirme
> ister, token'sız `curl` en iyi ihtimalle `401` döner — `101` beklenmemeli. WebSocket'in
> çalıştığını tarayıcıdaki bağlantı göstergesi doğrular.

Alan adı: **izleme.example.com** (başka bir subdomain kullanacaksan dosyadaki iki
`server_name` satırını ve `Cors__AllowedOrigins__0` değerini değiştir).

## nginx nerede koşuyor? Tek fark bu

| | A: nginx aynı host'ta (apt) | B: nginx konteynerde (Portainer) | **C: nginx ayrı sunucuda** |
| --- | --- | --- | --- |
| `proxy_pass` hedefi | `http://127.0.0.1:5199` | `http://mssqlrealtime:8080` | `http://<uygulama-ip>:5199` |
| Docker ağı | gerekmez | iki konteyner aynı ağda | gerekmez |
| compose `ports:` | `127.0.0.1:5199:8080` | gereksiz | `<özel-ip>:5199:8080` |
| Güvenlik duvarı | gerekmez (loopback) | gerekmez | 🔴 **şart** |

🔴 En sık yapılan iki hata:

1. **nginx konteynerdeyken `127.0.0.1` yazmak.** O adres nginx konteynerinin *kendisidir* →
   `502 Bad Gateway`.
2. **nginx ayrı sunucudayken portu herkese açmak.** Uygulama artık loopback'in arkasında
   değil; kimlik doğrulama var ama izleme paneli internete açık durmamalı.

### C senaryosu — nginx ayrı sunucuda (buradaki kurulum)

Uygulama sunucusunda:

```bash
# Sadece nginx sunucusu bu porta ulaşabilsin
sudo ufw allow from <nginx-sunucu-ip> to any port 5199 proto tcp
sudo ufw deny 5199
sudo ufw status numbered      # kuralın sırası önemli: allow, deny'dan önce olmalı
```

nginx sunucusunda, conf içindeki **iki** `proxy_pass` satırı:

```nginx
proxy_pass http://10.0.0.42:5199;      # uygulama sunucusunun özel IP'si
```

⚠️ İki sunucu arasındaki trafik **düz HTTP**'dir. Aynı özel ağdaysanız kabul edilebilir;
aradaki hop internetten geçiyorsa bu bacağı da TLS'e alın ya da VPN kullanın — aksi hâlde
bearer token ve snapshot içeriği ağda açık akar.

`X-Forwarded-For` zincirini uygulama kabul eder (`KnownProxies` temizlenmiştir). Uygulama
portu güvenlik duvarıyla kısıtlı olduğu sürece bu güvenlidir; **portu herkese açarsanız
istemci IP'si taklit edilebilir hâle gelir.**

### B senaryosu — konteynerdeki nginx'in ağını bul

```bash
docker inspect <nginx-konteyner-adi> --format '{{json .NetworkSettings.Networks}}' | python3 -m json.tool
```

Sonra `docker-compose.yml` içindeki `networks:` bloklarını aç, ağ adını yaz ve iki
`proxy_pass` satırını `http://mssqlrealtime:8080;` yap.

## WebSocket — atlanırsa sessizce bozulur

SignalR önce WebSocket dener. `map $http_upgrade` bloğu ve `Upgrade` / `Connection`
başlıkları yoksa nginx yükseltmeyi düşürür, SignalR **hata vermeden** long-polling'e geriler:
uygulama "çalışır" görünür ama her telefon saniyede bir HTTP isteği açar. Mobil veride
bunu pil ömründen anlarsın, loglardan değil.

Ayrıca `/hubs/` için ayrı bir `location` var — tek sebebi zaman aşımı: hub bağlantısı
tasarımı gereği iki push arasında boştadır ve nginx'in varsayılan 60 sn `proxy_read_timeout`
değeri bağlantıyı yaklaşık her dakika düşürür.

## Sertifika

DNS A kaydı sunucuya baktıktan **sonra**:

```bash
sudo certbot --nginx -d izleme.example.com
```

Konteynerdeki nginx için certbot'u da konteynerde çalıştırman gerekir (ör.
`nginxproxy/acme-companion` veya `certbot/certbot` ile `webroot` yöntemi); `--nginx`
eklentisi host kurulumuna göre yazılmıştır.

Yenileme testi:

```bash
sudo certbot renew --dry-run
```

## Doğrulama

```bash
# 1. Sağlık
curl -s https://izleme.example.com/api/health

# 2. WebSocket gerçekten yükseliyor mu? (101 bekleniyor)
curl -si -o /dev/null -w '%{http_code}\n' \
  -H 'Connection: Upgrade' -H 'Upgrade: websocket' \
  -H 'Sec-WebSocket-Version: 13' -H 'Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==' \
  https://izleme.example.com/hubs/tools

# 3. Uygulama HTTPS'i görüyor mu? Logda scheme=https olmalı, http değil.
docker compose logs app | grep -i 'GET /api/health'
```

Üçüncü kontrol `UseForwardedHeaders` içindir: `X-Forwarded-Proto` iletilmezse uygulama
kendini HTTP sanır — üretilen bağlantılar ve güvenli çerez davranışı bozulur.
