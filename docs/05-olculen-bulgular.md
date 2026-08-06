# Ölçülen bulgular

> Buradaki her satır **çalıştırılarak** bulundu, belgeden okunarak değil. Tarih ve saat
> taşır, çünkü aynı gün içinde davranış değişebilir.

## 2026-08-04 19:0x — `InvariantGlobalization=true` SqlClient'i kırar

`Directory.Build.props` içine performans refleksiyle `<InvariantGlobalization>true</...>`
koymuştum. Sonuç: bağlantı anında

```
Globalization Invariant Mode is not supported.
```

`Microsoft.Data.SqlClient` ICU ister. Etkileri:

- Ayar `false` yapıldı ve dosyaya gerekçesi yazıldı.
- **Docker imajı `-alpine` veya `-chiseled` olamaz** — bu varyantlar ICU getirmez.
  `Dockerfile` tam Debian tabanlı `aspnet:10.0` kullanır.

İlginç yan etki: hata alarm zincirini doğruladı — sunucu "erişilemiyor" oldu, `offline`
alarmı ateşlendi ve mesajı kullanıcıya taşındı. Yani zincir daha ilk gerçek hatada çalıştı.

## 2026-08-04 20:0x — Dapper positional record ≠ DMV tipleri

Prob satırları `record Row(int SessionId, …)` olarak yazılmıştı. Dapper positional
constructor'ı **birebir CLR tipi** ile eşleştirir ve dönüşüm yapmaz:

```
A parameterless default constructor or one matching signature
(System.Int16 SessionId, …) is required for SessionsProbe+Row materialization
```

Çünkü `session_id` **smallint**, `cpu_time` ve `memory_usage` **int**, `blocking_session_id`
**smallint**. Dört prob birden sessizce boş dönüyordu — bağlantı "başarılı" görünürken.

Çözüm: tüm `Row` tipleri **settable property'li sınıf** oldu; Dapper o zaman dönüşüm yapıyor.

> Ders: DMV kolon tiplerini varsayma. `smallint`/`int` farkı derlemede değil, **çalışma
> anında** ve **sessizce** patlar.

## 2026-08-04 20:0x — Pahalı problar ilk turda hiç çalışmıyordu

Seyreltme koşulu `pollNumber % EveryNthPoll != 0` idi ve sayaç 1'den başlıyor. `EveryNthPoll = 60`
olan problar (sürüm, veritabanları, servisler) **60. tura kadar**, yani 3 dakika, hiç
çalışmadı. Yeni eklenen bir sunucunun ekranı o süre boyunca yarı boş kalıyordu.

Düzeltme: `pollNumber > 1` koşulu eklendi — ilk tur her zaman her şeyi çalıştırır.

## 2026-08-04 20:1x — `SERVERPROPERTY('HostPlatform')` NULL dönüyor

SQL Server 2022 (16.0.4252.3) üzerinde `SERVERPROPERTY('HostPlatform')` **NULL**.
Doğru kaynak `sys.dm_os_host_info.host_platform`. Prob ona çevrildi.

## 2026-08-04 19:4x — Identity şemasını elle kurmak 500 üretir

`AddAuthentication(IdentityConstants.BearerScheme).AddIdentityCookies()` +
`AddIdentityCore().AddApiEndpoints()` kombinasyonunda korumalı her uç:

```
No authenticationScheme was specified, and there was no DefaultChallengeScheme found.
```

`AddIdentityApiEndpoints<TUser>()` bearer + cookie şemalarını ve **varsayılanlarını** birlikte
kurar. Elle kurmaya çalışmak, 401 yerine 500 demek.

## 2026-08-04 20:2x — Konteynerde CPU %100 görünüyor

Docker'daki SQL Server 2022'de `RING_BUFFER_SCHEDULER_MONITOR` içindeki `SystemIdle` **0**
geliyor; dolayısıyla makine CPU'su hep %100 hesaplanıyor. Hedef platform Windows Server
olduğu için ürün için engel değil, ama:

- Bu ring buffer **dakikada bir** örneklenir → değer 60 saniyeye kadar eski olabilir.
- Bu yüzden `CpuSampleAgeSeconds` ölçülüp taşınıyor ve arayüzde 90 saniyeyi geçince
  *"⚠️ İşlemci değeri N saniye önce ölçüldü"* uyarısı çıkıyor; alarm mesajına da not düşülüyor.

> Ölçülmemiş bir sayıyı canlıymış gibi göstermek, yanlış sayı göstermekten daha kötüdür.

## 2026-08-04 20:3x — Alarm bastırma uçtan uca doğrulandı

`tools/hub-dogrula.mjs` ile canlı hub üzerinden:

- 14 saniyede **5 snapshot** aktı (3 sn aralık).
- CPU alarmı aktifken **bildirim gönderilmedi** — 15 dakikalık tekrar penceresi çalışıyor.
- Oturum sayısı eşiği 1'e çekilince **yeni kural anında bildirim üretti**
  (`🟠 Yerel Test SQL | 2 açık oturum — sınır 1`), CPU alarmı bastırılmaya devam etti.

## 2026-08-05 09:1x — `EnsureCreated` yükseltmeyi sessizce bozar

Bildirim tabloları eklendi, uygulama açıldı, `/api/notifications/channels` **500** döndü:

```
SQLite Error 1: 'no such table: NotificationChannelSettings'
```

`Database.EnsureCreatedAsync()` **var olan** bir veritabanına dokunmaz — yeni tablo eklemez.
Yani şema değiştiren her sürüm yalnız *yükseltmede* patlar, temiz kurulumda çalışır: fark
edilmesi en zor hata türü.

Düzeltme: **EF Migrations**. Migration'lar `MssqlRealtime.Api` altında (`MigrationsAssembly`
ile), çünkü şema platform tablolarıyla modül tablolarının birleşimidir ve hangi modüllerin
derlemede olduğunu yalnız host bilir. Açılışta `Database.MigrateAsync()`.

## 2026-08-05 09:2x — SQLite `DateTimeOffset` ile ORDER BY yapamaz

```
SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses.
```

Alarm geçmişi tanımı gereği zamana göre sıralı. Kayıt tipleri UTC `DateTime`'a çevrildi;
dışarıya hâlâ `DateTimeOffset` veriliyor. (Aynı tuzak `dm_exec_sessions` tarihlerinde yok:
onlar zaten `DateTime`.)

## 2026-08-05 09:2x — Uygulama kapalıyken bildirim: doğrulandı

Yerel bir webhook alıcısı (`tools/webhook-alici.mjs`) ile, **hiçbir istemci bağlı değilken**:

```
🟠 Sunucu İzleme | Test: Webhook bildirimi çalışıyor.
🔴 Yerel Test SQL | İşlemci: İşlemci %100 — sınır %85   | ölçülen=100 sınır=85
🟠 Yerel Test SQL | Oturum sayısı: 2 açık oturum — sınır 1 | ölçülen=2 sınır=1
```

HMAC imzası `X-Signature` başlığında geldi. API logu: `Alert delivered via webhook`.

## 2026-08-05 09:2x — Restart alarmları tekrar bildirmiyor

İki alarm aktifken servis yeniden başlatıldı:

```
[09:29:09 INF] Restored 2 alert(s) that were active before restart
```

Sonrasında yeni webhook teslimatı: **0**. Başlangıç saatleri korundu, dolayısıyla "6 saattir
sürüyor" bilgisi restart'ta sıfırlanmıyor.

## 2026-08-05 13:1x — İzleme aracı `User-Agent` göndermezse kesinti uydurur

Yeni HTTP modülünün ilk canlı denemesinde `https://api.github.com/zen` **403 Forbidden**
döndü ve modül bunu haklı olarak "erişilemiyor" sayıp alarm üretti. Sebep sitede değil bizde:
istek `User-Agent` başlığı taşımıyordu. GitHub API bunu reddediyor, birçok WAF da öyle.

Düzeltildi: `SunucuIzleme/1.0 (+monitoring)`. Sonrası: **200, 49 ms**.

> Bir izleme aracının en kötü hatası, izlediği şeyi bozuk göstermektir.

## 2026-08-05 13:1x — Teslim edilemeyen bildirim artık kaybolmuyor

Webhook alıcısı **kapalıyken** üç alarm üretildi:

```
[13:15:10 WRN] Notification channel webhook failed: Connection refused (localhost:9099)
outbox: {"pending":3,"abandoned":0}
```

Alıcı açıldıktan sonra, hiçbir müdahale olmadan:

```
[13:15:55 INF] Queued notification delivered via webhook after 2 attempt(s)
[13:17:55 INF] Queued notification delivered via webhook after 3 attempt(s)
[13:17:55 INF] Queued notification delivered via webhook after 3 attempt(s)
outbox: {"pending":0,"abandoned":0}
```

Aynı testte beklenmedik bir doğrulama daha çıktı: API yeniden başlatıldığı için
"Yerel API sağlığı" hedefi gerçekten düştü ve toparlandı — hem `down` hem de
"✅ normale döndü" bildirimi doğru sırayla gitti.

## 2026-08-05 13:0x — İkinci modül: mimari iddiası sınandı

Site/API izleme modülü eklendi. Dokunulan ortak dosya sayısı:

| Katman | Değişiklik |
|---|---|
| Alarm motoru | **0** |
| Bildirim kanalları | **0** |
| SignalR hub | **0** |
| Kimlik / yetkilendirme | **0** |
| Host (`Program.cs`) | 1 satır (`AddToolModule<HttpModule>`) + 1 using |
| Ön yüz kayıt | 1 satır (`modules` dizisine ekleme) + 1 import |

Modülün kendi dosyaları dışında toplam **4 satır**. Bildirim, alarm bastırma, geçmiş,
kalıcılık ve ayar ekranı hazır geldi.

## 2026-08-05 14:2x — Telegram canlı doğrulandı

Kullanıcı botu kurdu, token ve chat id'yi arayüzden girdi; **mesajlar geldi**. Bildirim
zincirinin son doğrulanmamış halkası kapandı: artık üç kanalın (Telegram, webhook, e-posta
kodu aynı yoldan geçiyor) ikisi canlı ölçülmüş durumda.

## 2026-08-05 14:2x — Agent sessizliği artık sessiz kalmıyor

Agent süreci öldürüldü (müşteri sunucusu kapandı senaryosu). Eşik 1 dakikaya çekilmişti:

```
[14:27:21 WRN] Agent alert raised:
  Musteri A sunucusu 1 dakikadır sessiz — bu agent'a bağlı 1 sunucu artık izlenmiyor.
  ölçülen=1.1 dk · sınır=1 dk · seviye=Critical
```

Agent geri başlatıldığında:

```
[14:27:42 INF] Agent Musteri A sunucusu connected
[14:27:51 WRN] Agent alert cleared: Agent sessiz normale döndü.
```

> Bu, bir izleme aracının en tehlikeli açığıydı: ölçüm gelmemesi ile "sorun yok" birbirinden
> ayırt edilemiyordu. Mesajın *"bu agent'a bağlı N sunucu artık izlenmiyor"* demesi kasıtlı —
> sessizliğin neye mal olduğunu söylemeyen bir alarm eksik bir alarmdır.

## 2026-08-05 11:5x — Docker imajı ilk kez gerçekten derlendi (ve iki hata çıktı)

`Dockerfile` v0.1.0'dan beri duruyordu ama **hiç çalıştırılmamıştı**. Çalıştırınca:

**① `HEALTHCHECK` hiçbir zaman geçmiyordu.**

```
docker inspect → health: starting  (sonsuza kadar)
/bin/sh: 1: curl: not found
```

`aspnet:10.0` imajında ne curl ne wget var. Sonuç: konteyner sonsuza kadar "starting"de
kalır, `depends_on: condition: service_healthy` çalışmaz, orchestrator sağlıksız sanıp
yeniden başlatabilir. Düzeltme: imaja `curl` eklendi (~4 MB).

**② Agent, `runtime:10.0` imajında başlamıyordu.**

```
framework 'Microsoft.AspNetCore.App', version '10.0.0' was not found
```

Agent hiçbir endpoint map'lemiyor ama `Core` ve `Modules.Mssql` mod<ül uç API'si için
`Microsoft.AspNetCore.App` framework referansı taşıyor ve bu bağımlılık miras kalıyor.
Düzeltme: agent imajı da `aspnet:10.0` tabanlı. (Core'un web tarafını ayrı bir derlemeye
bölmek daha küçük imaj verirdi — henüz değmez, not düşüldü.)

> Her ikisi de yalnız **çalıştırınca** görülebilecek hatalardı: `dotnet build` ikisini de
> yeşil geçiyordu.

**Doğrulanan tam yığın:** hub konteyneri (healthy) ← SignalR ← agent konteyneri → SQL Server
16.0.4252.3 ölçüldü, veri hub'a ulaştı, alarm üretildi. `/data` biriminde veritabanı ve
anahtar halkası kalıcı.

## 2026-08-05 — Windows Server 2016/2019'da `Invoke-WebRequest` GitHub'dan indiremiyor

```
Invoke-WebRequest : İstek durduruldu: SSL/TLS güvenli kanalı oluşturulamadı.
```

PowerShell 5.1 varsayılan olarak TLS 1.0/1.1 dener; GitHub yalnız TLS 1.2+ kabul eder.
Sunucunun ağıyla ya da sertifikayla ilgisi yok.

Çözüm (yalnız o oturum için, kalıcı değişiklik yapmaz):

```powershell
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
```

`curl.exe` bir alternatiftir (kendi TLS yığınını kullanır) **ama her sunucuda yoktur**:
2026-08-06'da bir **Windows Server 2016** kurulumunda `CommandNotFoundException` verdi —
curl.exe Windows'a 1803 ile geldi, Server 2016'da hiç yoktur.

⚠️ Ayrıca PowerShell'de `curl` bir takma addır ve `Invoke-WebRequest`'e gider; kullanılacaksa
**`.exe` uzantısı şart**.

**Her yerde çalışan yol** TLS satırı + `Invoke-WebRequest -UseBasicParsing`.

## 2026-08-06 — Windows Server 2016 destekleniyor (ölçüldü)

Self-contained .NET 10 paketi **Windows Server 2016** üzerinde sorunsuz çalışıyor: servis
kuruldu, panel açıldı, canlı MSSQL verisi aktı (22 oturum, gerçek müşteri kurulumu).

Hedef makineye .NET kurulmadı — runtime pakete gömülü olduğu için işletim sisteminin .NET
sürümüyle ilgisi yok. Bu, eski sunucularda çalışan müşteriler için belirleyici: 2016'da
kurulu .NET Framework/Core sürümü ne olursa olsun ürün etkilenmiyor.

Bu sürümlerde dikkat edilecek tek şey indirme adımı (TLS 1.2 ve curl.exe'nin yokluğu).

## 2026-08-06 18:05 — Windows güvenlik bulguları ölçüldü (Windows 11 Pro ARM64, VM)

Kurulum `windows-kur.ps1` ile yapıldı, denetim `tools/windows-guvenlik-denetimi.ps1` ile
**yönetici olmayan** bir oturumdan çalıştırıldı — tehdit modeli tam olarak bu: makinede
sıradan bir kullanıcı hesabı olan biri.

| # | Bulgu | Ölçülen |
|---|---|---|
| 1 | Yönetici parolası `HKLM\...\Session Manager\Environment` altında düz metin | **`BUILTIN\Users` okuyabiliyor.** Hesap oluşturulduktan sonra da silinmiyor |
| 2 | Genel adres girilince Kestrel `0.0.0.0`'a bağlanıyor | Doğrulandı; güvenlik duvarı kuralının kaynak kısıtı **`Any`** |
| 2b | Sahte `X-Forwarded-For` ile hız sınırı | **12 denemede tek bir `429` yok** — hepsi `401`. Başlık her istekte değiştiği için limitleyici her seferinde yeni bir bölüm açıyor |
| 3 | `ProgramData\SunucuIzleme`, `keys\`, `mssqlrealtime.db` | Üçünde de **`BUILTIN\Users = ReadAndExecute`**. Yani sıradan bir kullanıcı veri koruma anahtar halkasını okuyup kayıtlı SQL parolalarını çözebilir |
| 4 | Servis hesabı | **LocalSystem** |
| 5 | Loglarda sır | **Ölçülemedi** — `logs\` boştu, 0 dosya tarandı. "Temiz" değil, "bilinmiyor" |
| 6 | Güvenlik başlıkları | Dördü de yok: `X-Content-Type-Options`, `X-Frame-Options`, `Content-Security-Policy`, `Referrer-Policy` |
| 7 | LAN'dan erişim | `http://172.24.80.1:5199` **düz HTTP** ile cevap veriyor |

En ağırı 3 numara: şifreleme var ama anahtarı okuyabilen için şifreleme yok.

**Yan bulgu — x64 paketi ARM64 Windows'ta çalışıyor.** Self-contained win-x64 yayın,
Windows 11 ARM64 üzerinde emülasyonla sorunsuz kuruldu ve çalıştı (servis ayakta, sağlık
ucu cevap verdi). ARM makineler için ayrı paket gerekmiyor.

**Ölçüm sırasında karışan şey — iki kurulum yarıştı.** Aynı dakikalarda kullanıcı
`setup.exe` sihirbazını dolduruyordu; sihirbaz benim betikle yaptığım kurulumun **üzerine
yazdı** (parola ve `ASPNETCORE_URLS` değişti, `Cors__AllowedOrigins__0` benimki kaldı).
Sonuç: registry'de tutarsız bir karışım. Kodda hata yok — temiz bir koşuda `0.0.0.0`,
19 karakterlik parola ve `0.0.0.0` dinleyen soket doğrulandı. Ders: **iki kurulum yolu
aynı ortam değişkenlerini yazıyor ve biri diğerini sessizce eziyor**; sıra kimdeyse o
kazanıyor.

## 2026-08-06 02:20 — setup.exe macOS'ta derleniyor, belgeler tersini söylüyordu

`amake/innosetup` konteyneri (Wine + Inno Setup 6) `SunucuIzleme-Setup-0.12.0.exe`'yi
macOS/arm64'te **111 saniyede** derledi, çıktı 39 MB. Windows makine gerekmiyor.

🔴 **Ölçülmüş ders belgeye yazılmadığı için kayboldu.** Bu yöntem 2026-08-06 00:38'de
v0.9.1 ile bir kez kullanılmış (commit `6c570c1`, "build setup.exe without Windows"), ama
komut hiçbir betiğe girmemiş; `CLAUDE.md`, `setup/README.md` ve `docs/03-kurulum.md`
"Inno Setup yalnız Windows'ta çalışır" demeye devam etmiş. Sonraki oturumda v0.12.0
release'i **setup.exe'siz** yayınlandı, çünkü belge okunup doğru kabul edildi. Yalnız
commit mesajında yaşayan bilgi, yaşamıyor demektir.

Düzeltildi: `tools/setup-derle.sh` (sürüm eşitliğini de kontrol eder, `windows-publish/`
yoksa uyarır), üç belge güncellendi.

## 2026-08-06 02:00 — sürüm iki dosyada birbirinden kaymıştı

`Directory.Build.props` 0.12.0 iken `setup/SunucuIzleme.iss` 0.11.0'da kalmıştı. Fark
sessiz: setup derlenir, çalışır, ama 0.12.0 ikilileri `SunucuIzleme-Setup-0.11.0.exe`
adıyla ve Program Ekle/Kaldır'da 0.11.0 olarak görünür — "müşteride hangi sürüm var?"
sorusu cevapsız kalır. `tools/setup-derle.sh` artık derlemeden **önce** ikisini
karşılaştırıp eşit değilse durur.

## Doğrulanmayı bekleyenler

| Konu | Neden ölçülemedi |
|---|---|
| Windows Server 2019 üzerinde CPU ring buffer değeri | Elde Windows sunucu yok |
| `sys.dm_server_services` tam çıktısı | Linux konteynerde yalnızca SQL Agent listelendi |
| iOS/Android'de bildirim davranışı | Xcode iOS platform bileşeni kurulu değil (~7 GB) |
| SMTP kanalı canlı gönderim | Test edilecek mail sunucusu yok |
| Yüksek sunucu sayısında poller yükü | Tek sunucuyla ölçüldü |
