# Ölçülen bulgular

> Buradaki her satır **çalıştırılarak** bulundu, belgeden okunarak değil. Tarih ve saat
> taşır, çünkü aynı gün içinde davranış değişebilir.

## 2026-08-10 00:30 — "sunucular kayboldu": kaybolmamışlardı

Kullanıcı 0.19.x kurulumundan sonra *"sunucular kayboldu"* dedi. Kurulum ve güncelleme
şüpheliydi. Sunucu günlüğü üçünü de aklıyor:

```
15:47:37  POST   /api/modules/mssql/servers          201   ← sunucu 1 yaratildi
15:48:30  POST   /api/modules/mssql/servers          201   ← sunucu 2 yaratildi
15:51:59  DELETE /api/modules/mssql/servers/019fe690 204   ← sunucu 1 SILINDI
15:52:22  DELETE /api/modules/mssql/servers/019fe691-4eec 204  ← sunucu 2 SILINDI
… sonrasinda hic 201 yok; hic "Started polling" yok …
10.08 00:04  guncelleme (8 saat SONRA)
```

Yani sunucular kurulumdan **sekiz saat önce** elle silinmişti. Veritabanı dosyası da
değişmemişti (oluşturma zamanı 09.08 15:45, üzerine yazılmamış) ve makinede tek
veritabanı vardı.

**Asıl bulgu, aradaki 404'ler:**

```
15:52:08  DELETE …019fe691-0155…  404
15:52:44  DELETE …019fe691-0155…  404
15:52:48  DELETE …019fe691-0155…  404
15:54:41  POST   …019fe691-0155…/kill  400
```

Bu kimlik bu hub'da **hiç yaratılmadı**. Yani ekranda, sunucuda karşılığı olmayan bir
kart vardı ve silinmiyordu — çünkü `store.remove()` 404'te hata fırlatıp yerel temizliği
atlıyordu. Kullanıcı üç kez denedi, kart durdu.

Kullanıcı telefonda **başka bir panel** kayıtlı olduğunu doğruladı. 0.18.6 öncesi
sürümde panel değiştirince önceki panelin verisi ekranda kalıyordu (aynı gün ölçülüp
düzeltilen hata), yani "güncellemeden önce sunucular vardı" gözlemi de bununla
açıklanıyor: görülen şey öteki panelin sunucularıydı.

**Ders:** "kayboldu" denen veri üç ayrı yerde aranmadan önce günlüğe bakılmalıydı —
ama günlük `C:\Windows\System32\data\logs` altındaydı (göreli yol), yani "loglara bak"
adımı boş klasör gösteriyordu. Üç kusur da v0.19.2'de düzeltildi.

## 2026-08-09 17:50 — panel değiştirince eski müşterinin hub'ında kalınıyordu

Kullanıcı bildirdi: "canlı yazmasına rağmen sunucu değiştirdim, header'daki değişmiyor."

Ölçüm düzeneği: yerelde gerçek API (5299, gerçek giriş ve gerçek SignalR bağlantısı),
ön yüz `vite dev` (5199), ikinci panel olarak var olmayan bir adres (5399). Panel
değişimi **gerçek akışla** yapıldı — header'daki 🔀 düğmesi, tam sayfa yenilemesi yok.
WebSocket'ler kimlik bazında izlendi.

```
                          ESKI kod            YENI kod
header                    bedir (bayat)       acme
baglanti gostergesi       canli  (yalan)      bagli degil
eski hub soketi (5299)    ACIK                kapali
yeni panele istek         hic gitmedi         gidiyor
```

Üç kusur üst üste binmişti:

1. **Soket eski hub'da kalıyor.** `realtime.start()` bağlantı ayaktayken erken dönüyor
   (`state !== Disconnected`), hub adresi de bağlantı kurulurken sabitleniyor. Panel
   değişiminde `stop()` çağıran kimse yoktu — `start()`/`stop()` yalnız kök yerleşimin
   `onMount`'unda ve çıkışta çağrılıyordu.
2. **Header bayatlıyor.** `const activeServer = $derived(getActiveServer())`;
   `getActiveServer()` `localStorage` okur ve Svelte bunu izleyemez. Bağımlılığı olmayan
   bir `$derived` bir kez hesaplanır. İlk denemelerde güncellenmiş *görünmesi* zamanlama
   tesadüfüydü: `switchPanel()` eklenip `goto('/')` geciktiğinde header "bedir"de kaldı ve
   kullanıcının tarifi birebir üretildi.
3. **Store'lar taşınıyor.** `mssql` ve `http` store'ları eski panelin sunucularını,
   snapshot'larını ve geçmişini tutmaya devam ediyordu.

Bir izleme ürününde sonucu şu: A müşterisinin sayıları B müşterisinin adı altında
görünüyor ve gösterge "canlı" diyor. Sessiz yanlış veri, sessiz veri yokluğundan beterdir.

Düzeltme: `realtime.switchPanel()`, `app/src/lib/api/panel.svelte.ts` (panel reaktif
durum), her iki store'da `reset()`; üçü de `/giris` içindeki tek bir `enterActivePanel()`
üzerinden çağrılıyor.

## 2026-08-09 17:03 — dokunulmamış form kendine taslak yazıyordu

İkinci sunucunun ayar ekranına girildiğinde *"Yarım kalan form geri yüklendi"* uyarısı
çıkıyordu; oysa o formda hiçbir şey yazılmamıştı.

Sebep, taslağı yazan efektin koşulsuz olması:

```
app/src/lib/modules/mssql/MssqlServerForm.svelte
    $effect(() => {
        if (!loaded) return;
        sessionStorage.setItem(draftKey, ...);   ← formu açmak yetiyor
    });
```

Ölçüm gerçek tarayıcıda yapıldı — Playwright (chromium) + `vite dev`, oturum depolaması
ve uyarı metni doğrudan sayfadan okundu. Aynı senaryo iki kodla koşuldu:

```
adim                              ESKI kod                         YENI kod
1. forma ilk giris, yazi yok      ["mr.draft.mssql.server.yeni"]   []
   uyari                          false                            false
2. cikip geri gelindi             ["mr.draft.mssql.server.yeni"]   []
   uyari                          TRUE  ← hata                     false
3. ada bir sey yazildi            taslak var                       taslak var
4. yazdiktan sonra geri gelindi   uyari TRUE + deger geldi         uyari TRUE + deger geldi
```

Yani hata da düzeltme de ölçülerek gösterildi, ve kural 9'un asıl işlevi (yazılanın
kaybolmaması) dördüncü adımda hâlâ çalışıyor.

İkinci, daha sinsi sonuç: bayat taslak sunucudan yeni yüklenen profilin üstüne
yazılıyordu. Profil başka bir cihazdan değiştirilmişse ekranda eski değerler kalıyordu
ve bunu gösteren hiçbir işaret yoktu.

`HttpTargetForm` aynı kusuru taşıyordu, o da düzeltildi.

> Not: ön yüzde otomatik test altyapısı **yok**. Bu ölçüm elle kurulan geçici bir
> düzenekle yapıldı (`playwright` depoya eklenmedi), yani bu davranışı koruyan kalıcı
> bir test yok. `npm run check` yalnız tipleri görür.

## 2026-08-09 03:1x — bir DMV join'i bütün sekmeyi çizdirmiyordu

Yeni kurulan sunucuda iki instance: Express'te *Oturumlar* çalışıyor, SQL Server 2019
Standard'da liste **boş**. Sekme başlığı `Oturumlar (254)` diyor, yani veri gelmiş.
Ekranda duran şey bir önceki sekmenin kutucuklarıydı — `{#if tab === 'ozet'}` bloğu.
Kutucukların *Oturumlar* seçiliyken görünmesi tek bir şey anlatır: `oturumlar` dalı
render sırasında hata atıyor ve Svelte önceki dalın DOM'unu bırakıyor.

Ölçüm, sunucuya erişmeden yapıldı — kaynak okunarak:

```
app/src/lib/modules/mssql/MssqlTarget.svelte:978
    {#each filteredSessions as x (x.sessionId)}      ← anahtarlı

node_modules/svelte/src/internal/client/dom/blocks/each.js:350
    if (length > keys.size) {
        if (DEV) { validate_each_keys(array, get_key); }
        else     { e.each_key_duplicate('', '', ''); }   ← üretimde de throw
    }
```

Denetim **koşulsuz**: mükerrer anahtar hem geliştirme hem üretim derlemesinde
`throw` ediyor; `DEV` dalı yalnızca hata metnini zenginleştiriyor. Yani bu, geliştirici
konsolunda kalan bir uyarı değil, müşterinin ekranında sekmeyi öldüren bir hata.

Kaynağı `SessionsProbe`'daki `LEFT JOIN sys.dm_exec_connections`. O görünüm **bağlantı**
başına satır tutar; MARS açık bir oturum her aktif batch için alt bağlantı açar ve
oturum N satıra çoğalır. Express'te görülmemesinin sebebi istemcilerinin MARS
kullanmaması.

Aynı sınıf `BlockingProbe`'da da vardı. `RequestsProbe`'da veri doğru (MARS'ta oturum
başına birden çok istek olabilir), anahtar yanlıştı — `request_id` eklendi.

Guard testi eski sorguyla koşuldu:

```
NoProbeJoinsConnectionsDirectly(SessionsProbe) [FAIL]
SessionsQueryReadsConnectionsThroughTopOne     [FAIL]
```

Düzeltilmiş hâlde 80/80 geçiyor, `npm run check` 0 hata.

### 16:2x — aynı kusurun ikincisi, ilk düzeltme gözden geçirilirken

İlk düzeltme kontrol edilirken *Bloke* sekmesinde aynı sınıftan bir kusur daha çıktı:

```
app/src/lib/modules/mssql/MssqlTarget.svelte:1029
    {#each s.blocking as b (b.blockedSessionId)}     ← tek başına yeterli değil
```

`BlockingProbe`'un `blocker_c` join'i düzeltilmişti ama **bloke edilen** taraf
gözden kaçmıştı: `sys.dm_exec_requests` **istek** başına satır tutar. MARS'lı bir
oturumun iki isteği aynı anda bloke olursa iki kenar aynı `blockedSessionId` ile
gelir ve sekme yine `each_key_duplicate` ile çöker. `BlockedRequestId` eklendi,
anahtar `blockedSessionId:blockedRequestId` oldu.

Guard testi ölçülerek doğrulandı: `r.request_id` satırı sorgudan çıkarılınca

```
BlockingEdgesCarryBlockedRequestIdSoRowsStayUnique [FAIL]
```

geri konunca 81/81 geçiyor. `dotnet build` 0 hata, `npm run check` 0 hata.

Taranan diğer anahtarlı `{#each}` blokları (`app/src` genelinde 30 blok) temiz:
`extraHolidays` eklemede zaten tekrar denetimi yapıyor, kalanlar sunucudan tekil
gelen alanlarla (`serverId`, `targetId`, `name`, `waitType`, `key`) anahtarlanmış.

### 16:41 — teori gerçek bir SQL Server'da doğrulandı

Sorgular ilk kez **çalıştırıldı**. Ortam: yereldeki `kurumsal_sql` konteyneri,
Microsoft SQL Server 2022 (RTM-CU24-GDR). Müşteri instance'ı değil — mekanizmayı
doğrulamak için yeterli, çünkü MARS orada da açık çıktı.

`sys.dm_exec_connections` gerçekten oturum başına birden çok satır tutuyor:

```
session_id  baglanti
51..55, 61..63   2
59, 60           3
```

Aynı anda, aynı sunucuda, iki sorgunun karşılaştırması:

```
ESKI (LEFT JOIN dm_exec_connections)   satir = 24   oturum = 12   ← 12 oturum 24 satıra çıkıyor
YENI (OUTER APPLY … TOP 1)             satir = 12   oturum = 12
```

Yani hata tahmin değil: **eski sorgu 12 oturumu 24 satıra çoğaltıyor**, mükerrer
`SessionId` gövdeye giriyor ve ön yüz sekmeyi çizemiyor. Yeni sorgu oturum başına
tam bir satır.

Üç probe sorgusu kaynaktan aynen çıkarılıp koşuldu; tekillik kontrolleri boş döndü:

```
SessionsProbe   mukerrer SessionId                          → 0 satır
RequestsProbe   mukerrer (SessionId, RequestId)             → 0 satır
BlockingProbe   mukerrer (BlockedSessionId, BlockedRequestId) → 0 satır
```

`OUTER APPLY`'lı sorguların **sözdizimi de ilk kez burada sınandı** — üçü de hatasız
çalıştı. Daha önce yalnız `dotnet build` yeşildi, ki bu bir T-SQL metninin geçerli
olduğunu göstermez.

**Müşteri instance'ında hâlâ koşulmadı** (Standard, 254 oturumlu olan). Aynı dosya
orada da koşulabilir:

```sql
SELECT session_id, COUNT(*) AS baglanti
FROM sys.dm_exec_connections
GROUP BY session_id HAVING COUNT(*) > 1;
```

## 2026-08-08 03:05 — sürüm üç yerde duruyor, ikisini bilmek yetmiyor

`Directory.Build.props` ve `setup/SunucuIzleme.iss` 0.18.4'e çekildikten sonra
`./tools/apk-derle.sh` çalıştırıldı ve çıktı:

```
✅ Hazir: setup/output/SunucuIzleme-0.18.3.apk  ( 12M)
```

Betik sürümü `app/src-tauri/tauri.conf.json` içinden okuyor
(`tools/apk-derle.sh:25`). Hata yok, uyarı yok — **yeni kod, eski etiketle** paketlendi.
APK 0.18.4 içeriği taşıyor ama adı ve Android'in gördüğü sürüm 0.18.3; telefondaki
"güncelleme var mı" karşılaştırması bunu yükseltme saymaz.

`tauri.conf.json` düzeltilip APK yeniden derlendi. `CLAUDE.md` release tarifi üç sürüm
dosyasını da sayacak şekilde güncellendi; APK ve win-x64 zip adımları da eksikti.

## 2026-08-08 02:35 — sessiz saatler yazılmış ama hiç devreye girmemiş

Gece 02:35'te (Cumartesi) Telegram alarmı **sesli** geldi. Sebep saat penceresi değil:

```
sqlite3 src/MssqlRealtime.Api/data/mssqlrealtime.db \
  "select ChannelId, Key from NotificationChannelSettings"
webhook|url   webhook|secret   telegram|botToken   telegram|chatId
```

`__zamanlama` satırı **yok**. `GetScheduleAsync` boş sonuçta `new NotificationSchedule()`
döndürüyor, o kayıtta `Enabled` varsayılanı `false`'tı, `IsQuietAt` da ilk satırda
`false` dönüyordu — 08:30–18:00 penceresi hiç değerlendirilmiyordu. Cumartesi olması bile
işe yaramıyordu; kod oraya kadar gitmiyor.

Ders: **kullanıcının açması gereken bir varsayılan, açılmayacak varsayılandır.** Ayar
yazıldı, belgelendi, arayüzü çizildi ve üç gün boyunca hiçbir mesajı sessizleştirmedi.
Sessizlik alarmı kesmediği (yalnız `disable_notification` gönderdiği) için açık gelmesinin
bir bedeli de yoktu. v0.18.4'te varsayılan `true` yapıldı.

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

## 2026-08-22 16:52 — varsayılan değişikliği tek başına kimseyi kurtarmıyor

Oturum sayısı alarmının varsayılanı 200'den 500'e çekildi. `ServerProfile` içindeki
`SessionCountAlertThreshold = 500` bir **C# property başlangıç değeri**; yalnız `new`
ile oluşturulan nesneye uygulanır. Veritabanında zaten duran satırlara hiçbir etkisi
yok — yükseltmeden sonra müşteri "sınır 500" sanır, alarm 200'de çalışmaya devam eder.
Hata mesajı yok, log satırı yok; yalnız beklenmeyen bir bildirim.

Bu yüzden şema değiştirmeyen bir veri migration'ı eklendi
(`20260822135136_RaiseSessionCountThresholdDefault`). Ölçüm düzeneği: boş bir SQLite'a
bir önceki migration'a (`MetricsAndAlertContext`) kadar gidildi, elle üç satır yazıldı,
sonra `dotnet ef database update` ile yeni migration uygulandı.

| Sunucu | Önce | Sonra | Beklenen |
|---|---|---|---|
| Eski-200 | 200 | **500** | taşınmalı ✅ |
| Elle-350 | 350 | 350 | dokunulmamalı ✅ |
| Kapali-NULL | NULL | NULL | dokunulmamalı ✅ |

`WHERE SessionCountAlertThreshold = 200` koşulu kasıtlı: elle girilmiş bir değeri
"düzeltmek" kullanıcının kararını çöpe atmak olurdu. `NULL` zaten "bu kural kapalı"
demek, açılmamalı.

Temiz kurulum yolu ayrıca koşuldu (16:53): sıfırdan bir veritabanında tüm migration'lar
uygulandı, son migration'ın `UPDATE`'i sıfır satır etkiledi, `__EFMigrationsHistory`
son kayıt olarak yeni migration'ı gösterdi.

⚠️ **500 ölçülmüş bir sayı değil.** Havuz aritmetiğinden türetilmiş bir tahmin: üç
uygulama sunucusu × varsayılan `Max Pool Size` 100 = 300 boşta oturum. Kural
`is_user_process = 1` sayıyor ve `status` filtresi yok, yani `sleeping` havuz oturumları
da sayıya giriyor. Gerçek payı görmek için müşterideki dağılım ölçülmeli:

```sql
SELECT status, COUNT(*) FROM sys.dm_exec_sessions
WHERE is_user_process = 1 GROUP BY status;
```

## 2026-08-22 20:47 — `sys.dm_os_sys_info`'da `active_workers_count` yok

Worker doluluğu kuralı yazılırken yaygın olarak paylaşılan şu sorgu denendi:

```sql
SELECT max_workers_count, active_workers_count FROM sys.dm_os_sys_info;   -- ÇALIŞMAZ
```

Azure SQL Edge 15.0.2000.1574 (ARM64) konteynerinde ölçüldü; bu görünümde `%worker%`
kalıbına uyan **tek** sütun var:

```
name
----
max_workers_count
```

`active_workers_count` `sys.dm_os_schedulers`'ta. Prob ikisini kasıtlı olarak karıştırıyor:

```sql
SELECT COUNT(*)                             AS SchedulerCount,
       ISNULL(SUM(runnable_tasks_count), 0) AS RunnableTasks,
       ISNULL(SUM(active_workers_count), 0) AS ActiveWorkers,
       (SELECT max_workers_count FROM sys.dm_os_sys_info) AS MaxWorkers
FROM sys.dm_os_schedulers
WHERE status = 'VISIBLE ONLINE';
```

Ölçülen çıktı (boştaki konteyner):

```
SchedulerCount RunnableTasks ActiveWorkers MaxWorkers
4              0             24            256          → %9
```

`WHERE status = 'VISIBLE ONLINE'` payı kullanıcı zamanlayıcılarıyla sınırlar; `MaxWorkers`
ise instance geneli ve gizli zamanlayıcıları (DAC, resource monitor) da kapsar. Oran bu
yüzden birkaç worker kadar **düşük** çıkar — yanılma yönü güvenli tarafta.

Değiştirilmiş prob sorgusunun tamamı aynı konteynerde koşuldu, beş sonuç kümesi de döndü.
⚠️ `sqlcmd` ile denerken `SET QUOTED_IDENTIFIER ON` gerekiyor: ring buffer sorgusu XML
metodu kullanıyor ve `sqlcmd` bu ayarı varsayılan olarak **kapalı** açar (Msg 1934). Bu bir
prob hatası değil — `Microsoft.Data.SqlClient` ayarı zaten açık gönderir; yalnız elle test
ederken tuzak.

⬜ Ölçülmeyen: %80 eşiğinin gerçek bir üretim sunucusunda isabetli olup olmadığı. Konteynerde
worker havuzu hiç zorlanmadı; THREADPOOL beklemesi üretilmedi.

## 2026-08-22 22:2x — `sys.dm_exec_sql_text` içine `CASE` koymak çalışıyor

Oturumlara son çalıştırdıkları ifadeyi eklerken sorun maliyetti: `sys.dm_exec_sql_text`
satır başına bir plan-cache aramasıdır ve havuz kullanan bir uygulamada oturumların çoğu
boştadır. Hepsi için her turda metin çekmek, kimsenin okumayacağı metnin bedelini
müşterinin sunucusuna ödetmek olurdu.

Denenen çözüm — fonksiyona `NULL` geçirmek:

```sql
OUTER APPLY sys.dm_exec_sql_text(
    CASE WHEN s.status <> 'sleeping' OR s.open_transaction_count > 0
         THEN c.SqlHandle END) t
```

Azure SQL Edge 15.0.2000.1574 (ARM64) üzerinde ölçüldü. `sqlcmd` ile biri açık
transaction'lı, biri transaction'sız iki oturum boşta bırakıldı:

| SPID | Durum | Açık işlem | SqlText |
|---|---|---|---|
| 51 | `sleeping` | 1 | `BEGIN TRAN; UPDATE dbo.olcum SET a = 2;` ✅ |
| 52 | `sleeping` | 0 | *(metin çekilmedi)* ✅ |
| 53 | `running` | 0 | `SELECT s.session_id AS SPID, …` ✅ |

Üç davranış da beklendiği gibi: `CASE` kabul ediliyor, `NULL` verilince fonksiyon hiç
satır döndürmüyor, ve **asıl kazanılan durum** — açık transaction'la uyuyan blocker —
metnini veriyor. O oturumun `sys.dm_exec_requests`'te satırı yoktur; ne yaptığı başka
hiçbir yerden görünmüyordu.

`most_recent_sql_handle`, istemci IP'si için zaten var olan `dm_exec_connections`
`OUTER APPLY`'ına eklendi — ek join yok.

⚠️ Ölçülmeyen: gerçek bir müşteri sunucusunda (yüzlerce oturum) bu `CASE`'in poll süresine
etkisi. Konteynerde üç oturum vardı; ölçüm maliyeti hakkında bir şey söylemiyor.

## 2026-08-22 22:2x — alarm bağlamı 400 karakterde kırpılıyor

`EfAlertStore` alarm bağlamını veritabanına yazarken 400 karakterde kesiyor. Bu, SQL
metnini bağlama koyarken sessiz bir tuzak: 4000 karakterlik bir batch yazılsaydı kimlik
satırı ("SPID 71 · Rapor · sa · APP01") kırpma sırasında değil, **metnin sonunda** kalırdı
ve okunan alarmda kimin yaptığı görünmezdi.

Bu yüzden ifade alarm bağlamında **240 karakterde** kesiliyor ve tek satıra katlanıyor.
Tam metin (4000 karakter) canlı ekranda duruyor; alarm bir özet taşır.

## Doğrulanmayı bekleyenler

| Konu | Neden ölçülemedi |
|---|---|
| Windows Server 2019 üzerinde CPU ring buffer değeri | Elde Windows sunucu yok |
| `sys.dm_server_services` tam çıktısı | Linux konteynerde yalnızca SQL Agent listelendi |
| iOS/Android'de bildirim davranışı | Xcode iOS platform bileşeni kurulu değil (~7 GB) |
| SMTP kanalı canlı gönderim | Test edilecek mail sunucusu yok |
| Yüksek sunucu sayısında poller yükü | Tek sunucuyla ölçüldü |
| Oturum eşiği 500 gerçekten yeterli mi | Müşteride `sleeping`/aktif oturum dağılımı ölçülmedi |
| Worker doluluğu %80 eşiği isabetli mi | THREADPOOL doygunluğu üretilemedi; konteynerde havuz %9'da kaldı |
| SQL metni çekmenin yüzlerce oturumlu sunucuda poll maliyeti | Konteynerde üç oturum vardı |
| İşlemci sırası için sağlıklı bir varsayılan | Çekirdek sayısına bağlı; ölçüm yapılmadığı için kural kapalı bırakıldı |
| Veri migration'ının canlı yükseltmede davranışı | Yalnız boş SQLite düzeneğinde koşuldu, gerçek müşteri veritabanında değil |
