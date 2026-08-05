# Sunucu İzleme — çok araçlı izleme platformu

Telefondan, tarayıcıdan ve masaüstünden **canlı** sunucu izleme. Tek uygulama, tek backend,
içine sürekli yeni araç (tool) eklenebilir.

Araçlar: **MSSQL İzleme** ve **Site / API İzleme**.

MSSQL İzleme — birden fazla müşterinin SQL Server'ında kim bağlı, ne çalışıyor,
ne kilitlenmiş, makinenin işlemci ve belleği ne durumda; hepsi saniyeler içinde ve
kendi belirlediğin sınırlar aşılınca telefona bildirim.

```
Telefon / Masaüstü / Tarayıcı  ──SignalR (WSS)──▶  .NET 10 servis
        (Tauri + SvelteKit)                            │
                                                       │ salt okunur DMV sorguları
                                    ┌──────────────────┼──────────────────┐
                                    ▼                  ▼                  ▼
                              Müşteri A SQL      Müşteri B SQL      Müşteri C SQL
```

## Neler ölçülüyor

| Alan | Kaynak | Not |
|---|---|---|
| Kim bağlı (uygulama, makine, IP, kullanıcı) | `dm_exec_sessions` + `dm_exec_connections` | |
| Ne çalışıyor (ifade metniyle) | `dm_exec_requests` + `dm_exec_sql_text` | Batch değil, **çalışan ifade** |
| Kim kimi bloke ediyor | `dm_exec_requests` + `most_recent_sql_handle` | Engelleyen uyuyorsa bile SQL'i görünür |
| Makine işlemci | `dm_os_ring_buffers` | ⚠️ ~dakikada bir örneklenir, yaşı da gösterilir |
| Makine bellek | `dm_os_sys_memory` | Canlı |
| SQL Server belleği, PLE, hedef bellek | `dm_os_process_memory`, `dm_os_performance_counters` | |
| Beklemeler | `dm_os_wait_stats` | **Delta** — kümülatif değil |
| Veritabanları, son yedek | `sys.databases`, `msdb.backupset` | |
| Servis hesabı | `dm_server_services` | Orijinal soruna cevap veren yer |

## Site / API İzleme (ikinci araç)

Müşteri sitesi ya da API ucu ayakta mı, kaç ms'de yanıtlıyor, TLS sertifikası ne zaman
bitiyor. “200 OK dönüyor ama sayfada hata var” durumunu gövde kontrolüyle yakalar.

Bu araç, platformun iddiasının kanıtı: alarm motoruna, bildirim kanallarına veya host'a
dokunulmadan eklendi — bir kayıt satırı ve bir ön yüz klasörü. Nasıl yapıldığı:
`docs/02-modul-ekleme.md`.

## Alarm ve bildirim

Kendi belirlediğin sınır aşılınca haber verir — **uygulama kapalıyken de**:

| Kanal | Uygulama kapalıyken | Kurulum |
|---|---|---|
| Telegram | ✅ | Ücretsiz, ~2 dakika (`docs/06-bildirimler.md`) |
| E-posta (SMTP) | ✅ | Mevcut mail sunucun |
| Webhook (Slack/Teams/kendi) | ✅ | Bir URL, isteğe bağlı HMAC imza |
| Uygulama içi | yalnız açıkken | Otomatik |

Teslim edilemeyen bildirim **kaybolmaz**: veritabanına yazılır ve 8 saat boyunca artan
aralıklarla yeniden denenir (ölçüldü).

Gürültü kontrolü zaten yerleşik: sınır üst üste N ölçümde aşılmadıkça alarm oluşmaz, süren
alarm en fazla tekrar penceresi kadar sıklıkta bildirilir, uyarı→kritik yükselmesi pencereyi
deler, ve **servis yeniden başladığında süren alarmlar tekrar bildirilmez**.

İzlenen sunucuya **hiçbir şey kurulmaz** ve **hiçbir şey yazılmaz** — tek istisna, arayüzden
açıkça onayladığın `KILL <spid>`. Gereken izin: `VIEW SERVER STATE`.

## Hızlı başlangıç (geliştirme)

```bash
# 1. Backend
cd src/MssqlRealtime.Api
Admin__Password='Guclu-Bir-Parola' ASPNETCORE_URLS=http://localhost:5199 dotnet run

# 2. Ön yüz
cd app && npm install && npm run dev          # tarayıcı: http://localhost:1420
npx tauri dev                                  # masaüstü uygulaması
npx tauri ios dev                              # iOS (Xcode gerekli)
npx tauri android dev                          # Android (Android SDK + NDK gerekli)
```

İlk çalıştırmada tek yönetici hesabı oluşturulur. `Admin__Password` vermezsen rastgele bir
parola üretilir ve **bir kez** log'a yazılır. Kayıt (register) ucu kapalıdır.

## Yayına alma

`docs/03-kurulum.md` — nginx + Let's Encrypt, Docker, systemd, Windows servisi.
Kısa yol: `deploy/nginx/README.md`.

## Belgeler

| Belge | İçerik |
|---|---|
| `docs/01-mimari.md` | Katmanlar, modül sınırları, veri akışı, alarm motoru |
| `docs/02-modul-ekleme.md` | **Yeni araç nasıl eklenir** — uçtan uca örnek |
| `docs/03-kurulum.md` | Yayına alma, SSL, servis, yedekleme |
| `docs/04-kirilma-noktalari.md` | Ne bozulur, bugün ne olur — ölçülmüş |
| `docs/05-olculen-bulgular.md` | Canlı ölçümle bulunan davranışlar ve tuzaklar |
| `docs/06-bildirimler.md` | **Telegram/e-posta/webhook kurulumu** ve gürültü kontrolü |

## Yapı

```
src/
  MssqlRealtime.Core/            Platform çekirdeği — modül sözleşmesi, alarm motoru
  MssqlRealtime.Infrastructure/  Kimlik, depo, şifreleme
  MssqlRealtime.Modules.Mssql/   MSSQL aracı (problar, poller, uçlar)
  MssqlRealtime.Modules.Http/    Site/API aracı (kontrol, sertifika, uçlar)
  MssqlRealtime.Api/             Host: SignalR hub, Identity, statik ön yüz
app/
  src/lib/modules/<araç>/        Aracın ekranları
  src-tauri/                     Mobil/masaüstü kabuk
deploy/                          nginx, systemd
tools/hub-dogrula.mjs            Canlı akış doğrulama betiği
tools/webhook-alici.mjs          Bildirim teslimatını yerelde görmek için alıcı
```

## Sürüm

`Directory.Build.props` → `VersionPrefix`. Değişiklikler `CHANGELOG.md`.
