# Değişiklik günlüğü

Biçim: [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/) · Sürümleme: [SemVer](https://semver.org/lang/tr/)

## [0.2.0] — 2026-08-05

Uygulama kapalıyken de haber almak, ve alarmların restart'ta kaybolmaması.

### Eklenen

- **Bildirim kanalları** (`docs/06-bildirimler.md`): Telegram, e-posta (SMTP) ve webhook.
  Sunucu alarmı kendisi gönderir — telefonun bağlı olması gerekmez. Her kanalın kendi
  seviye eşiği ve "normale döndü gönder" ayarı var.
  - Webhook gövdesi isteğe bağlı **HMAC-SHA256** imzayla (`X-Signature`) gönderilir;
    `format: slack` ile Slack/Teams gövdesi üretilir.
  - Kanal ekleme yeni bir `INotificationChannel` uygulamasından ibarettir; ayar formu
    sunucudan gelen alan tanımlarıyla üretildiği için istemci güncellemesi gerekmez.
  - Sırlar (bot token, SMTP parolası) şifreli saklanır ve API'den asla geri dönmez.
- **Alarm kalıcılığı ve geçmişi**: alarmlar SQLite'a yazılır, `/api/alerts` ile listelenir,
  uygulamada *Alarm geçmişi* ekranında görünür. 90 gün saklanır; süren alarm silinmez.
- **Restart dayanıklılığı**: açık alarmlar başlangıç saatleriyle birlikte geri yüklenir ve
  **yeniden bildirilmez**.
- Teslimat ayrı bir kuyrukta yapılır: yavaş bir SMTP sunucusu ölçüm döngüsünü bekletmez.

### Değişen

- 🔴 **EF Migrations'a geçildi.** `EnsureCreated` var olan veritabanına yeni tablo eklemiyordu,
  yani şema değiştiren her sürüm yalnız *yükseltmede* patlıyordu. Artık açılışta
  `Database.MigrateAsync()` çalışır. Migration'lar host projesindedir.
- `IRealtimePublisher.PublishAlertAsync` kaldırıldı; yerine `IAlertSink` geldi. Alarm tek
  çağrıyla üç yere gider: bağlı uygulamalar, kalıcı geçmiş, bildirim kanalları.
- Alarm kayıtlarında `DateTimeOffset` yerine UTC `DateTime` — SQLite `DateTimeOffset` ile
  `ORDER BY` yapamıyor.

### Ölçülen

- Hiçbir istemci bağlı değilken iki gerçek alarm webhook'a ulaştı.
- Restart sonrası: `Restored 2 alert(s)`, ardından **0** tekrar bildirim.
- 39 test yeşil (14 yeni: restart davranışı, kanal filtreleme).

## [0.1.0] — 2026-08-04

İlk sürüm. Çok araçlı izleme platformu ve ilk aracı olan MSSQL İzleme.

### Eklenen — platform

- **Modül (tool) mimarisi**: `IToolModule` sözleşmesi, `ModuleRegistry`, `/api/modules`
  keşif ucu. Yeni araç eklemek host'ta tek satır kayıt gerektirir
  (`docs/02-modul-ekleme.md`).
- **Alarm motoru** (`AlertEngine`): modülden bağımsız; ardışık ihlal şartı, sayaç sıfırlama,
  tekrar bildirim penceresi, şiddet artışında anında bildirim, "normale döndü" bildirimi
  yalnızca kullanıcıya haber verilmişse.
- **SignalR hub** (`/hubs/tools`): modül ve hedef bazlı gruplar, alarm kanalı, yeniden
  bağlanmada otomatik yeniden abonelik. WebSocket el sıkışması için token sorgu
  parametresinden okunur (yalnız hub yolunda).
- **Kimlik**: ASP.NET Core Identity + `MapIdentityApi` (bearer token, yenileme, kilitleme).
  Tek operatör hesabı açılışta seed edilir, `/api/auth/register` kapalıdır.
- **Sır saklama**: SQL parolaları ASP.NET Core Data Protection ile şifreli; hiçbir DTO'da
  dönmez.
- Reverse proxy desteği (`UseForwardedHeaders`) ve Windows servisi olarak çalışabilme.

### Eklenen — MSSQL aracı

- Problar: örnek bilgisi, oturumlar, çalışan istekler (ifade metniyle), blocking zinciri
  (engelleyen uyusa bile SQL'i), makine CPU/RAM, wait stats **delta**, veritabanları ve son
  yedek, servis hesapları.
- Sunucu başına bağımsız poller döngüsü; profil değişiklikleri 15 sn içinde uygulanır.
- Kullanıcı tanımlı eşikler: CPU, bellek, SQL Server belleği, bloke oturum, uzun sorgu,
  oturum sayısı, erişilememe.
- Bağlantı testi `VIEW SERVER STATE` iznini de doğrular ve eksikse gereken `GRANT`'i gösterir.
- Oturum sonlandırma (`KILL`), sistem oturumları için reddedilir ve denetim kaydına yazılır.

### Eklenen — istemci

- SvelteKit + Tauri 2: iOS, Android, macOS, Windows, Linux ve tarayıcı — tek kod tabanı.
- Mobil özet kartları (en kötü sunucu üstte), detay ekranı (oturumlar, çalışan, bloke,
  veritabanları, sistem), eşik ayar ekranı.
- Yerel bildirim (Tauri notification eklentisi; tarayıcıda Web Notifications).
- Form taslağı `sessionStorage`'a yazılır: yenileme veya sunucu hatası girilenleri
  kaybettirmez (parola hariç).
- Sunucu adresi uygulamaya gömülü değildir; giriş ekranında girilir.

### Dağıtım

- Docker imajı (ICU içeren Debian tabanlı runtime), `docker-compose.yml`, systemd unit.
- nginx yapılandırması ve üç senaryo rehberi: aynı host, konteyner, ayrı sunucu.

### Bilinen eksikler (v0.1.0 itibarıyla; ilk üçü v0.2.0'da çözüldü)

- ~~Uygulama kapalıyken bildirim gitmez~~ → v0.2.0: bildirim kanalları.
- ~~Alarm durumu bellektedir~~ → v0.2.0: SQLite'a yazılıyor.
- ~~EF Migration yok~~ → v0.2.0: migration'lar eklendi.
- Agent modu yok: müşteri SQL portuna doğrudan erişim gerekir.
