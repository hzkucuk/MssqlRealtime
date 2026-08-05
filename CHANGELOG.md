# Değişiklik günlüğü

Biçim: [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/) · Sürümleme: [SemVer](https://semver.org/lang/tr/)

## [0.5.0] — 2026-08-05

Sessiz kalmayan izleme.

### Eklenen

- **Agent sessizlik alarmı.** Bir agent yapılandırılan süreden (varsayılan 3 dk) uzun süre
  sessiz kalırsa kritik alarm üretilir ve mesaj sessizliğin bedelini söyler:
  *"… bu agent'a bağlı N sunucu artık izlenmiyor."*
  - Hiç bağlanmamış agent için alarm üretilmez (kurulumu bitmemiş, arıza değil).
  - Sunucu atanmamış agent için alarm üretilmez (sessizliğinin bedeli yok).
  - Hub yeniden başladığında 45 sn beklenir; agent'lara yeniden bağlanma şansı tanınır.
  - Karar mantığı `AgentHealthEvaluator` içinde saf bir fonksiyon — doğrudan test edilir.
- `tools/telegram-chatid.mjs` ve `tools/telegram-kur.sh`: bot kurulumunu arayüz olmadan
  tamamlayıp test etmek için.

### Ölçülen

- Agent süreci öldürüldü → 1 dk sonra kritik alarm (webhook + Telegram); agent geri geldi →
  "normale döndü" bildirimi. Her ikisi de loglarla doğrulandı.
- **Telegram kanalı canlı doğrulandı** — kullanıcı botu kurdu, mesajlar ulaştı.
- 77 test yeşil (13 yeni).

## [0.4.0] — 2026-08-05

Agent modu: NAT arkasındaki müşteri sunucuları.

### Eklenen

- **`MssqlRealtime.Agent`** — müşteri sunucusunda çalışan küçük servis. Merkeze **dışarı
  doğru** bağlanır, iş listesini alır, SQL Server'ı yerelden ölçer ve sonucu gönderir.
  Müşteri güvenlik duvarında hiçbir port açılmaz.
- Agent yönetimi: kayıt anahtarı üretme (bir kez gösterilir, hash'i saklanır), anahtar
  yenileme, bağlı/çevrimdışı durumu, atanmış sunucu sayısı.
- Sunucu profiline **agent ataması**. Atanan sunucuyu merkez artık kendisi ölçmez; atama
  değişikliği bağlı agent'a **anında** iletilir (yeniden başlatma gerekmez).
- Agent silinince ona atanmış sunucular sessizce izlemeden düşmez, merkeze geri döner.

### Tasarım kararları

- 🔴 **Agent hiçbir şeye karar vermez.** Ölçer ve gönderir; eşikler, alarm motoru, bildirim
  ve geçmiş merkezde kalır. Böylece agent üzerinden izlenen sunucu doğrudan izlenenle aynı
  sonucu verir, ve eski/ele geçirilmiş bir agent alarm bastıramaz.
- Problar paylaşılır: agent hub'ın kullandığı **aynı** `ISqlProbe` sınıflarını çalıştırır.
- SQL parolası agent diskine **yazılmaz** — TLS üzerinden gelir, bellekte durur.
- Bağlantı yokken ölçüm biriktirilmez: beş dakika önceki bir snapshot'ı "canlı" göstermek
  yanıltıcı olur.
- Protokol sürümü uyuşmazsa kayıt reddedilir; sessizce yanlış veri göndermek yerine bağlanmaz.

### Ölçülen

- Agent ayrı süreç olarak çalıştırıldı: kayıt → yapılandırma → yerel ölçüm → hub'da alarm
  değerlendirme zinciri uçtan uca çalıştı (`Alert raised cpu … (via agent …)`).
- 64 test yeşil (9 yeni: anahtar üretimi/hash'i, bağlantı defteri yarış durumları).

## [0.3.0] — 2026-08-05

İkinci araç, ve bildirimlerin gerçekten kaybolmaması.

### Eklenen — Site / API İzleme modülü (yeni araç)

- HTTP/HTTPS uç noktaları izleme: ayakta mı, kaç ms'de yanıtlıyor, beklenen durum kodu ve
  **gövde içeriği** doğru mu (“200 OK dönen hata sayfası” tuzağını yakalar).
- **TLS sertifikası bitiş takibi** — kendi kısa ömürlü el sıkışmasıyla okunur; havuzdaki bir
  bağlantıdan okunan sertifika hangi isteğe ait olduğu belirsiz olduğu için ayrı tutuldu.
- Hedef başına bağımsız kontrol döngüsü, son 60 ölçüm üzerinden erişilebilirlik yüzdesi.
- Eşikler: erişilemiyor, yavaş yanıt, sertifika bitişine kalan gün.
- Kaydetmeden önce “Şimdi dene”.

> Bu modül platform iddiasının sınavıydı: alarm motoruna, bildirim kanallarına, hub'a veya
> host'a **dokunulmadan** eklendi — backend'de bir kayıt satırı, ön yüzde bir klasör.
> Bildirim, alarm bastırma, geçmiş ve ayar ekranı hazır geldi.

### Eklenen — bildirim dayanıklılığı

- **Kalıcı outbox**: teslim edilemeyen bildirim kaybolmaz, veritabanına yazılır ve geri
  çekilmeli aralıklarla (30 sn → 2 dk → 5 dk → 15 dk → 30 dk) 8 saat boyunca yeniden denenir.
- Kanal arada kapatılırsa yeniden deneme durur (karar, hata değil).
- `/api/notifications/outbox` ile bekleyen/vazgeçilen sayısı görünür — sessizce biriken bir
  kuyruk, kendisi bir arıza belirtisidir.

### Düzeltilen

- HTTP kontrolleri artık `User-Agent` gönderiyor. Ölçüldü: `api.github.com` User-Agent'sız
  isteğe **403** döndü — düzeltilmeseydi izleme aracı olmayan kesintiler uydururdu.

### Ölçülen

- Webhook alıcısı kapalıyken 3 bildirim outbox'ta birikti; alıcı açılınca 2. ve 3. denemede
  teslim edildi ve kuyruk boşaldı.
- Gerçek hedeflerle: GitHub API 200/49 ms/sertifika 55 gün, DNS hatası doğru yakalandı,
  gövde kontrolü çalıştı; bir down→up döngüsünde “normale döndü” bildirimi de gitti.
- 55 test yeşil (16 yeni).

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
