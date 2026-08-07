# Sunucu İzleme

SQL Server ve web servislerini **telefondan canlı** izleyen, müşteri başına tek kurulan bir
panel. İzlenen sunucuya hiçbir şey kurulmaz, hiçbir şey yazılmaz.

[![Son sürüm](https://img.shields.io/github/v/release/hzkucuk/MssqlRealtime?label=son%20s%C3%BCr%C3%BCm)](https://github.com/hzkucuk/MssqlRealtime/releases/latest)

```text
Telefon / Tarayıcı / Masaüstü  ──SignalR (WSS)──▶  .NET 10 Windows servisi
       (Tauri + SvelteKit)                              │
                                                        │ salt okunur DMV sorguları
                                     ┌──────────────────┼──────────────────┐
                                     ▼                  ▼                  ▼
                               Müşteri A SQL      Müşteri B SQL      Müşteri C SQL
```

Araçlar: **MSSQL İzleme** ve **Site / API İzleme**. Platform çok araçlı; yeni bir araç
eklemek host'ta tek satır (`docs/02-modul-ekleme.md`).

---

## Kurulum — 5 dakika

**1. Paneli kur.** [Son sürümden](https://github.com/hzkucuk/MssqlRealtime/releases/latest)
`SunucuIzleme-Setup-*.exe` indirin ve SQL Server'ın bulunduğu makinede çalıştırın.

Sihirbaz üç şey sorar: yönetici e-postası/parolası, panelin dışarıdan erişileceği adres ve
port. Genel adres boş bırakılırsa panel yalnız o makineden açılır (`127.0.0.1:5199`) ve
güvenlik duvarı kuralı **açılmaz** — varsayılan kurulum kazara ağa açılmaz.

Kurulum, sağlık ucu cevap verene kadar bekler: "kuruldu" diyorsa panel gerçekten ayaktadır.

| | |
|---|---|
| Program | `C:\SunucuIzleme` |
| Veri (veritabanı + şifreleme anahtarları) | `C:\SunucuIzleme\data` — **kaldırmada silinmez** |
| Servis | `SunucuIzleme`, otomatik başlar, çökerse yeniden başlar |
| Gereken | Windows Server 2016+ · .NET **kurmaya gerek yok**, pakete gömülü |

**2. SQL tarafında salt okunur bir hesap açın.** SSMS'te `setup/sql-kurulum.sql` betiğini
çalıştırmanız yeterli. Gereken tek izin `VIEW SERVER STATE`'tir.

`GRANT ALTER ANY CONNECTION` yalnızca arayüzden oturum sonlandırma (`KILL`) kullanacaksanız
gerekir; verilmezse ürün sorunsuz çalışır, yalnız "Kes" düğmesi hata döner.

**3. Telefona uygulamayı kurun.** Aynı sürümden `SunucuIzleme-*.apk`. Uygulamada
**Panel adresi** olarak kurulumda girdiğiniz genel adresi yazın — şema dahil, birebir aynı.
Yanlışsa arayüz açılır ama giriş CORS hatasıyla sessizce başarısız olur.

Telefona kurmak istemezseniz panel tarayıcıdan da açılır; "ana ekrana ekle" ile tam ekran
çalışır.

### Yükseltme

Yeni `setup.exe`'yi mevcut kurulumun üzerine çalıştırın. Yükseltmede **hesap sorulmaz**,
ağ ayarları hatırlanır, veriler korunur. Sessiz de yapılabilir:

```text
SunucuIzleme-Setup-*.exe /VERYSILENT
```

### Dışarıdan erişim

Panelin önüne ters vekil sunucu (nginx / Nginx Proxy Manager) koyun ve **Websockets
Support**'u açın — kapalıysa hata alınmaz, bağlantı sessizce long-polling'e düşer ve telefon
pili erir. Vekil sunucu başka bir makinedeyse kurulumda **IP'sini girin**: `X-Forwarded-For`
başlığı yalnız oradan ve loopback'ten kabul edilir.

Ayrıntı: `docs/03-kurulum.md`.

---

## Ne yapar

### MSSQL İzleme

| Alan | Kaynak |
|---|---|
| Kim bağlı (uygulama, makine, IP, kullanıcı) | `dm_exec_sessions` + `dm_exec_connections` |
| Ne çalışıyor — ifade metniyle | `dm_exec_requests` + `dm_exec_sql_text` |
| Kim kimi bloke ediyor | `dm_exec_requests` + `most_recent_sql_handle` |
| Makine işlemcisi | `dm_os_ring_buffers` — ~dakikada bir örneklenir, **yaşı gösterilir** |
| Makine belleği | `dm_os_sys_memory` |
| SQL belleği, PLE, hedef bellek | `dm_os_process_memory`, `dm_os_performance_counters` |
| Beklemeler | `dm_os_wait_stats` — **delta**, kümülatif değil |
| Veritabanları, son yedek | `sys.databases`, `msdb.backupset` |
| Servisler ve çalıştıkları hesap | `dm_server_services` |
| Sürüm ve edisyon | `SERVERPROPERTY` — SQL Server 2016 dahil her sürümde |

**Oturumlar tablosu** bir tabloya ihtiyacı olan her şeyi yapar: arama (SPID, uygulama,
makine, kullanıcı, veritabanı), **kademeli gruplama** (önce makine, içinde uygulama…),
sütun gizleme/genişletme/**sürükleyerek sıralama**, ve sütun başına **özet** (toplam,
ortalama, adet, en küçük, en büyük) hem grup satırlarında hem tablo altında.

Grup satırındaki `⋮` düğmesi (ya da sağ tık / uzun basma) alt grupları toplu açıp kapatır.

### Site / API İzleme

Adres ayakta mı, kaç ms'de yanıtlıyor, TLS sertifikası ne zaman bitiyor. "200 dönüyor ama
sayfada hata var" durumunu gövde kontrolüyle yakalar.

Bu araç platformun iddiasının kanıtı: alarm motoruna, bildirim kanallarına ve host'a
dokunulmadan eklendi.

### Raporlar

Ölçümler dakikada bir saklanır; **gün / hafta / ay / yıl** aralıklarında grafik olarak
okunur. Hangi alanların çizileceğini siz seçersiniz, grafik türü çizgi/alan/sütun olarak
değişir, bir grafik tam ekrana alınabilir ve aynı veriler sıralanabilir bir tabloda görünür.

Kayıtlar yaşlanır: bir haftadan eskiler saatlik, üç aydan eskiler günlük ortalamaya iner,
**iki yıldan eskiler silinir**. Sunucuya ulaşılamayan turlar kaydedilmez — kesintinin üstüne
sakin bir ay çizmek, bir izleme geçmişinin söyleyemeyeceği tek yalandır.

### Alarm ve bildirim

Sınır aşılınca haber verir — **uygulama kapalıyken de**:

| Kanal | Uygulama kapalıyken | Kurulum |
|---|---|---|
| Telegram | ✅ | Ücretsiz, ~2 dakika (`docs/06-bildirimler.md`) |
| E-posta (SMTP) | ✅ | Mevcut mail sunucunuz |
| Webhook (Slack/Teams/kendi) | ✅ | Bir URL, isteğe bağlı HMAC imza |
| Uygulama içi | yalnız açıkken | Otomatik |

Alarm kaydı **kimin tükettiğini** de taşır: en çok CPU/bellek yakan oturum, kilit zincirinin
başı ya da uzun süren sorgunun sahibi — SPID, uygulama, kullanıcı, makine. Alarm anında
yakalanır, çünkü bildirimi okuduğunuzda o oturum çoktan kapanmıştır.

**Sessiz saatler:** mesai dışında bildirim kesilmez, **sessiz gönderilir** (Telegram'ın
`disable_notification` özelliği). Mesaj ve geçmiş eksilmez, telefon yalnız titremez. Kesmek
gelmeyen alarm demektir. Çalışma günleri, saatler ve resmî tatil/bayram takvimi ayarlanabilir;
bayram tarihleri hesaplanır ve elle düzeltilebilir.

Gürültü kontrolü yerleşiktir: sınır üst üste N ölçümde aşılmadıkça alarm oluşmaz, süren alarm
en fazla tekrar penceresi kadar sık bildirilir, uyarı→kritik yükselmesi pencereyi deler ve
servis yeniden başladığında süren alarmlar **tekrar bildirilmez**.

---

## Güvenlik duruşu

- İzlenen sunucuya **yazma yok**. Tek istisna, arayüzden açıkça onaylanan `KILL <spid>`;
  sistem oturumları (`session_id ≤ 50`) reddedilir ve işlem denetim kaydına yazılır.
- SQL parolaları **Data Protection ile şifreli** saklanır ve API'den geri dönmez; yalnız
  `hasPassword: true` bilgisi gider. Windows kimlik doğrulaması kullanılırsa parola hiç
  saklanmaz.
- Kurulum parolası registry'ye **yazılmaz**; kilitli veri klasörüne dosya olarak bırakılır ve
  uygulama hesabı kurar kurmaz siler.
- Kayıt (register) ucu **kapalıdır**. Tek operatör hesabı kurulumda oluşur. Giriş ucunda hız
  sınırı, captcha ve 5 denemede hesap kilidi vardır.
- `X-Forwarded-For` yalnız loopback'ten ve kurulumda girilen vekil IP'sinden kabul edilir.
- Güvenlik başlıkları gönderilir: `X-Frame-Options: DENY`, `X-Content-Type-Options`,
  `Referrer-Policy`, CSP `frame-ancestors 'none'`.

**Bilinen, kapatılmamış:** servis Windows'ta **LocalSystem** ile çalışır ve veri klasörü
makinedeki diğer yerel kullanıcılara okunabilir. Klasörü daraltma denendi ve servisin kendi
veritabanını açmasını engellediği için geri alındı (`docs/04-kirilma-noktalari.md`).
Kurulu paneli denetlemek için: `tools/windows-guvenlik-denetimi.ps1` — hiçbir şeyi
değiştirmez, yalnız ölçer.

---

## Kaynaktan derleme

```bash
# Windows yayın klasörü (self-contained, ~123 MB) — macOS/Linux/Windows
./tools/windows-paketle.sh

# setup.exe — Inno Setup, Wine altında konteynerde; Windows makine GEREKMEZ (Docker gerekir)
./tools/setup-derle.sh

# Android APK (arm64) — derler, hizalar, imzalar
./tools/apk-derle.sh
```

### Geliştirme

```bash
# Sunucu
cd src/MssqlRealtime.Api
dotnet run -- --Storage:DataDirectory=./veri --urls=http://localhost:5199

# Ön yüz
cd app && npm install && npm run dev     # tarayıcı: http://localhost:1420
npx tauri dev                            # masaüstü
npx tauri android dev                    # Android (SDK + NDK 27, JDK 21)
```

İlk açılışta tek yönetici hesabı oluşturulur; parola verilmezse rastgele üretilir ve **bir
kez** log'a yazılır.

Doğrulama: `dotnet build`, `dotnet test`, `cd app && npm run check` — üçü de temiz olmalı.

---

## Belgeler

| Belge | İçerik |
|---|---|
| [`docs/01-mimari.md`](docs/01-mimari.md) | Katmanlar, modül sınırları, veri akışı, alarm motoru |
| [`docs/02-modul-ekleme.md`](docs/02-modul-ekleme.md) | **Yeni araç nasıl eklenir** — uçtan uca örnek |
| [`docs/03-kurulum.md`](docs/03-kurulum.md) | Yayına alma, ters vekil sunucu, SSL, yedekleme |
| [`docs/04-kirilma-noktalari.md`](docs/04-kirilma-noktalari.md) | Ne bozulur, **bugün** ne olur — ölçülmüş |
| [`docs/05-olculen-bulgular.md`](docs/05-olculen-bulgular.md) | Çalıştırılarak bulunmuş davranışlar ve tuzaklar |
| [`docs/06-bildirimler.md`](docs/06-bildirimler.md) | Telegram/e-posta/webhook kurulumu, sessiz saatler |
| [`CHANGELOG.md`](CHANGELOG.md) | Ne değişti ve **ne ölçüldü** |

Belgelerin ayırt edici tarafı `04` ve `05`: birinde neyin bozulacağı ve bugün ne olduğu
yazılı, diğerinde canlı ölçümle bulunmuş her tuzak tarih/saatiyle duruyor. Bu projede
çalıştırılmadan doğru sayılan hiçbir şey yok.

## Yapı

```text
src/
  MssqlRealtime.Core/            Platform çekirdeği — modül sözleşmesi, alarm motoru
  MssqlRealtime.Infrastructure/  Kimlik, depo, şifreleme, bildirim kanalları, ölçüm geçmişi
  MssqlRealtime.Modules.Mssql/   MSSQL aracı (problar, poller, uçlar)
  MssqlRealtime.Modules.Http/    Site/API aracı
  MssqlRealtime.Api/             Host: SignalR hub, Identity, statik ön yüz, migration'lar
app/
  src/lib/modules/<araç>/        Aracın ekranları
  src/lib/components/            Tablo, grafik, menü bileşenleri
  src-tauri/                     Mobil/masaüstü kabuk
setup/                           Inno Setup betiği + SQL kurulum betiği
tools/                           Paketleme, imzalama, güvenlik denetimi, yardımcı betikler
docs/                            Belgeler
```

## Sürümleme

Tek kaynak: `Directory.Build.props` → `VersionPrefix`. Sürüm numarası panelde ekranda görünür;
telefon uygulaması panelden eskiyse uygulama bunu söyler ve indirme bağlantısı verir.
