# Kurulum ve yayına alma

## 0. Ne nerede çalışır

| Bileşen | Nerede | Nasıl |
|---|---|---|
| İzleme servisi (API + poller + hub) | Kendi sunucun (Linux/Windows/Docker) | systemd · Docker · Windows servisi |
| nginx + TLS | Aynı host, konteyner **veya ayrı sunucu** | `deploy/nginx/README.md` |
| Ön yüz | Aynı servis tarafından statik sunulur | build çıktısı `wwwroot/` |
| Mobil/masaüstü uygulama | Telefon/PC | Tauri paketleri |
| İzlenen SQL Server'lar | Müşteri tarafında | **hiçbir kurulum yok** |

Servis bir arka plan servisidir: telefon kapalıyken de ölçer, alarm üretir ve kaydeder.

## 1. Docker ile (önerilen)

```bash
git clone <repo> && cd MssqlRealtime

cat > .env <<'EOF'
ADMIN_EMAIL=admin@ornek.com
ADMIN_PASSWORD=en-az-10-karakterli-guclu-parola
PUBLIC_ORIGIN=https://izleme.ornek.com
BIND_ADDRESS=127.0.0.1
EOF

docker compose up -d --build
docker compose logs -f app
```

`data/` klasörü oluşur: SQLite veritabanı, Data Protection anahtarları, loglar.

Doğrulama:

```bash
docker compose ps                      # health: healthy olmalı
curl -s http://127.0.0.1:5199/api/health
```

⚠️ **İmajı küçültmeye çalışmayın** — iki tuzak ölçüldü:

| Değişiklik | Sonuç |
|---|---|
| `-alpine` / `-chiseled` taban | `Microsoft.Data.SqlClient` ICU ister → bağlantı anında *"Globalization Invariant Mode is not supported"* (2026-08-04) |
| `curl` kurulumunu kaldırmak | `HEALTHCHECK` sessizce hiç geçmez, konteyner sonsuza kadar "starting" (2026-08-05) |

### Agent'ı da konteyner olarak çalıştırmak

Müşteride Docker varsa Windows servisi kurmaya gerek yok:

```bash
HUB_URL=https://izleme.example.com ENROLLMENT_KEY=... \
  docker compose -f docker-compose.agent.yml up -d
```

Hiçbir port yayınlanmaz — agent yalnız dışa doğru bağlanır.

⚠️ SQL Server konteynerin **dışında**, host üzerinde çalışıyorsa sunucu adresi olarak
`localhost` değil **`host.docker.internal`** yazın: konteyner içinde `localhost` konteynerin
kendisidir.

## 2. systemd ile (Docker'sız Linux)

```bash
dotnet publish src/MssqlRealtime.Api -c Release -o /opt/mssqlrealtime
cd app && npm ci && npm run build && cp -r build/* /opt/mssqlrealtime/wwwroot/

sudo useradd --system --no-create-home mssqlrealtime
sudo mkdir -p /var/lib/mssqlrealtime && sudo chown mssqlrealtime: /var/lib/mssqlrealtime

sudo cp deploy/systemd/mssqlrealtime.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now mssqlrealtime
sudo journalctl -u mssqlrealtime -f
```

## 3. Windows servisi olarak

Uygulama `UseWindowsService()` ile kurulur, ek kod gerekmez:

```powershell
dotnet publish src\MssqlRealtime.Api -c Release -r win-x64 --self-contained false -o C:\mssqlrealtime

sc.exe create MssqlRealtime binPath= "C:\mssqlrealtime\MssqlRealtime.Api.exe" start= auto
sc.exe description MssqlRealtime "MSSQL canli izleme servisi"

# Ortam degiskenleri (servis icin makine seviyesinde)
[Environment]::SetEnvironmentVariable('ASPNETCORE_URLS','http://127.0.0.1:5199','Machine')
[Environment]::SetEnvironmentVariable('Storage__DataDirectory','C:\ProgramData\MssqlRealtime','Machine')
[Environment]::SetEnvironmentVariable('Admin__Password','guclu-parola','Machine')

sc.exe start MssqlRealtime
```

Windows kimlik doğrulaması (`AuthMode = Integrated`) kullanacaksan servisi izlenecek
sunuculara erişebilen bir **etki alanı hesabıyla** çalıştır.

## 4. İlk giriş

İlk açılışta tek yönetici hesabı oluşturulur. `Admin__Password` verilmemişse rastgele bir
parola üretilip **bir kez** log'a yazılır:

```bash
docker compose logs app | grep -A3 "İlk kurulum"
# veya
sudo journalctl -u mssqlrealtime | grep -A3 "İlk kurulum"
```

Kayıt (`/api/auth/register`) ucu kapalıdır — ikinci bir kullanıcı oluşturulamaz.

## 5. nginx + Let's Encrypt

`deploy/nginx/README.md` — üç senaryo (aynı host / konteyner / ayrı sunucu), WebSocket
şartı ve doğrulama komutları orada.

## 6. İzlenecek SQL Server tarafı

Kurulum yok, yalnızca salt okunur bir hesap:

```sql
USE master;
CREATE LOGIN [izleme] WITH PASSWORD = N'guclu-parola', CHECK_POLICY = ON;
GRANT VIEW SERVER STATE TO [izleme];
GRANT VIEW ANY DEFINITION TO [izleme];

-- Son yedek tarihini de gormek icin (istege bagli)
USE msdb;
CREATE USER [izleme] FOR LOGIN [izleme];
ALTER ROLE db_datareader ADD MEMBER [izleme];
```

`KILL` yetkisi **ayrıdır** ve varsayılan olarak verilmez. Arayüzden oturum sonlandırmak
istiyorsan `ALTER ANY CONNECTION` gerekir:

```sql
GRANT ALTER ANY CONNECTION TO [izleme];   -- yalnizca gerekiyorsa
```

Bu izin verilmezse uygulama sorunsuz çalışır; yalnız "Kes" düğmesi hata döner.

## 7. Mobil uygulama

```bash
cd app
npx tauri ios build       # Xcode gerekli
npx tauri android build   # Android SDK + NDK gerekli
npx tauri build           # masaüstü (macOS/Windows/Linux)
```

Uygulama ilk açılışta **sunucu adresini** sorar (ör. `https://izleme.ornek.com`) —
adres uygulamanın içine gömülü değildir, aynı derleme her kurulumda kullanılabilir.

## 8. Yedekleme — atlanırsa acıtır

`data/` klasörünün tamamını yedekle:

| Dosya | Kaybedilirse |
|---|---|
| `mssqlrealtime.db` | Sunucu profilleri ve yönetici hesabı gider |
| `keys/` | 🔴 **Kayıtlı tüm SQL parolaları okunamaz hâle gelir** |
| `logs/` | Denetim izi (kim hangi oturumu kesti) gider |

```bash
tar czf mssqlrealtime-$(date +%F).tar.gz data/
```

## 9. Bildirim kanallarını aç — atlanırsa uygulama kapalıyken kimse haber almaz

Kurulum bittikten sonra uygulamada **⚙️ Bildirimler** ekranından en az bir kanal aç:
Telegram (en hızlısı, ~2 dakika), e-posta veya webhook. Her birinde **Test gönder** düğmesi
var; token'ın yanlış olduğunu gece 03:00'te değil şimdi öğren.

Ayrıntı ve Telegram bot adımları: `docs/06-bildirimler.md`.

## 10. Güncelleme

```bash
git pull
docker compose up -d --build     # veya: dotnet publish + systemctl restart
```

Şema açılışta **EF Migrations** ile güncellenir (`Database.MigrateAsync`), veri kaybı olmaz.
Yeni bir sürüm tablo eklediğinde bir şey yapman gerekmez.

⚠️ Yükseltmeden önce yine de `data/` klasörünü yedekle: migration geri alınamaz.
