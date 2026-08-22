# Değişiklik günlüğü

Biçim: [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/) · Sürümleme: [SemVer](https://semver.org/lang/tr/)

## [0.23.0] — 2026-08-23

Açık kalan beş soru ölçüldü ve üçü koda dönüştü. Ölçüm ortamı: Azure SQL Edge
15.0.2000.1574 (ARM64 konteyner), 4 zamanlayıcı, `max_workers_count` 256. Emülasyon
altında; **mutlak süreler gerçek sunucuya taşınamaz**, davranışlar taşınır. Ayrıntı:
`docs/05-olculen-bulgular.md`.

### Düzeltilen — tek olay üç bildirim üretiyordu

45 saniyedir bloke olan **tek** bir istek, kural motorunda üç kuralı birden ihlalde
gösteriyordu: `blocking`, `long-running`, `blocking-duration`. Sebep, bloke bir isteğin
aynı zamanda çalışan bir istek olması — `total_elapsed_time` beklerken de işler.

Kilit süresi kuralı **açıkken** uzun sorgu kuralı artık bloke istekleri kendi listesine
almıyor. **Kapalıyken** alıyor: o zaman süreyi izleyen başka kimse yok, gizlemek olayı
kaybetmek olurdu. Geriye kalan iki bildirim tekrar değil kademe: önce "bloke var"
(~15 sn), sonra "kilit uzadı" (30 sn).

### Eklenen — bağlantı kurulamadığında panelin ayakta olup olmadığı da söyleniyor

Dün bir müşteride ters vekil `/hubs` yolunu panele iletmiyordu. `/api/*` çalıştığı için
panel açılıyor, giriş yapılıyor, araç listesi geliyordu; yalnız canlı akış gelmiyordu.
Tarayıcı bunu ayırt edilemez bir `TypeError: Failed to fetch` diye bildirir.

Artık bağlantı kurulamadığında uygulama `/api/health`'i de yokluyor. Panel cevap veriyorsa
şerit şunu ekliyor: *"Panelin kendisi ayakta. Sorun /hubs yolunun panele iletilmemesinde:
ters vekil sunucuda /hubs için ayrı bir kural varsa kaldırın, Websockets desteğini açın."*
Panel de cevap vermiyorsa hiçbir şey uydurmuyor.

### Eklenen — işlemci sırası eşiği için ölçülmüş öneri

Kural varsayılan kapalı kalmaya devam ediyor (sağlıklı değer çekirdek sayısına bağlı), ama
form artık o sunucunun **ölçülen zamanlayıcı sayısını** gösterip iki katını öneriyor.
Ölçüm: 80 eşzamanlı iş çalışırken runnable = 6, zamanlayıcı = 4 → zamanlayıcı başına ~1,5.

### 🔴 Düzeltilen bilgi — SQL metni bildirim kanallarına gitmiyor

v0.22.0'ın notunda "SQL metni artık Telegram/e-posta/webhook bildirimlerine de giriyor"
yazıyordu. **Yanlıştı ve doğrulamadan yazılmıştı.** Bildirim gövdesi `Alert.Message`'tır;
üç kanalın hiçbiri `Context` alanını okumaz. SQL metni paneli terk etmiyor — yerel alarm
geçmişinde ve panelin arayüzünde duruyor, ikisi de kimlik doğrulamasının arkasında. Kanal
bazında maskeleme anahtarı gerekmiyor; v0.22.0 notu, `docs/04` ve GitHub sürüm notu
düzeltildi.

### Ölçülen — worker doluluğu doğru şeyi izliyor

| Durum | Oturum | Çalışan iş | Aktif worker | Doluluk |
|---|---|---|---|---|
| Boşta | 151 | 0 | 24 | %9 |
| 80 eşzamanlı iş | 231 | 80 | 109 | %43 |

151 **boşta** oturum iğneyi hiç oynatmadı; 80 eşzamanlı iş worker sayısını 24'ten 109'a
çıkardı. Metrik bağlantıyı değil eşzamanlı işi izliyor — oturum sayısı kuralının kanadığı
yerde bu kural sağlam. ⚠️ %80'in doğru eşik olduğu hâlâ ölçülmedi; THREADPOOL doygunluğu
üretilemedi.

### Ölçülen — SQL metni korumasının gerçek kazancı

| Oturum | Korumalı | Korumasız | Fark |
|---|---|---|---|
| 60 | 7,65 ms | 7,10 ms | yok |
| 151 | 46,95 ms | 51,70 ms | %9 |

Koruma kalıyor ama gerekçesi düzeltildi: "maliyet çok yüksek" değil, "kazanç %9 ve
büyüyor". Asıl dikkat edilecek olan sorgunun kendisi — 60 → 151 oturumda 7,65 ms'den
46,95 ms'ye çıktı, doğrusaldan hızlı büyüyor. 500 oturumda ne olacağı ölçülmedi.

Ölçülen: `dotnet build` 0 hata/0 uyarı, `dotnet test` **113** test geçti (3 yeni),
`npm run check` 0 hata, `npm test` **18** test geçti (2 yeni).

## [0.22.1] — 2026-08-22

### Düzeltilen — telefonda bir panel sonsuza kadar "bağlı değil" diyordu

Bildirildi 2026-08-22: telefonda iki panel kayıtlı, birinde her şey canlı, diğerinde
"bağlı değil" — ama sunucunun kendi tarayıcısında iki panel de sorunsuz. Uygulama neden
bağlanamadığını **biliyordu ve söylemiyordu**.

İki ayrı arıza çıktı:

**1. Süresi dolmuş oturum sonsuza kadar yeniden deneniyordu.** `getAccessToken()` refresh
token bittiğinde `null` döner ve saklanan token'ları siler. Hub'ın token fabrikası bu
`null`'ı `?? ''` ile **boş dizeye** çeviriyor, bağlantı yine kuruluyor, hub 401 veriyordu.
Sonuç: 30 saniyede bir, sonsuza kadar, aynı imkânsız deneme. Panel başına ayrı oturum
tutulduğu için bu yalnız **bir** paneli vurur — en uzun süre önce giriş yapılanı. Bildirilen
tablonun şekli tam olarak bu.

Artık token yoksa bağlantı hiç denenmiyor; durum "bu panelin oturumu sona ermiş" olarak
işaretleniyor ve ekranda **Giriş yap** düğmesi çıkıyor.

**2. Bağlantı hatasının sebebi hiçbir yerde gösterilmiyordu.** `realtime.lastError` doluyor
ama arayüzde tek bir kullanımı yoktu; kullanıcı 401 mi, CORS mu, ad mı çözülmedi, WebSocket
mi kapalı ayırt edemiyordu. Başlığın altında bağlantı kesildiğinde bir şerit çıkıyor: panel
adresi, sebebin kendisi, kaçıncı deneme olduğu ve **Yeniden dene** düğmesi. Yalnız
`disconnected` durumunda; kısa yeniden bağlanmalarda yanıp sönmez.

Bu ürünün 5. kuralı "sessizlik ≠ sağlık" diyor. Sebebi bilip göstermemek o kuralın ihlaliydi.

### Ölçülen — testin gerçekten koruduğu (2026-08-22 23:42)

`app/src/lib/api/realtime.svelte.test.ts` eklendi: token yokken bağlanma **denenmemeli**,
sebep söylenmeli, deneme sayacı şişmemeli. Koruma geçici olarak kaldırılıp koşuldu ve
**dördün ikisi düştü**:

```
× token yokken BAĞLANMAYI DENEMEZ                    expected "spy" not to be called
× token yokken sebebi söyler, "bağlı değil" ile yetinmez   expected false to be true
```

Koruma geri konunca 16 testin hepsi geçti.

⚠️ Not: bildirilen panelin oturumunun gerçekten dolmuş olduğu **doğrulanmadı** — belirti
birebir uyuyor ama sebebi kesinleştiren şey, artık ekranda yazacak olan hata metni. Ağ ya
da CORS kaynaklıysa 2. düzeltme bunu söyleyecek.

Ölçülen: `dotnet build` 0 hata/0 uyarı, `dotnet test` 110 test geçti, `npm run check`
0 hata, `npm test` **16** test geçti (4 yeni).

## [0.22.0] — 2026-08-22

### Eklenen — SQL metni her ölçümde ve her alarmda

"Uzun süren sorgu" alarmı SPID'i ve uygulamayı söylüyor, **hangi sorgu** olduğunu
söylemiyordu. Bildirimi okuyan kişi panele girip sorguyu aramak zorundaydı; on dakika
sonra o oturum çoğu zaman kapanmış oluyordu.

**Alarm bağlamlarına ifade eklendi.** İşlemci, bellek, SQL Server belleği, kilitlenme,
kilit süresi, uzun süren sorgu, oturum sayısı ve işlemci sırası kurallarının hepsi artık
kimliğin yanında ifadeyi de taşıyor:

```
SPID 71 · Rapor · sa · APP01 · 240 sn │ Sorgu: SELECT * FROM satis_hareket WHERE tarih > @p0
```

İfade tek satıra katlanıp **240 karakterde** kesilir. Sebep ölçülmüş: alarm bağlamı
veritabanına 400 karakterde kırpılıyor (`EfAlertStore`) ve aynı metin Telegram ile
e-postaya da gidiyor — 4000 karakterlik bir batch, *kimin* yaptığını söyleyen kimlik
satırını mesajdan tamamen dışarı iterdi. Tam metin canlı ekranda duruyor.

**Oturumlar artık son çalıştırdıkları ifadeyi taşıyor.** Şimdiye kadar `SessionInfo`'da
SQL metni hiç yoktu; oturum tablosuna "Son sorgu" sütunu eklendi (varsayılan gizli,
sütun menüsünden açılır). Asıl kazanç uyuyan blocker'da: açık transaction'la uyuyan bir
oturumun `sys.dm_exec_requests`'te satırı **yoktur**, dolayısıyla ne yaptığı başka hiçbir
yerden görünmüyordu.

### Ölçülen — metin çekme maliyeti gerçekten sınırlanıyor (2026-08-22 22:2x)

`sys.dm_exec_sql_text` satır başına bir plan-cache aramasıdır. Havuz kullanan bir
uygulamada oturumların çoğu boştadır; hepsi için her turda metin çekmek, kimsenin
okumayacağı bir metnin bedelini müşterinin sunucusuna ödetmek olurdu. Bu yüzden çağrı
bir `CASE` ile korunuyor — `NULL` verilince fonksiyon hiç satır döndürmez:

```sql
OUTER APPLY sys.dm_exec_sql_text(
    CASE WHEN s.status <> 'sleeping' OR s.open_transaction_count > 0
         THEN c.SqlHandle END) t
```

Azure SQL Edge 15.0.2000.1574 (ARM64) üzerinde üç durum da ölçüldü:

```
SPID Durum     AçıkİşlemSqlText
51   sleeping  1        BEGIN TRAN; UPDATE dbo.olcum SET a = 2;   ← uyuyan blocker, metin geldi
52   sleeping  0        (METİN ÇEKİLMEDİ)                         ← boşta, atlandı
53   running   0        SELECT s.session_id AS SPID, ...           ← çalışıyor, metin geldi
```

Bağlantı sorgusu zaten `sys.dm_exec_connections`'a `OUTER APPLY` yapıyordu (istemci IP'si
için); `most_recent_sql_handle` aynı APPLY'a eklendi — **ek join yok**.

⚠️ **Gizlilik — 2026-08-23'te düzeltildi.** Bu bölüm ilk yazıldığında "SQL metni artık
Telegram/e-posta/webhook bildirimlerine de giriyor" diyordu. **Yanlıştı.** Kod okunarak
doğrulandı: bildirim gövdesi `AlertNotification.Body` = `Alert.Message`'tır ve üç kanalın
hiçbiri `Context` alanını okumaz — `TelegramChannel` hedef adı, gövde, ölçülen/sınır ve
saati gönderir; `WebhookChannel` alan alan serileştirir ve `context` listede yoktur;
`EmailChannel` aynı şekilde. SQL metni **paneli terk etmiyor**: alarm geçmişi tablosunda
(yerel SQLite) ve panelin kendi arayüzünde duruyor, ikisi de kimlik doğrulamasının
arkasında.

Yine de bilinsin: alarm bağlamı ad-hoc sorgulardaki düz metin literalleri taşıyabilir ve
alarm geçmişi ekranını açan herkes onu görür. Kanal bazında maskeleme **gerekmiyor**;
gerekseydi eklenecekti.

Ölçülen: `dotnet build` 0 hata/0 uyarı, `dotnet test` **110** test geçti (5 yeni),
`npm run check` 0 hata, `npm test` 12 test geçti (2026-08-22 22:2x).

## [0.21.0] — 2026-08-22

### Eklenen — üç darboğaz kuralı: kilit süresi, işlemci sırası, worker doluluğu

Oturum sayısı bir darboğaz göstergesi değil; boşta duran havuz oturumlarını da sayar ve
gerçek sıkışmayı ancak dolaylı gösterir. Bu sürüm sıkışmayı **doğrudan ölçen** üç kural
ekliyor.

**Kilit süresi** (`blocking-duration`, varsayılan 30 sn) — mevcut "Kilitlenme" kuralı
*kaç* oturumun bloke olduğunu sayıyor, *ne kadar süredir* bloke olduğunu değil. Yarım
saniye bekleyen on oturum yoğun bir sunucudur; iki dakikadır bekleyen tek oturum bir
olaydır. Veri zaten toplanıyordu (`BlockingEdge.WaitTimeMs`), hiçbir kural okumuyordu —
**ek SQL maliyeti yok**. Bildirim engelleyen SPID'i, uygulamayı ve engelleyen sorgunun
metnini taşıyor.

**İşlemci sırası** (`runnable-tasks`, **varsayılan kapalı**) — `sys.dm_os_schedulers`
üzerinden CPU sırasında bekleyen görev sayısı. Ring buffer'dan gelen CPU%'in aksine
**canlı** bir değer. Veri zaten toplanıyordu (`MachineResources.RunnableTasks`), ek SQL
maliyeti yok. Varsayılan olarak kapalı, çünkü sağlıklı değer çekirdek sayısına bağlı ve
ölçülmeden konan bir sayı tam olarak 200'lük oturum sınırının hatası olurdu. Bildirim
zamanlayıcı sayısını da yazar ki eşik kalibre edilebilsin.

**Worker doluluğu** (`worker-utilization`, varsayılan %80) — havuz dolduğunda yeni
bağlantılar `THREADPOOL` beklemesine girer ve **izleme panelinin kendisi de bağlanamaz**;
bu yüzden erken uyarması gereken kural bu. Mevcut zamanlayıcı sorgusuna iki sütun eklendi,
**ek round-trip yok**.

### Ölçülen — `sys.dm_os_sys_info`'da `active_workers_count` yok (2026-08-22 20:47)

Yaygın olarak paylaşılan `SELECT max_workers_count, active_workers_count FROM
sys.dm_os_sys_info` sorgusu **çalışmaz**. Azure SQL Edge 15.0.2000.1574 (ARM64)
konteynerinde ölçüldü — bu görünümde `%worker%` kalıbına uyan tek sütun var:

```
name
----
max_workers_count
```

`active_workers_count` `sys.dm_os_schedulers`'ta yaşıyor. Prob bu yüzden ikisini
kasıtlı olarak karıştırıyor: pay `dm_os_schedulers`'tan, tavan `dm_os_sys_info`'dan.
Gizli zamanlayıcılar (DAC, resource monitor) paya girmez ama tavana dahildir, dolayısıyla
oran birkaç worker kadar **düşük** çıkar — güvenli yön.

Değiştirilmiş prob sorgusunun tamamı aynı konteynerde koşuldu, beş sonuç kümesi de döndü:

```
SchedulerCount RunnableTasks ActiveWorkers MaxWorkers
4              0             24            256          → %9 dolu
```

### Değişen — mevcut sunucularda iki kural yükseltmede açılır

`PressureAlertThresholds` migration'ı üç kolonu ekler ve mevcut satırlara kilit süresi
için 30 sn, worker doluluğu için %80 yazar. Yeni kolon var olan satıra `NULL` gelir ve
`NULL` "kural kapalı" demektir — geri doldurmasaydık, bu sürümden önce kayıtlı her sunucu
kuralları formda görür ama çalıştırmazdı. Bu, bir önceki sürümde belgelenen tuzağın
aynısı (`docs/04-kirilma-noktalari.md`).

⚠️ Sonucu açıkça yazıyorum: **yükseltmeden sonra iki kural kendiliğinden devreye girer.**
Sağlıklı bir sunucuda ikisi de sessizdir — 30 saniyelik bir kilit ve %80 dolu bir worker
havuzu hava durumu değil, olaydır. İşlemci sırası bilerek `NULL` bırakıldı.

Ölçülen: `dotnet build` 0 hata/0 uyarı, `dotnet test` **105** test geçti (8 yeni),
`npm run check` 0 hata, `npm test` 12 test geçti (2026-08-22 20:5x).

## [0.20.1] — 2026-08-22

### Değişen — oturum sayısı alarmının varsayılan eşiği 200 → 500

Kural, açık oturum sayısını `sys.dm_exec_sessions` üzerinden `is_user_process = 1` ile
sayıyor; **status filtresi yok**, yani `sleeping` durumdaki bağlantı havuzu (connection
pool) oturumları da sayıya giriyor. Havuz kullanan üç uygulama sunucusu (`Max Pool Size`
varsayılanı 100) tek başına 300 boşta oturum demek — kural hiç iş yokken kalıcı olarak
ihlalde kalıyor ve her turda "hâlâ ihlal" raporluyor. Kalıcı açık alarm, bakılmayan
alarmdır.

200 sayısının ölçülmüş bir dayanağı yoktu; seçilmiş bir varsayılandı. 500 de ölçülmüş
değil — yalnız havuz aritmetiğine göre daha az gürültülü. Eşik sunucu başına
düzenlenebilir, alan boşaltılırsa kural tümüyle kapanır.

- `ServerProfile.SessionCountAlertThreshold` varsayılanı 500
- Yeni sunucu formunun ön dolgusu 500

### Eklenen — mevcut sunucuları da taşıyan veri migration'ı

Bir C# property başlangıç değeri yalnız **yeni oluşturulan** satıra uygulanır; kayıtlı
sunucular sessizce 200'de kalırdı — "sınırı 500 yaptık" denip alarmın 200'de çalışması
en kötü türden bir sürpriz. `RaiseSessionCountThresholdDefault` şema değiştirmez, tek
iş yapar:

```sql
UPDATE MssqlServerProfiles SET SessionCountAlertThreshold = 500
WHERE SessionCountAlertThreshold = 200;
```

Yalnız **eski varsayılanı taşıyan** satırlar güncellenir. `NULL` (kural kapalı) ve elle
girilmiş her değer olduğu gibi bırakılır — kullanıcının açık tercihi bizim değiştireceğimiz
şey değil. `Down` doğası gereği kayıplıdır: 500'ü bilerek seçmiş bir sunucu da 200'e döner,
eski değer hiçbir yerde saklanmıyor.

**Ölçüldü 2026-08-22 16:52** — boş bir SQLite'a bir önceki migration'a kadar gidilip üç
satır yazıldı, sonra yeni migration uygulandı:

```
Eski-200      200  →  500     (taşındı)
Elle-350      350  →  350     (dokunulmadı)
Kapali-NULL   NULL →  NULL    (dokunulmadı)
```

Temiz kurulum yolu da koşuldu: sıfırdan bir veritabanında tüm migration'lar sorunsuz
uygulandı, `UPDATE` sıfır satır etkiledi (2026-08-22 16:53).

Ölçülen: `dotnet build` 0 hata, `dotnet test` 97 test geçti, `npm run check` 0 hata,
`npm test` 12 test geçti (2026-08-22 16:53).

⬜ Not: oturum sayısı zaten zayıf bir darboğaz göstergesi. Daha isabetli olanlar —
en uzun blok süresi, runnable task sayısı, worker thread doluluğu — henüz kural
olarak yok; ilk ikisinin verisi `BlockingEdge.WaitTimeMs` ve `MachineResources.RunnableTasks`
içinde zaten toplanıyor ve kullanılmıyor.

## [0.20.0] — 2026-08-10

### Eklenen — ön yüzde test altyapısı (vitest)

Bu gece ön yüzde dört ayrı hata bulundu ve **hepsi geçici Playwright düzenekleriyle**
ölçüldü; hiçbiri depoda kalmadı, yani hiçbiri korunmuyordu. `npm run check` yalnız
tipleri görür — dördünü de göremezdi.

`npm test` artık store'ların kararını sınıyor: ekranda ne olmalı. İki dosya, 12 test,
1 saniye. Bağımlılıklar (HTTP ve SignalR) taklit ediliyor; ölçülen şey ağ değil, karar.

**Testlerin gerçekten koruduğu ölçüldü:** store refactor öncesi hâline (c74dd40)
döndürülüp koşuldu ve **dördü düştü**:

```
× kaydı olan ama hiç ölçülmemiş sunucu EKRANDA GÖRÜNÜR   expected [] to have a length of 1
× profili olmayan ölçüm kart üretmez                      got 1  (hayalet kart)
× tazeleme profilde karşılığı olmayan ölçümleri budar     got 2
× ad ve müşteri PROFİLDEN okunur                          expected undefined to be 'Yeni Ad'
```

Kalan üçü (404'te kartın kalkması, 404 dışındaki hatanın yutulmaması, panel değişiminde
temizlik) o commit'te zaten düzeltilmişti ve geçti — yani testler hangi düzeltmenin neyi
kapsadığını ayırt ediyor.

`CLAUDE.md`'deki değişiklik disiplinine eklendi: artık `dotnet build` + `dotnet test` +
`npm run check` + **`npm test`**.

### Değişen — liste artık izlenenlerden türüyor, ölçüm önbelleğinden değil

Bu gece üç ayrı hatayı ayrı ayrı yamadık ve üçü de aynı kökten çıktı: **ekrandaki
liste, izlenen sunucuların listesinden değil, gelen ölçümlerin önbelleğinden
çiziliyordu.**

```ts
get servers() { return [...this.snapshots.values()] }   // ölçümler
// oysa gerçek olan: this.profiles                       // izlenenler
```

Sonuçları, hepsi bu gece görüldü:

- Silinen sunucunun son ölçümü haritada kaldığı için **kart ekranda kalıyordu**; silmeye
  basınca hub `404` dönüyor, kart yine duruyordu (v0.19.2'de yamandı).
- Panel değiştirilince **önceki panelin ölçümleri** yeni panelin adı altında görünüyordu
  (v0.18.6'da yamandı).
- Ve en sinsisi: **eklenmiş ama henüz ölçülmemiş bir sunucu ekranda hiç görünmüyordu.**

Artık profil listesi tek gerçek: kayıt yoksa kart yok, ölçüm yoksa kart var ve
*"ölçüm bekleniyor"* der. Ad ve müşteri adı profilden okunur (her zaman vardır), sayılar
ölçümden (olmayabilir). Tazelemede profilde karşılığı olmayan ölçümler atılır; bilinmeyen
bir ölçüm gelirse profil listesi bir kez tazelenir. Aynı düzeltme site/API modülünde de
yapıldı.

Sıralamada ölçümü olmayan kayıt **uyarı** sayılır. Sıralayıcı eksik değerleri dibe atar;
oysa "ölçüm gelmiyor" bir izleme ürününde dibe atılacak değil, öne çıkarılacak bir
durumdur — sessizlik sağlık değildir.

> 🔴 **Ölçüldü 2026-08-10 00:50**, gerçek hub ve tarayıcıyla (Playwright + yerel API).
> Veritabanına **kapalı** bir sunucu profili yazıldı — hiç yoklanmadığı için ölçümü yok:
>
> ```
>                        ESKİ kod                       YENİ kod
> kart sayısı            0                              1
> ekranda                "Henüz izlenen sunucu yok"     Kapali Sunucu · Marmara · kapalı
> ```
>
> Yani veritabanında duran bir sunucu ekranda **hiç yoktu**; kullanıcının onu görüp
> yeniden açması ya da silmesi mümkün değildi.

## [0.19.2] — 2026-08-10

### 🔴 Düzeltilen — silinmiş bir sunucunun kartı ekrandan gitmiyordu

Müşteri makinesinde ölçüldü (2026-08-09 15:52, sunucu günlüğü): listede, sunucuda
karşılığı **olmayan** bir kart duruyordu. Silmeye basıldığında hub `404` dönüyor,
istemci hata fırlatıyor ve **yerel temizlik hiç çalışmıyordu** — kart ekranda kalıyordu.
Kullanıcı aynı kartı **üç kez** silmeye çalıştı, üçünde de `404` aldı, kart yerinde
kaldı; sonunda üstünde oturum sonlandırma bile denedi (`400`).

`404` artık **başarı** sayılıyor: istenen son durum ("bu kayıt gitsin") zaten sağlanmış
demektir, kart kaldırılır. Aynı kusur site/API modülünde de vardı, o da düzeltildi.

> Bu, "kurulumdan sonra sunucular kayboldu" diye başlayan araştırmanın gerçek
> bulgularından biri. Sunucular kaybolmamıştı: ikisi de 15:52'de **elle silinmişti**
> (günlükte iki `201` ve iki `204` var). Ekranda görünmeye devam eden şey, hub'da
> karşılığı olmayan bayat kartlardı — telefonda ise 0.18.6'da düzeltilen panel
> değiştirme hatası başka bir panelin sunucularını gösteriyordu.

### 🔴 Düzeltilen — günlükler `C:\Windows\System32` altına yazılıyordu

Bir arıza araştırılırken ortaya çıktı: uygulamanın kendi günlükleri
`C:\Windows\System32\data\logs\` altındaydı.

`appsettings.json`'daki yol **göreli** yazılmıştı (`data/logs/app-.log`) ve bir Windows
servisinin çalışma dizini `C:\Windows\System32`'dir. Servis LocalSystem olarak
çalıştığı için oraya yazabiliyordu — yani **hata da vermiyordu**, yalnızca kimsenin
bakmayacağı bir yere yazıyordu. Sonuç: bir sorun çıktığında "loglara bak" adımı boş bir
klasör gösteriyordu; `CLAUDE.md`'deki 502 kontrol listesi de öyle.

Yol artık **mutlak** ve veri klasörünün altında (`<veri>\logs\app-*.log`). Dizideki
sıraya bağlı kalmamak için dosya havuzunun yolu anahtar adıyla bulunup değiştiriliyor.

**Ölçüldü 2026-08-10 00:35:** çalışma dizini bilerek veri klasöründen farklı seçilip
servis çalıştırıldı. Günlük `<veri>/logs/app-20260810.log` olarak düştü; çalışma dizini
altında `data/logs` **hiç oluşmadı** (eski davranışta orası dolardı).

> Bu kusur bir veri kaybına yol açmadı ama bir araştırmayı saatlerce zorlaştırdı:
> "sunucular kayboldu" denen olayın cevabı en baştan günlükte duruyordu.

## [0.19.1] — 2026-08-09

### 🔴 Düzeltilen — kurulum servisi durduramıyordu, dosyalar kilitli kalıyordu

Yükseltmede kurulum şu hatayla duruyordu:

> *Var olan dosya değiştirilirken sorun çıktı: DeleteFile tamamlanamadı; kod 5.
> Erişim engellendi.* — `C:\SunucuIzleme\clrjit.dll`

Servis elle durdurulup "yeniden denensin" denince geçiyordu. **İki kusur üst üste
binmişti:**

1. `sc stop` yalnızca *durdur* kontrolünü gönderir ve hemen döner. `ewWaitUntilTerminated`
   `sc.exe`'yi bekliyordu, **servisi değil**; ardındaki `Sleep(1500)` bir tahmindi.
   Uygulama .NET genel host: süren bir SQL probu (CommandTimeout 15 sn) kapanmayı
   saniyelerce geciktirebilir.
2. Daha temeli: o kod **dosyalar kopyalandıktan sonra** çalışıyordu. Kilit, dosya ayıklama
   anında vardı — beklese bile geç kalırdı.

Artık servis `PrepareToInstall` içinde, yani **hiçbir dosyaya dokunulmadan önce**
durduruluyor ve gerçekten durana kadar (en fazla 90 sn) bekleniyor; ardından süreci de
beklenir, asıl dosya kilidini o tutar. Durum karşılaştırması `Get-Service .Status` ile
yapılıyor: `sc query` çıktısındaki durum metni yerelleştirilir (Türkçe Windows'ta
"STOPPED" yazmaz), enum ise dilden bağımsızdır. Aynı bekleme kaldırma yoluna da kondu.

**Bu, v0.19.0'daki güncelleme düğmesini de kurtarır:** sessiz kurulumda (`/VERYSILENT`)
o diyalog gösterilemez, dolayısıyla güncelleme sessizce başarısız olurdu.

### 🔴 Düzeltilen — kurulumdan sonra izlenen sunucular kaybolmuş görünüyordu

Yükseltmenin ardından *MSSQL İzleme* ekranı **"Henüz izlenen sunucu yok"** diyordu.
**Veri silinmemişti**; servis başka klasöre bakıyordu.

`IsUpgrade` veritabanını eski yerleşimde (`ProgramData\SunucuIzleme`) de arıyordu — yani
kurulum o ihtimali biliyordu — ama servise **her zaman** `{app}\data` veriliyordu. Eski
yerleşimden gelen bir kurulum bu yüzden bomboş bir veritabanı açıyordu: profiller,
alarm geçmişi ve veri koruma anahtarları eski klasörde duruyor, panel onları görmüyordu.

Artık yükseltme veri klasörünü **taşımıyor**: veritabanı neredeyse servis oraya
yönlendiriliyor.

> Ayrıca `CLAUDE.md` düzeltildi. Belge verinin `ProgramData` altında olduğunu söylüyordu,
> oysa kod 0.12.x'ten beri `{app}\data` kullanıyor. Yalnız belgeye bakıp yedek alan biri
> **boş bir klasörü** yedeklemiş olurdu.

**Ölçülemeyen:** her iki düzeltme de Windows kurulum davranışı; macOS'ta koşturulamaz.
Inno Setup derlemesi (Wine altında) sözdizimini doğrular, davranışı doğrulamaz. Gerçek
yükseltme denemesi hâlâ açık iş.

## [0.19.0] — 2026-08-09

### Eklenen — panel kendini güncelleyebiliyor (elle tetiklenerek)

Yeni sürüm yayınlandığında başlığın altında şerit çıkıyor: *"Panel v0.19.0 çalışıyor,
v0.19.1 yayınlandı"* + **Güncelle**. Düğmeye basınca panel kurulum dosyasını GitHub
sürümünden indirir, kurar ve kendini yeniden başlatır.

**Zamanlanmış güncelleme bilerek yok.** Bu bir izleme ürünü: bozuk bir sürüm servisi
düşürürse müşteri izlemesiz kalır ve **bunu kimse fark etmez**. Somut örnek elimizde —
0.18.5 öncesi sürüm MARS'lı sunucuda sekmeleri bomboş bırakıyordu; otomatik güncelleme
olsaydı o sürüm bütün müşterilere kendiliğinden inerdi. Ne zaman güncelleneceğine
operatör karar verir.

**Nasıl güvenli tutuluyor:**

- **Sağlama zorunlu.** Kurulum dosyasının sha256'sı GitHub'ın kendi verdiği özetle
  karşılaştırılır; tutmazsa dosya silinir ve kurulum hiç başlamaz. Özeti olmayan bir
  varlık da kurulmaz.
- **Sağlık kapısı.** Yükseltici "kurdum" demez: yeni sürüm `/api/health` ile cevap
  verene kadar (en fazla 180 sn) bekler.
- **Geri dönüş.** Cevap gelmezse *çalışan sürümün kendi kurulum dosyası* ile geri
  dönülür — aynı, denenmiş kurulum makinesi servisi doğru argümanlarla yeniden kurar.
  O paket bulunamıyorsa arayüz bunu **önceden** söyler (`⚠ geri dönüş paketi yok`) ve
  onay metni de uyarır.
- **Ayrık süreç.** Kurulum servisi `sc stop` + `sc delete` ile kaldırdığı için
  güncellemeyi başlatan sürecin kendisi ölüyor. Sağlık kontrolünü ve geri dönüşü
  servisten bağımsız yaşayan bir PowerShell yükselticisi yapar; her adımı
  `ProgramData\SunucuIzleme\logs\guncelleme-*.log` dosyasına yazar — güncelleme
  sırasında ekranda gösterilecek bir yer yok, sonradan bakılabilecek bir iz gerekiyor.
- Uçlar yetki ister; Windows dışında `POST` reddedilir.

**Ölçüldü 2026-08-09 22:5x:** `GET /api/update` gerçek GitHub sürüm listesini okudu
(`current 0.18.6 · latest 0.18.6 · available false`), yetkisiz istek **401** döndü,
macOS'ta `POST` *"yalnızca Windows kurulumunda yapılabilir"* diyerek reddetti.
`dotnet test` **97 geçti** (önce 81): sürüm karşılaştırma, taslak/ön sürüm eleme,
kurulum dosyası eşleme, geri dönüş paketinin bulunması ve **bulunamadığında
gizlenmemesi**.

> ⚠️ **Windows'ta henüz denenmedi.** Kurulumun kendisi, sağlık kapısı ve geri dönüş
> macOS'ta ölçülemez. Bunlar Windows VM'de bir kez koşturulmadan "çalışıyor" sayılmaz;
> `docs/04-kirilma-noktalari.md` bunu açık iş olarak taşıyor.

## [0.18.6] — 2026-08-09

### Düzeltilen — hiç doldurulmamış form için "yarım kalan form geri yüklendi"

İkinci sunucunun ayar ekranına girildiğinde *"Yarım kalan form geri yüklendi. Parolayı
yeniden girmeniz gerekir."* yazıyordu — kimsenin bir şey yazmadığı bir form için.

**Kök neden.** Taslağı yazan `$effect` koşulsuzdu: `loaded` olur olmaz `form`'u okuyup
`sessionStorage`'a yazıyordu. Yani formu **sadece açmak** taslak bırakıyordu. Bir sonraki
girişte taslak bulunuyor, "geri yüklendi" uyarısı çıkıyordu.

Görünenden daha kötüsü de vardı: bayat taslak, sunucudan **yeni yüklenen profilin
üstüne** yazılıyordu. Profil başka bir cihazdan değiştirilmişse ekranda sessizce eski
değerler duruyordu.

Artık form, yüklendiği andaki hâli (`baseline`) ile karşılaştırılıyor. Fark yoksa taslak
yazılmıyor, hatta duran bayat taslak siliniyor; yalnız gerçekten değişen form saklanıyor
ve yalnız o durumda uyarı çıkıyor. `HttpTargetForm` de aynı kusuru taşıyordu, o da
düzeltildi.

Uyarı metni de yanlıştı: kayıtlı parolası olan bir sunucuda parolayı yeniden girmek
**gerekmez** — boş bırakmak kayıtlıyı korur. Metin artık duruma göre değişiyor.

**Ölçüldü 2026-08-09 17:0x**, gerçek tarayıcıda (Playwright + `vite dev`), dört adım:

```
                                   ESKİ kod                YENİ kod
1. forma ilk giriş, yazı yok       taslak yazıldı          taslak yok
2. çıkıp geri gelindi              ⚠️ uyarı çıktı          uyarı yok
3. gerçekten yazıldı               taslak yazıldı          taslak yazıldı
4. yazdıktan sonra geri gelindi    uyarı + değer geldi     uyarı + değer geldi
```

Yani F5 koruması (kural 9) bozulmadı: yazılan şey hâlâ geri geliyor, yalnız
yazılmamış form artık taslak bırakmıyor.

### 🔴 Düzeltilen — panel değiştirince eski müşterinin hub'ında kalınıyordu

Müşteri paneli değiştirildiğinde uygulama **önceki müşterinin hub'ına bağlı kalıyordu**.
Üst çubuk çoğu zaman hâlâ eski panelin adını yazıyor, bağlantı göstergesi **"canlı"**
diyor, ama ekrandaki her sayı bıraktığın panelden geliyordu. Yeni panele tek bir istek
bile gitmiyordu.

**Üç ayrı kusur üst üste binmişti:**

1. `realtime.start()` bağlantı ayaktayken erken dönüyor, hub adresi de bağlantı
   kurulurken sabitleniyor. Panel değişiminde kimse `stop()` çağırmadığı için soket eski
   hub'da açık kalıyordu. Artık `switchPanel()` var: durdur, alarmları temizle, yeni
   adrese bağlan.
2. `const activeServer = $derived(getActiveServer())` — `getActiveServer()`
   `localStorage` okur, Svelte bunu izleyemez. `$derived`'ın geçersiz kılınacak bağımlılığı
   olmadığı için bir kez hesaplanıp bayatlıyordu; güncellenip güncellenmemesi render
   zamanlamasına kalmıştı. Panel artık reaktif durumda tutuluyor
   (`app/src/lib/api/panel.svelte.ts`).
3. Modül store'ları (`mssql`, `http`) eski panelin sunucularını, snapshot'larını ve
   geçmişini taşımaya devam ediyordu. İkisine de `reset()` eklendi ve panel değişiminde
   çağrılıyor.

**Ölçüldü 2026-08-09 17:5x**, gerçek tarayıcıda ve gerçek hub'la (Playwright + yerelde
çalışan API, header'daki 🔀 düğmesiyle, tam sayfa yenilemesi olmadan):

```
                          ESKİ kod            YENİ kod
header                    bedir (bayat)       acme
bağlantı göstergesi       canlı  (yalan)      bağlı değil
eski hub soketi (5299)    AÇIK                kapalı
yeni panele istek         hiç gitmedi         gidiyor
```

Bir izleme ürününde bunun anlamı şu: A müşterisinin sayıları, B müşterisinin adı
altında gösteriliyordu.

### Düzeltilen — bağlantı geri geldiğinde sürüm rozeti geri gelmiyordu

Panel sürümü yalnız açılışta bir kez soruluyordu. Uygulama, hub'a ulaşamadığı bir anda
açıldıysa rozet o oturum boyunca kayıp kalıyor, bağlantı sonradan kurulsa bile geri
gelmiyordu — üst çubuk bildiğinden azını söylüyordu. Artık gerçek zamanlı bağlantı
"canlı" olduğunda sürüm yeniden soruluyor.

Sürüm rozeti artık panelin kendisine bağlı: panel değişince yeni hub'a soruluyor, eski
panelin sürümü ekranda kalmıyor.

> Not: rozetin kaybolması çoğu zaman bir **belirti**dir, arıza değil. Bağlantı göstergesi
> "bağlı değil" diyorsa sürüm rozeti de firma adı da aynı sebepten yoktur: uygulama hub'a
> ulaşamıyordur.

## [0.18.5] — 2026-08-09

### 🔴 Düzeltilen — oturum listesi boş kalıyordu (SQL Server Standard)

Yeni kurulan sunucuda iki instance izleniyordu: Express'te *Oturumlar* listeleniyor,
Standard'da **hiçbir satır çizilmiyordu** — üstelik sekme başlığı `Oturumlar (254)`
diyerek 254 oturum saydığı hâlde. Ekranda kalan şey bir önceki sekmenin (*Özet*)
kutucuklarıydı.

**Kök neden.** `SessionsProbe` şunu yapıyordu:

```sql
FROM sys.dm_exec_sessions s
LEFT JOIN sys.dm_exec_connections c ON c.session_id = s.session_id
```

`sys.dm_exec_connections` **bağlantı** başına satır tutar, oturum başına değil. MARS
(`MultipleActiveResultSets=True` — birçok EF bağlantı dizesinde varsayılan) etkin bir
oturum her aktif batch için bir alt bağlantı açar. Böylece bir oturum N satıra çoğalıyor,
gövde mükerrer `SessionId` taşıyor ve ön yüzdeki `{#each … (x.sessionId)}` bloğu
`each_key_duplicate` fırlatıp **sekmenin tamamını** çizmeden bırakıyordu. Express'te
görünmemesinin sebebi, istemcilerinin MARS açmıyor olmasıydı.

Bağlantı bilgisi artık `OUTER APPLY (SELECT TOP 1 … ORDER BY connect_time)` ile
okunuyor: oturum başına tam bir satır. En eski bağlantı ana bağlantıdır, MARS
çocukları onunla aynı adresi paylaşır.

**Aynı sınıftan üç kusur daha bulundu ve düzeltildi:**

- `BlockingProbe` aynı 1:N join'i `blocker_c` için yapıyordu → *Bloke* sekmesi, MARS
  kullanan bir engelleyici olduğunda aynı şekilde çökerdi.
- `RequestsProbe`'da veri **doğru**: MARS'ta bir oturumun eşzamanlı birden çok isteği
  olabilir. Yanlış olan anahtardı. `request_id` eklendi, liste artık
  `sessionId:requestId` ile anahtarlanıyor.
- *Bloke* sekmesinde **ikinci bir anahtar kusuru** kalmıştı (bulundu 2026-08-09 16:2x,
  ilk düzeltme gözden geçirilirken): `sys.dm_exec_requests` **istek** başına satır tutar,
  yani MARS'lı bir oturumun iki bloke isteği aynı `blockedSessionId` ile iki kenar
  üretir. `blocker_c` join'i düzeltilmişti ama liste hâlâ tek başına
  `blockedSessionId` ile anahtarlanıyordu — sekme yine çökerdi. `BlockedRequestId`
  eklendi, anahtar `blockedSessionId:blockedRequestId` oldu.

**Ölçüldü 2026-08-09 03:1x ve 16:2x.** `svelte/internal/client/dom/blocks/each.js` okundu:
mükerrer anahtar denetimi koşulsuz ve **üretim derlemesinde de** `throw` ediyor
(`DEV` dalı yalnız hata metnini zenginleştiriyor) — yani bu, geliştirme moduna özgü
bir uyarı değil, müşteride de çöken bir hata. Guard testi eski sorguyla koşuldu ve
düştü, düzeltilmişle geçti. Aynısı *Bloke* kenarı için de yapıldı: `r.request_id`
satırı çıkarılınca `BlockingEdgesCarryBlockedRequestIdSoRowsStayUnique` düşüyor.
`dotnet test` 81/81, `npm run check` 0 hata.

**16:41 — gerçek sunucuda doğrulandı.** Sorgular ilk kez çalıştırıldı (SQL Server 2022
CU24). O instance'ta MARS açık: oturumların bağlantı sayısı 2–3. Aynı anda ölçüm —
eski sorgu **12 oturumu 24 satıra** çoğaltıyor, yeni sorgu 12 satır veriyor. Üç probe
sorgusunun tekillik kontrolü de boş döndü. `OUTER APPLY`'lı sorguların sözdizimi de ilk
kez burada sınandı; `dotnet build` bunu göstermiyordu.

## [0.18.4] — 2026-08-08

### 🔴 Düzeltilen — sessiz saatler hiç devreye girmiyordu

0.9.0'da yazılan sessiz saatler **varsayılan olarak kapalıydı** ve kimse açmadığı için üç
gün boyunca tek bir mesajı bile sessizleştirmedi. Ölçüldü 2026-08-08 02:35 (Cumartesi):
Telegram alarmı sesli geldi; veritabanında `__zamanlama` kaydı yoktu, `IsQuietAt` ilk
satırda `Enabled == false` görüp dönüyordu — 08:30–18:00 penceresine hiç bakılmıyordu.

Varsayılan **açık** yapıldı. Bedeli yok: sessizlik alarmı kesmiyor, yalnız Telegram'ın
`disable_notification` bayrağını gönderiyor — mesaj düşer, geçmiş eksilmez, telefon
susar. Ayarı daha önce elle kapatmış olan kurulumlar etkilenmez; kayıtlı değer kazanır.

**Yükseltmede davranış değişir:** zamanlamaya hiç dokunmamış kurulumlarda mesai dışı
alarmlar (kritikler dahil, `criticalAlwaysLoud` varsayılanı `false`) artık sessiz düşer.
Gece uyandırılmak isteyen *Bildirimler → Kritik alarmları mesai dışında da sesli gönder*
kutusunu açar.

### Değişen — "Başlangıç / Bitiş" neyin başlangıcı olduğunu söylemiyordu

Alanlar **mesai** aralığını alıyor, sessiz aralığı değil. 22:00–06:00 yazan biri "gece
sessiz olsun" dediğini sanır; oysa gece vardiyası tanımlamış olur ve o kurulumda sessiz
olan **gündüzdür**. Regresyon testi yazılırken bu beklenti bizzat ters kuruldu — arayüzü
okuyan kullanıcının da aynı yere düşmemesi için sebep yok.

Etiket **"Mesai saatleri"** oldu ve altına hesaplanmış sonuç yazılıyor:
*"Sessiz gidecek: çalışma günlerinde 18:00–08:30 · Cmt, Paz tüm gün · resmî tatil ve
bayramlarda tüm gün"*. Kullanıcı kaydetmeden önce ne olacağını okuyor.

### Belgelenen

- `docs/06-bildirimler.md` tablosunda **"Sessiz saatler: açık"** satırı yoktu; tablo,
  özellik açıkmış gibi okunuyordu. Eklendi.
- Sessizliğin **panelin kurulu olduğu makinenin yerel saatiyle** hesaplandığı yazıldı
  (`DateTimeOffset.Now`) — Windows'un saat dilimi Türkiye değilse pencere kayar.
- 🔴 Sürüm **üç** dosyada duruyor: `Directory.Build.props`, `setup/SunucuIzleme.iss` ve
  `app/src-tauri/tauri.conf.json`. Sonuncusu bu sürümde atlandı ve APK sessizce
  `0.18.3` adıyla derlendi — hata yok, uyarı yok, yalnız yanlış etiket. Yeniden derlendi
  ve `CLAUDE.md` release tarifi (APK + win-x64 zip adımlarıyla birlikte) düzeltildi.

## [0.18.3] — 2026-08-07

### Eklenen — grup satırında ⋮ düğmesi

Sağ tık her ortamda çalışmıyor: telefon tarayıcıları uzun basmayı metin seçmeye ayırabiliyor,
bazı webview'ler `contextmenu` olayını hiç iletmiyor. Aynı menüyü açan **görünür bir düğme**
her yerde çalışır — grup satırının sağında `⋮`.

Sağ tık ve uzun basma duruyor; bu yalnız garantili yol.

## [0.18.2] — 2026-08-07

### 🔴 Düzeltilen — sağ tık menüsü hiç açılmıyordu

Menü bileşeni yanlışlıkla **Raporlar sekmesinin içine** yerleştirilmişti. Oturumlar
sekmesinde sağ tık durumu ayarlıyor ama çizecek bileşen orada olmadığı için hiçbir şey
olmuyordu. Bileşen sekmelerin dışına alındı.

### Düzeltilen — menü ekran kenarından taşıyordu

Menü açıldıktan sonra ölçülüp ekrana sığdırılıyordu; ölçüm iki farklı yöntemle denendi
(effect içinde ve boyamadan iki kare sonra) ve **ikisinde de menü son boyutundan dar
ölçüldü**, dolayısıyla sağ kenardan taştı.

Ölçüm bırakıldı: imleç ekranın sağ yarısındaysa menü **sola**, alt yarısındaysa **yukarı**
doğru açılıyor. Masaüstü menülerinin yaptığı da budur ve hiçbir ölçüme bağlı değildir.

Ölçüldü (430×420 pencere): sağ üstte `sol=140 sag=330 · taşma yok`, sağ altta
`sol=280 sag=470 ust=70 alt=300 · taşma yok`.

## [0.18.1] — 2026-08-07

### Değişen — özet artık sütun başına seçiliyor

0.17.0'da seçilen işlem **bütün** sayısal sütunlara uygulanıyordu. Yanlıştı: CPU'nun
**toplamı** sorulur, boşta kalma süresinin **en büyüğü**, oturum belleğinin **ortalaması**.
Hepsine aynı işlemi uygulamak, birinin cevabını diğerinin sorusuna vermek demek.

DevExpress XtraGrid'in modeli araştırıldı ve aynısı uygulandı: özet **sütun başına** tanımlı
(`SummaryType`), aynı sütunun altında hem grup satırında hem tablo altında gösteriliyor.

- Sütun başlığına **sağ tıklayıp** o sütunun işlemini seçiyorsunuz: Toplam / Ortalama / Adet /
  En küçük / En büyük / yok. Sayısal olmayan sütunda menü hiç çıkmıyor.
- Değerin önünde küçük bir etiket var (`TOP`, `ORT`, `MAKS`): `3.856.792 ms` tek başına neyin
  toplamı olduğunu söylemiyor.
- Seçilenler araç çubuğunda çip olarak listeleniyor; çipe tıklamak o sütunun özetini kaldırıyor.
- Her grup ve tablo altı **tek satır**: sütunların her biri kendi işlemiyle hesaplanıyor.

Kaynak: [DevExpress — Group Summaries](https://docs.devexpress.com/WindowsForms/114625/controls-and-libraries/data-grid/getting-started/walkthroughs/summaries/tutorial-group-summaries)

## [0.18.0] — 2026-08-07

### Eklenen — sağ tık menüsü

Oturumlar tablosunda **sağ tık** (telefonda **uzun basma**) menü açıyor. Menü tıklanan yere
göre içerik değiştiriyor ve hiç yer kaplamıyor — bu komutların hepsini düğme olarak koymak
tabloyu komut çubuğuna çevirirdi.

**Grup başlığında:**
- Bu grubu aç / kapat
- **Alt gruplarıyla birlikte kapat / aç** — kademeli gruplamada eksik olan buydu
- Tümünü kapat / Tümünü aç
- Yalnız bunu göster (grubun adıyla arama)

**Satırda:** oturumu kes · bu makineyi / kullanıcıyı / uygulamayı ara.

**Sütun başlığında:** bu sütunu gizle · buna göre grupla · sütun sırasını sıfırla.

- Menü **imlecin olduğu yerde** açılıyor ve ekrandan taşarsa içeri çekiliyor (Sütunlar
  menüsünde yaşanan hatanın tekrarı olmasın diye ölçülerek konumlanıyor).
- Tabloya **tek yakalayıcı** bağlı; hangi menünün açılacağına tıklanan öğeye bakarak karar
  veriyor. Her satıra ayrı işleyici bağlamak, 500 satırda 500 işleyici demekti.
- Dokunmatikte 500 ms basılı tutmak yeterli; parmak kayarsa iptal oluyor.

## [0.17.0] — 2026-08-07

### Eklenen — özet satırları

Oturumlar tablosunda sayısal sütunlar için **Toplam / Ortalama / Adet / En küçük / En büyük**.
Hangilerinin hesaplanacağını çipten seçiyorsunuz; birden fazlası aynı anda seçilebiliyor.

- **Hem gruplu hem gruplamasız çalışır**: her grubun altında o grubun özeti, tablonun sonunda
  ekrandaki tüm satırların özeti. Kademeli gruplamada her seviye kendi özetini alır.
- Değerler **kendi sütunlarının altında** durur; yoksa hangi sayının neye ait olduğu okunmaz.
- Ölçümü olmayan satırlar hesaba **katılmaz**. Sıfır sayılsalardı ortalama yalan söylerdi —
  `NULL` ile `0` aynı şey değil.
- **Adet**, o sütunda gerçekten değer bulunan satır sayısını verir; toplam satır sayısı zaten
  grup başlığında yazıyor.
- Birim korunur: `3.856.792 ms`, `32 KB`, `1 sa 39 dk`.
- Arama açıkken özet **filtrelenmiş satırlara** göre hesaplanır — ekranda ne görüyorsanız
  onun özeti.

## [0.16.2] — 2026-08-07

### 🔴 Düzeltilen — SQL Server 2016'da çekirdek sayısı ve çalışma süresi boş kalıyordu

0.16.1 sürüm ve edisyonu kurtardı ama çekirdek/çalışma süresi hâlâ boştu. Sebep aynı
görünüm: `sys.dm_os_host_info` **bir alt sorguda** geçiyordu ve o görünüm yoksa ifade
**ayrıştırma anında** düşüyordu — alt sorguda olması onu kurtarmıyor. Böylece her sürümde
bulunan `sys.dm_os_sys_info`'dan gelecek çekirdek sayısı da onunla birlikte kayboluyordu.

Ölçüldü: müşteri sunucusu **SQL Server 2016 (13.0.5108.50)**; bu görünüm SQL 2017'de geldi.

- İşletim sistemi adı artık kendi sorgusunda; yoksa yalnız o eksik kalıyor.
- Çekirdek ve çalışma süresi **okunamadığında satır hiç yazılmıyor**. `— çekirdek · 0 sn
  açık` yazmak, ölçüm varmış gibi okunuyordu.

## [0.16.1] — 2026-08-07

### 🔴 Düzeltilen — bazı sunucularda sürüm/edisyon hiç gelmiyordu

**Belirti:** müşteri makinesinde Sistem sekmesindeki "Örnek" bölümünün tamamı tire; servis
listesi ve bellek bilgisi ise doğru geliyor.

**Sebep:** sürüm, edisyon, çekirdek sayısı ve çalışma süresi **tek sorguda** toplanıyordu ve
sorgu iki DMV'ye bağlıydı — `sys.dm_os_sys_info` ve `sys.dm_os_host_info`. İkincisi yalnızca
SQL Server 2017 ve sonrasında var. O görünüm yoksa ya da yetki verilmemişse sorgunun tamamı
düşüyor ve **hiçbir yetki gerektirmeyen** `SERVERPROPERTY` bilgileri de onunla birlikte
kayboluyordu.

**Düzeltme:** sorgu ikiye ayrıldı. Önce her kurulumda çalışan `SERVERPROPERTY` (sürüm,
edisyon, sürüm seviyesi), sonra "olursa iyi" sayılan makine bilgileri. İkincisi patlarsa
birincisi ayakta kalıyor ve hata kaydediliyor — sessizce kaybolmuyor.

**Ayrıca:** okunamayan bilgi artık tire ile gösterilmiyor. Tire "değer sıfır" gibi okunuyor;
oysa orada ölçüm hiç yok. Ne olduğu ve ne gerektiği yazılıyor.

## [0.16.0] — 2026-08-07

### Düzeltilen

- 🔴 **"Yeni sürüm var" şeridi boş sayfaya götürüyordu.** Şerit, panelin sürümüne ait yayın
  sayfasını açıyordu; o sürümde APK yayınlanmamışsa (yalnız sunucu tarafı değiştiyse)
  indirilecek bir şey bulunmuyordu — 0.15.1'de tam olarak bu oldu. Artık **en son yayın**
  açılıyor. Ayrıca bundan sonra her yayında APK da var.
- **Sürüm/edisyon satırı**, sürüm numarası okunamadığında edisyonu da gizliyordu. Artık
  ikisinden biri varsa satır çiziliyor.

### Eklenen — sessiz saatler

Bildirimler ekranında yeni bölüm: çalışma günleri, çalışma saatleri, tatiller.

- 🔴 **Mesai dışında bildirim kesilmez, sessiz gönderilir.** Telegram'ın kendi sessiz
  gönderimi kullanılıyor (`disable_notification`): mesaj normal şekilde düşer, alarm geçmişi
  eksilmez, telefon yalnız ses çıkarmaz ve titremez. Kesmek, gelmeyen alarm demektir — bir
  izleme panelinin yapabileceği en kötü şey.
- Varsayılan olarak **kritik alarmlar da sessiz**. Kullanıcının gerekçesi: *"gece ben
  uyanırsam uykusuzluktan zaten kimse bakamaz."* İsteyen "kritikler her zaman sesli"
  seçeneğini açar.
- **Gece yarısını aşan aralık** desteklenir (22:00–06:00 gibi).
- **Test mesajı her zaman sesli** gider: kullanıcı zaten gelip gelmediğine bakıyor.
- Zamanlama okunamazsa **sesli** gönderilir. Sessizlik varsayılan olamaz.

### Eklenen — resmî tatiller ve bayramlar

- Sabit tarihli yedi resmî tatil ile **Ramazan ve Kurban bayramları** hesaplanıyor;
  bayramlar `UmAlQuraCalendar` ile bulunuyor. Ölçüldü: 2026 için Ramazan **20–22 Mart**,
  Kurban **27–30 Mayıs** — Diyanet takvimiyle uyuşuyor.
- ⚠️ Ay takvimi Diyanet'ten **bir gün** şaşabilir; ekranda bu yılın listesi gösteriliyor ve
  kullanıcı **kendi gününü ekleyip** düzeltebiliyor (şirket tatili, idari izin için de).
- Hazır kütüphane arandı ve **kullanılmadı**: `Nager.Date` (22,8M indirme, MIT etiketli)
  çalışma anında `LicenseKeyException` atıyor — GitHub sponsorluğu ile lisans anahtarı
  istiyor. Ölçülmeseydi bağımlılık eklenmiş ve üretimde patlamış olacaktı.

### Değişen

- `INotificationChannel.SendAsync` artık `bool silent` parametresi alıyor. Üç kanal
  (Telegram, e-posta, webhook) güncellendi; sessiz gönderimi yalnız Telegram destekliyor,
  diğerleri parametreyi yok sayıyor (e-posta ve webhook zaten telefonu titretmiyor).

## [0.15.1] — 2026-08-07

### 🔴 Düzeltilen — sürüm, veritabanı ve servis bilgisi çoğu zaman boş geliyordu

**Belirti:** SQL Server sürümü ve edisyonu (`SQL Server 2019 · Express`) sunucu detayında
hiç görünmüyor.

**Sebep (kod okunarak bulundu):** bu üç bilgi pahalı sorgulardan geliyor ve **60 turda bir**
okunuyor. Ama anlık görüntü her turda sıfırdan kuruluyordu; probun çalışmadığı 59 turda
alanlar **boş** gidiyordu. Yani bilgi 15 dakikada bir bir kez belirip hemen kayboluyordu.
Koddaki yorum "builder önceki değerleri taşır" diyordu — taşımıyordu.

**Düzeltme:** yavaş probların son değerleri sunucu bazında saklanıyor ve her anlık görüntüye
taşınıyor; prob çalıştığında üzerine yazıyor. Yalnız erişilebilen turlarda saklanıyor —
kapalı bir sunucudan boş liste taşımak, sonraki turda "veritabanı yok" demek olurdu.

Aynı hata **Veritabanları** ve **Sistem → Servisler** listelerini de etkiliyordu; ikisi de
düzeldi.

## [0.15.0] — 2026-08-07

### Eklenen — raporlarda alan seçimi, grafik türü, tablo ve tam ekran

- **Hangi alanların çizileceğini siz seçiyorsunuz**: işlemci, SQL işlemci payı, bellek,
  SQL belleği, oturum, çalışan sorgu, bloke, en uzun sorgu.
- **Grafik türü**: çizgi (eğilim), alan (hacim), sütun (dönemleri karşılaştırma).
- **Tam ekran** düğmesi: bir grafiğe odaklanmak için ekranı kaplar.
- **Tablo görünümü**: aynı veriler satır satır, **her sütundan sıralanabilir**, tarih/saat
  ile aranabilir (`07.08` ya da `14:` yazmak o günü ya da saati getirir).
- 🔴 **Grafik başına en fazla iki seri.** Üçüncü bir seri, doğrulanmış renk çifti bittiği
  için durum renklerine (yeşil/sarı/kırmızı) girmeyi gerektirirdi; onlar bu üründe ölçülmüş
  durumu anlatıyor. Aynı birimden ikiden fazla alan seçilirse grafik ikişerli bölünür.
- Farklı birimler asla aynı eksende değil: yüzde, adet, MB ve saniye ayrı kutulara gider.

### Eklenen — sütun sırası sürüklenerek değiştirilir

Sütunlar menüsünde her satırın **⠿ tutamacı** var; sürükleyince sütun yer değiştiriyor ve
tercih tarayıcıda saklanıyor. HTML5 sürükle-bırak dokunmatikte çalışmadığı için pointer
olaylarıyla yazıldı — aynı kod parmakla da fareyle de çalışıyor.

- Tablo artık **sütun listesinden** çiziliyor; başlık ve hücreler birlikte taşınıyor.
- **İşlem sütunu her zaman sonda kalır**: sağa sabitlenmiş bir kolonun ortada durması onu
  sabitlenmiş olmaktan çıkarırdı.
- Yeni sürümde eklenen bir sütun, kaydedilmiş sırada yoksa varsayılan yerine yakın kalır.

### Düzeltilen

- 🔴 **Sütunlar menüsü telefonda ekranın dışına taşıyordu** — etiketlerin yarısı kesiliyordu
  (ölçüldü 2026-08-07, ekran görüntüsüyle). Düğme sola yakınken `right: 0` + sabit genişlik
  menüyü sola itiyordu. Dar ekranda menü artık **alttan açılan bir sayfa**: konumu ekrana
  göre kendisi belirleniyor, hiçbir kenardan taşmıyor ve başparmağa yakın duruyor.

## [0.14.1] — 2026-08-07

### Eklenen — kademeli (iç içe) gruplama

Oturumlar tablosunda gruplama artık **çok seviyeli**: önce Makine, onun içinde Uygulama,
onun içinde Kullanıcı… İstediğiniz kadar seviye.

- **Seçim sırası hiyerarşiyi belirler.** "Önce makine sonra uygulama" ile tersi farklı iki
  sorudur — *bu makineden hangi uygulamalar bağlı* ve *bu uygulama hangi makinelerden
  geliyor*. Çipteki rakam kaçıncı seviye olduğunu söyler.
- Her grup başlığı hangi alana göre gruplandığını da yazar; iç içe iki seviyede tek başına
  `MUHASEBE-PC` hangi soruya cevap verdiğini söylemiyor.
- Gruplar **yol anahtarıyla** açılıp kapanır: iki farklı makinenin altındaki aynı isimli
  uygulama birbirini kapatmaz (ölçüldü — `PC-A/rapor.exe` ile `PC-B/rapor.exe` ayrı).
- Çizim kendini çağıran tek bir parçadan yapılıyor, dolayısıyla seviye sayısı arttıkça kod
  büyümüyor.

## [0.14.0] — 2026-08-07

### Eklenen — Raporlar

Sunucu detayında yeni bir sekme: **gün / hafta / ay / yıl** aralıklarıyla dört grafik —
işlemci+bellek (%), oturum sayısı, bloke oturumlar, en uzun sorgu. Ekranda kaldığı sürece
dakikada bir tazeleniyor.

- **Ölçümler artık saklanıyor.** Şimdiye kadar hiçbir şey diske yazılmıyordu (sparkline'ın
  geçmişi bile yalnız telefonun belleğindeydi), dolayısıyla "geçen ay nasıldı?" sorusunun
  cevabı yoktu. Poller'lar ölçümlerini bir toplayıcıya veriyor, o da **dakikada bir satır**
  yazıyor: poller disk beklemiyor, yoğun sunucu boştakiyle aynı maliyette.
- **Kayıtlar yaşlanıyor:** bir haftadan eskiler saatlik, üç aydan eskiler günlük ortalamaya
  iniyor, **iki yıldan eskiler siliniyor**. Katlama ham örnek sayısına göre ağırlıklı —
  yarım kalan bir saat, tam saatle aynı ağırlıkta sayılmıyor. "En uzun sorgu" ortalama değil
  **maksimum** olarak taşınıyor, çünkü o sütun "ne kadar kötüleşti?" sorusunu cevaplıyor.
- 🔴 **Yalnız erişilebilen turlar kaydediliyor.** Ulaşılamayan sunucu için sıfır yazmak,
  kesintinin üstüne sakin bir ay çizerdi — bir izleme geçmişinin söyleyemeyeceği tek yalan.
- Okuma tarafı çözünürlüğü pencereye göre seçiyor: bir günlük pencere 1440 nokta (telefon
  çizebilir), bir yıllık ham veri yarım milyon nokta olurdu (çizemez, okunmaz da).

### Grafik kararları — hesaplanmış, göz kararı değil

- Seri renkleri `dataviz` doğrulayıcısından geçirildi. Koyu tema `#4f8ff0`/`#d75f9e`:
  en kötü renk körlüğü çifti **ΔE 13,1**, normal görme **23,3**. Açık tema kendi adımlarını
  aldı (`#2570e8`/`#c02d7d`, ΔE 18,1 / 28,3) — otomatik çevirme değil, ayrı seçim.
- **Durum renkleri (yeşil/sarı/kırmızı) seri rengi olarak kullanılmadı.** Bu üründe onlar
  ölçülmüş durumu anlatıyor; seri kimliği için harcanırsa alarm anlamını yitirir.
- **Yüzde ile adet aynı grafikte gösterilmedi.** İki ölçekli tek çizim en sık yapılan grafik
  hatası; ayrı kutulara alındı.
- **Ölçüm olmayan aralıkta çizgi kopuyor.** Boşluğu düz çizgiyle örtmek, olmayan bir ölçümü
  varmış gibi göstermek olurdu.
- İki serili grafikte gösterge var, tek serilide yok (başlık zaten adını söylüyor). İmleç
  değerleri metin renginde; seri rengini yalnız yanındaki nokta taşıyor.

## [0.13.0] — 2026-08-07

### 🔴 Düzeltilen — telefon uyuduktan sonra "bağlı değil" kalıyordu

Uygulama ilk açılışta yeşil bağlanıyor, telefon uyuyup uyandığında **kırmızı kalıyor** ve bir
daha kendiliğinden toparlamıyordu. Üç ayrı eksik vardı:

- **Yeniden bağlanma politikası pes ediyordu.** SignalR'a sabit bir gecikme dizisi
  verilmişti (`[0, 2s, 5s, 10s, 30s]`); dizi bitince istemci **bir daha hiç denemiyor**.
  Artık politika sonsuz: gecikme 30 saniyeye kadar büyüyor ama asla durmuyor.
- **Bağlantı tamamen kapandığında yeniden deneme yoktu.** `onclose` yalnız durumu
  "bağlı değil" yapıyordu; artık geri sayımı da başlatıyor.
- **Telefon uyandığında hiçbir şey tetiklemiyordu.** Sayfa uykuya gittiğini haber vermez,
  sadece çalışmayı bırakır. `visibilitychange`, `online` ve `focus` olaylarında bağlantı
  kopuksa **anında** yeniden deneniyor — geri sayımı beklemek, karşıda canlı veri varken
  kırmızı göstergeye bakmak demekti.

### Eklenen — oturumlarda arama ve gruplama

Yirmi oturum ekrana sığar, iki yüz sığmaz. Soru da zaten "hepsini göster" değil: *"şu
uygulama ne yapıyor"* ya da *"o makineden kim bağlı"*.

- **Arama** tek kutuda SPID, uygulama, makine, IP, kullanıcı, durum ve veritabanında birden
  arıyor — insanlar alan adı seçmez, parça yazar. Türkçe harf katlaması kullanıldı:
  `İSTANBUL` yazınca `istanbul-pc` bulunur.
- **Gruplama**: Uygulama / Makine / Kullanıcı / Durum / Veritabanı. Her grup başlığı satır
  sayısını taşır ve tıklayınca kapanır.
- Gruplama **sıralamayı bozmaz** — hangi sütuna göre sıraladıysanız o geçerli kalır;
  gruplama sıralamaz, toplar.
- Filtre açıkken sayaç `12 / 340 oturum` der; kaçının gizlendiğini bilmeden liste okunmaz.
- İkisi de istemcide çalışır: anlık görüntü zaten bellekte ve birkaç saniyede bir yenilenir,
  sunucuya sormak aynı sorunun iki cevabı arasında listeyi titretirdi.
- Satır işaretlemesi bir snippet'e taşındı; gruplu ve düz görünüm tek koddan çiziliyor,
  ikisi birbirinden ayrışamaz.

### Değerlendirilip yapılmayan

**SQL servisini arayüzden yeniden başlatma** istendi, birlikte geri alındı. Servisi
döndürmek tüm bağlantıları koparır, açık işlemleri geri alır ve büyük bir veritabanında
recovery dakikalar sürebilir — izleme panelinin önlemesi gereken kesintinin kendisi. Ayrıca
SQL yetkisi değil **Windows servis yönetimi** yetkisi gerektirir; ürün ilk kez izlenen
makinede yönetici hakkı isterdi. Sistem sekmesinde servisler görünmeye devam ediyor.

## [0.12.5] — 2026-08-07

### 🔴 Kurulum artık `C:\SunucuIzleme` altında — servis nihayet açılıyor

**Belirti (0.12.1–0.12.4):** kurulum "servis kuruldu ama sağlık ucu cevap vermedi" diyor,
servis Durduruldu, `logs\` boş.

**Sebep:** 0.12.1'de veri klasörünün izinleri daraltılmıştı (`icacls /inheritance:r`).
Servis LocalSystem olmasına rağmen kendi veritabanını açamadı —
`SQLite Error 14: unable to open database file` — ve log klasörü de aynı yerin altında
olduğu için **tek satır iz bırakmadan** öldü. Üç sürüm boyunca kurulumu kırdı.

**Çözüm:** izinlerle boğuşmak yerine yer değişti. Program **`C:\SunucuIzleme`** altına
kuruluyor, veri de **`C:\SunucuIzleme\data`** içinde. Program Files ve ProgramData'nın
kısıtlayıcı izinleri denklemden çıktı; özel ACL işi yapılmıyor.

- Eski kurulumdan **veri taşınıyor**: `ProgramData\SunucuIzleme` altındaki veritabanı ve
  veri koruma anahtarları yeni klasöre kopyalanıyor (anahtarlar taşınmazsa kayıtlı SQL
  parolaları bir daha çözülemezdi).
- Veritabanı açılamazsa uygulama artık **ne olduğunu söyleyerek** duruyor: yol ve
  düzeltme komutu Olay Görüntüleyici'de görünüyor. Sessiz ölüm yok.
- Doğrulandı (Windows 11, VM): kök altında klasör oluşuyor, uygulama açılıyor,
  `/api/health` → `ok`, veritabanı ve anahtarlar oluşuyor.

⚠️ **Açık borç:** veri klasörü artık sıradan yerel kullanıcıya okunabilir. Sertleştirme
denendi ve ürünü kırdı; doğru çözüm ölçülmeden tekrar denenmeyecek.

## [0.12.4] — 2026-08-06

### Düzeltilen — yükseltme artık hiçbir şey sormuyor

Kurulum **her seferinde** e-posta ve parola soruyordu. Yükseltmede hesap zaten var;
girilen parola hiçbir işe yaramıyordu (uygulama mevcut hesabı görüp geçiyor) ve bu, sessiz
güncellemeyi imkânsız kılıyordu.

- Veritabanı varsa kurulum bunu **yükseltme** sayar: hesap ekranı **atlanır**, e-posta
  üzerine yazılmaz, parola dosyası hiç yazılmaz. Mevcut hesap ve parola aynen kalır.
- Ağ ayarları (port, genel adres, vekil IP) `HKLM\SOFTWARE\SunucuIzleme` altında saklanıyor
  ve yükseltmede **geri yükleniyor** — kullanıcı hiçbir şeyi yeniden yazmıyor.
- Böylece yükseltme **tam sessiz** yapılabiliyor:

  ```
  SunucuIzleme-Setup-0.12.4.exe /VERYSILENT
  ```

  Otomatik güncelleme için gereken buydu: soru soran bir kurulum otomatikleştirilemez.
- Kaldırma kendi ayar anahtarını da temizliyor.

## [0.12.3] — 2026-08-06

### 🔴 Düzeltilen — kaldırma yarıda kalıyordu

**Belirti:** *"Runtime error (at 6:47): Could not call proc"* — kaldırma penceresi hatayla
duruyor, ürün sistemden kalkmıyor.

**Sebep:** kaldırma kodu güvenlik duvarı kuralını silerken port numarasını **kurulum
sihirbazının ağ ayarları sayfasından** okuyordu. Kaldırmada sihirbaz sayfaları yoktur;
çağrı patlıyor ve kaldırma orada kesiliyordu. Hata v0.9.1'den beri koddaydı, ilk kez
kaldırma denendiğinde ortaya çıktı.

**Düzeltme:** kural adı jokerle (`Sunucu Izleme (*`) siliniyor, hiçbir sihirbaz nesnesine
dokunulmuyor. Kaldırma ayrıca eski sürümlerin registry'de bıraktığı ayarları da
temizliyor — **0.12.1 ve öncesinin bıraktığı yönetici parolası dahil**.

> Kaldırmadan önce bunu deneyenler için: kaldırmaya gerek yok, yeni setup mevcut kurulumun
> üzerine çalışır ve servisi yeniden kurar.

## [0.12.2] — 2026-08-06

### 🔴 Düzeltilen — 0.12.1 kurulumdan sonra açılmıyordu

**Belirti:** yükseltmeden sonra servis başlamıyor, elle başlatınca da `127.0.0.1:5199`
açılmıyor, `logs\` klasöründe **tek satır yok**.

**Sebep (ölçüldü, Windows 11):** uygulama veri klasörünün yerini makine ortam
değişkeninden okuyordu. Windows'ta `services.exe` ortam bloğunu **önyüklemede** alır;
kurulumdan sonra yazılan değişkeni servis göremez. Göremeyince kod
`C:\Program Files\SunucuIzleme\data`'ya düşüyor, oraya yazamıyor ve
`UnauthorizedAccessException` ile ölüyordu. Log da yazamıyordu, çünkü log klasörü aynı
yerin altında — sessiz ölüm.

**Düzeltme:**

- Ayarlar artık **servisin komut satırında** gidiyor (`--Storage:DataDirectory`, `--urls`,
  CORS, vekil IP) — servisin her zaman gördüğü tek kanal.
- Yapılandırma hiç gelmezse Windows'ta varsayılan `C:\ProgramData\SunucuIzleme`; kod
  artık program klasörüne yazmayı **hiçbir koşulda** denemiyor.
- Klasör yine de açılamazsa ne yapılacağını söyleyen bir hata veriyor.
- **Kurulum parolası registry'ye hiç yazılmıyor.** Kilitli veri klasörüne dosya olarak
  bırakılıyor, uygulama hesabı kurar kurmaz siliyor (ölçüldü: dosya kayboldu, hesap
  kuruldu). Bu, 0.12.1'deki "parolayı `BUILTIN\Users` okuyabiliyor" bulgusunu da kapatıyor.

### Güvenlik

- 🔴 **Sahte `X-Forwarded-For` artık hız sınırını atlamıyor.** Başlık yalnız loopback'ten ve
  kurulumda verilen vekil IP'sinden kabul ediliyor. Ölçüldü: LAN üzerinden 14 denemenin
  sonunda **429** geldi (düzeltmeden önce 12 denemede tek bir 429 yoktu).
- Kurulum sihirbazına **ters vekil sunucu IP** alanı eklendi (aynı makinedeyse boş bırakılır).
- Güvenlik başlıkları: `X-Content-Type-Options`, `X-Frame-Options: DENY`,
  `Referrer-Policy`, `CSP: frame-ancestors 'none'`. Tam CSP yazılmadı — tarayıcı istemcisi
  hangi müşteri paneline bağlanacağı önceden bilinmediği için `connect-src` sayılamıyor.
- **Kurulum artık sağlık ucunu bekliyor** (setup.exe de). "Servis başladı" demek yetmiyordu;
  başlayıp hemen ölen bir servis de başlamış görünüyor — bu sürümde tam olarak o oldu.

### Geri alınan

Servisi sanal hesaba (`NT SERVICE\SunucuIzleme`) almak yazıldı ve **geri alındı**:
doğrulanmadan gönderilemez. Bugünkü arıza tam olarak doğrulanmamış bir varsayımdan çıktı.
LocalSystem açık borç olarak duruyor.

## [0.12.1] — 2026-08-06

### Eklenen — güncelleme uyarısı

Uygulama panelden **eskiyse** başlığın altında bir şerit çıkıyor: *"Panel v0.12.1
sürümünde, uygulamanız v0.12.0"* + **İndir**. Şerit yalnız uygulama geride kaldığında
görünür; panel geride kaldığında görünmez, çünkü onu telefondan kimse düzeltmez —
o durumda başlıktaki sessiz `≠` işareti kalır.

- **Şerit görünürken başlıktaki `≠` işareti gizleniyor.** Aynı olguyu iki yerde söylemek
  iki ayrı olgu gibi okunuyor.
- Kapatınca **oturum boyunca** susuyor (`sessionStorage`). Yarın geri gelen bir hatırlatma
  hatırlatmadır; bir daha hiç gelmeyen, kaçırılmış bir güncellemedir.
- Tarayıcıdan bakıldığında hiç çıkmaz: paketi zaten panel sunuyor, sürümler tanımı gereği
  aynı. Platform kontrolü yok — uyuşmazlığın kendisi zaten sinyal.

> **Neden Tauri updater değil:** Tauri v2 updater eklentisi Windows, Linux ve macOS'u
> destekliyor; **Android ve iOS'u desteklemiyor** (resmî belge, doğrulandı 2026-08-06).
> Bu ürün telefonda kullanılıyor ve masaüstü paketi yayınlanmıyor, dolayısıyla updater
> olmayan bir ürünü güncelleyen bir sistem olurdu. Android'de sessiz kurulum da mümkün
> değil — kullanıcı onayı her hâlükârda şart.

### Eklenen — sürüm artık ekranda

- **Giriş ekranında `v0.12.1`**, başlıkta ise **panelin sürümü** panel adresinin yanında.
  "Hangi sürüm bu müşteride?" her destek görüşmesinin ilk sorusu; cevabı ekranda olmalı,
  klasörlerde değil.
- **Uygulama panelden eskiyse fark söyleniyor** (`≠ v0.12.0`, sarı). Telefon uygulaması
  elle güncelleniyor, dolayısıyla geride kalabiliyor; farkı gizleyen bir ekran "eski
  uygulama" meselesini bir saatlik hata ayıklamaya çevirir. Alarm rengi değil, nabız yok —
  bilgi, uyarı değil.
- Sürüm `/api/health` ile **kimlik doğrulamadan önce** de okunabiliyor; giriş yapamayan
  birinin de sunucunun sürümünü görebilmesi gerekir.
- 🔴 Sayı tek kaynaktan geliyor: ön yüz derlemede `Directory.Build.props`'u okuyor,
  sunucu kendi assembly'sinden. Dördüncü bir kopya tutulmadı — bu projede iki kopyanın
  birbirinden kaydığı bir sürüm zaten yayınlandı (0.12.0 / 0.11.0).

### Güvenlik — ölçülerek bulundu, ölçülerek kapatıldı

Windows 11 Pro ARM64 bir VM'de kurulum yapılıp denetim **yönetici olmayan** bir oturumdan
çalıştırıldı; tehdit modeli budur. Sekiz bulgunun yedisi doğrulandı, ikisi bu sürümde
kapatılıyor:

- 🔴 **Veri klasörü artık sıradan kullanıcıya kapalı.** `ProgramData\SunucuIzleme`'nin
  kalıtılan ACL'i `BUILTIN\Users`'a **okuma** veriyordu ve bu, `keys\` altındaki veri
  koruma anahtar halkasına kadar uzanıyordu — yani kayıtlı SQL parolaları makinedeki
  herhangi bir yerel hesap için **şifresiz** sayılırdı. Kurulum kalıtımı kesip yalnız
  SYSTEM ve Yöneticiler bırakıyor (`icacls`, grup adları yerelleştiği için SID ile).
- 🔴 **Kurulum parolası hesap oluşturulunca siliniyor.** Parola registry'de düz metin
  duruyordu ve `BUILTIN\Users` okuyabiliyordu; hesap kurulduktan sonra da kalıyordu.
  Artık ilk açılışta hash'lendikten hemen sonra ortam değişkeninden temizleniyor —
  yükseltmelerde de (hesap zaten varsa) temizleniyor.

**Kapatılmayan, bilinen borçlar:** servis hâlâ LocalSystem; güvenlik başlıkları yok;
sahte `X-Forwarded-For` hız sınırını atlıyor (12 denemede `429` görülmedi). Üçü de
`docs/04-kirilma-noktalari.md`'de açık borç olarak yazılı.

⚠️ **Yükseltme mevcut kurulumu düzeltir**, ama düzeltmeden önce parolayı okumuş biri
varsa parolayı değiştirin — geçmişe dönük koruma yoktur.

### Araçlar

- **`tools/windows-guvenlik-denetimi.ps1`** — salt okunur denetim: parolanın registry'de
  olup olmadığı, veri klasörünü kimin okuyabildiği, servis hesabı, güvenlik başlıkları ve
  sahte `X-Forwarded-For` ile 12 giriş denemesi. Düzeltmeden önce/sonra aynı betik
  çalıştırılıp `ACIK` satırlarının `TEMIZ` olması beklenir.

### Ölçülen

- Self-contained **win-x64 paketi Windows 11 ARM64'te emülasyonla çalışıyor** — ARM
  makineler için ayrı paket gerekmiyor.
- İki kurulum yolu (setup.exe ve `windows-kur.ps1`) aynı ortam değişkenlerini yazıyor ve
  biri diğerini **sessizce eziyor**; ikisi aynı anda çalıştırılırsa registry karışık bir
  duruma düşüyor.

## [0.12.0] — 2026-08-06

### Eklenen — sanatsal ve ölçülmüş

Kullanıcı: *"tasarım daha sanatsal olabilir bence."* Süs eklemek yerine **veriye biçim
verildi** — bir izleme panelinde en güzel şey, bakınca anlaşılan veridir.

- **Sparkline** — her CPU ve bellek kutucuğunun altında son ~40 ölçümün şeridi. Sayı
  *nerede olduğunu* söyler; şerit *nereye gittiğini*. Gece 3'te sorulan asıl soru bu.
  - Ölçek her kartta sabit (0–100) — kartlar birbiriyle karşılaştırılabilsin diye.
    Otomatik ölçek, %2'lik gürültüyü dağ gibi gösterirdi.
  - Renk **vurgu rengi**, durum rengi değil: kartın şeridi ve noktası durumu zaten
    söylüyor, üçüncü kez söylemek gürültü olurdu.
  - Geçmiş yalnız bellekte; kalıcı olsaydı "rapor" olduğunu ima ederdi.
- **Giriş ekranı bir kapak oldu.** Üründeki tek verisiz ekran, dolayısıyla markanın nefes
  alabileceği tek yer. Nabız çizgisi açılışta **bir kez** kendini çizer — döngü olsaydı
  gerçekten bir şey ifade eden alarm nabzıyla yarışırdı. Kelime işareti degrade mürekkep;
  gövde metnine uygulansa kontrastı düşerdi, 1.6rem başlıkta okunaklı kalıyor.
- **Kartın sol kenarında durum şeridi** — listeyi kaydırırken göz noktaları tek tek
  taramıyor, şeridi yakalıyor.
- **Zeminde iki çok soluk ışık** (üst köşeler). Düz koyu bir yüzeyde kartların kenarı
  kayboluyordu; artık üzerinde durdukları bir derinlik var.

### Düzeltilen — ölçümle bulundu

- 🔴 **Açık temada "uyarı" ile "kritik" ayırt edilemiyordu.** Eski çift
  (`#b26a00` / `#d63b26`) kırmızı-yeşil renk körlüğünde **ΔE 1.0**, normal görmede
  **11.3** (eşik 15) — yani tam görüşlü biri için bile zor. Bir izleme panelinde
  "uyarı"yı "kritik" sanmak tam da olmaması gereken hata. Yeni üçlü doğrulayıcıdan
  geçiyor: `--ok #0e7a4d`, `--warn #b0851c`, `--crit #c62828`; en kötü normal-görme
  çifti ΔE 19.4, en kötü CVD çifti (kırmızı↔yeşil) 7.9 — ve durum rengi **her zaman**
  bir metin etiketiyle birlikte geliyor.
- **Durum noktası artık metin taşıyor** (`Normal` / `Uyarı` / `Kritik`). Renk tek başına
  durum anlatmaz — ekran okuyucu ve renk körü kullanıcı için nokta sessizdi.
- **Izgaradaki yetim kutucuk.** `auto-fit`, dört ölçümlü kartta üç sütun seçip dördüncüyü
  tek başına alta atıyordu; yanında kart genişliğince boşluk kalıyordu. Sabit 2 (telefon)
  / 4 (geniş ekran) sütun: hem 4'lü özet hem 8'li detay ızgarası tam doluyor.

### Araçlar

- **`tools/apk-derle.sh`** — APK'yı derler, hizalar ve imzalar. Yerelde en kırılgan halka
  Android zinciri: makinedeki varsayılan JDK 25 ile Gradle `:buildSrc`'ta çıplak bir
  `> 25.0.3` hatasıyla düşüyor (ölçüldü 2026-08-06 11:35), betik JDK 21'i sabitliyor.
  > APK'yı GitHub Actions'ta derleyip imzalayan bir workflow yazılıp **geri alındı**:
  > uygulama tek bir telefonda kullanılıyor, karşılığında imzalama anahtarı public bir
  > deponun secret'larına taşınacaktı. Android imzalama anahtarı döndürülemez — tek
  > kullanıcı için alınacak kalıcı bir risk değil.
  Ölçüldü 2026-08-06 14:23: `--target aarch64` verilse bile çıktı `apk/universal/`
  altına düşüyor, yalnız içeriği arm64'e daralıyor — 36 MB yerine **12 MB**.
- **`tools/setup-derle.sh`** — setup `.exe`'sini macOS/Linux'ta derler (Inno Setup, Wine
  altında `amake/innosetup` konteynerinde). Ölçüldü 2026-08-06 02:20: 110 sn, 39 MB.
  Yöntem v0.9.1'de bir kez elle kullanılmış ama betiğe girmemişti; belgeler "yalnız
  Windows" demeye devam ettiği için v0.12.0 release'i önce setup'sız yayınlandı. Betik
  ayrıca `Directory.Build.props` ile `SunucuIzleme.iss` sürümlerini karşılaştırır ve
  eşit değilse **derlemeyi reddeder** — bu ikisi bu sürümde birbirinden kaymıştı
  (0.12.0 / 0.11.0).

### Nasıl doğrulandı

Tarayıcıda gerçek bileşenlerle çizdirilip **ekran görüntüsü alınarak** — 430 ve 900 piksel,
koyu ve açık tema. İlk denemede sparkline kutucuğu komşusundan geniş yapıp ızgarayı
bozuyordu ve yüksek değerlerde dolgu mavi bir bloğa dönüşüyordu; ikisi de görüldüğü için
düzeltildi. Renk kararları göz kararı değil, `dataviz` doğrulayıcısıyla hesaplandı.

## [0.11.0] — 2026-08-06

### Eklenen — görsel kimlik ve tasarım

- **Simge** — tarayıcı sekmesindeki gri dünya gitti. Nabız çizgisi geçen bir sunucu rafı:
  ürünün ne izlediğini ve *canlı* izlediğini söylüyor. 16 pikselde okunabilmesi için
  şekiller kalın tutuldu, tek ince ayrıntı nabız.
- **Ana ekrana eklenebilir** (`manifest.webmanifest`): telefonda tam ekran açılır, kendi
  simgesi ve durum çubuğu rengi olur. iOS ve Android'de çalışır.
- **Tasarım elden geçirildi.** Kararların gerekçesi `app.css` başında yazılı:
  - Yeşil/sarı/kırmızı **yalnız ölçülmüş durumu** anlatır; vurgu rengi bu yüzden bilerek
    mavi-mor. Alarm rengi dekoratif amaçla kullanılsaydı anlamını yitirirdi.
  - **Yalnız kritik alarm nabız atar** — hareket süs değil, "bu hâlâ sürüyor" demek.
    Her yerde animasyon olsa hiçbiri fark edilmezdi. Nabız transform/opacity ile çalışır:
    saatlerce açık kalan bir ekranda her karede yeniden boyama yapmaz.
  - Sayılar **tabular** hizalı; canlı akışta değişen bir rakam satırı zıplatmıyor.
  - Yalnızca **tıklanabilir** kartlar hover'da tepki verir; durağan bir kartın oynaması
    yalan söyler.
  - Tablo satırında hover vurgusu — 20 satırlık bir listede gözün nerede olduğunu bilmek
    okumanın yarısı.
  - Başlık çubuğu yarı saydam + bulanık; desteklemeyen tarayıcıda düz yüzeye düşer.
  - `prefers-reduced-motion` ve görünür klavye odağı desteklenir.

## [0.10.0] — 2026-08-06

### Eklenen — sütun denetimi

- **Sütun genişliği sürüklenerek ayarlanır.** Başlığın sağ kenarındaki tutamaçtan çekin;
  çift tıklamak varsayılana döndürür. Dokunmatikte de çalışır (pointer olayları).
- **Sütun göster/gizle** — ⚙ Sütunlar menüsünden. Telefonda asıl işe yarayan bu: dokuz
  sütunu 2 cm''ye sıkıştırmak yerine dördünü seçersiniz.
- Tercihler **tablo başına** tarayıcıda saklanır; her ziyarette yeniden ayarlamak gerekmez.
- **SPID ve İşlem sütunu gizlenemez**: onlar olmadan satır ne tanınır ne de üzerinde işlem
  yapılabilir.
- Oturumlar tablosuna varsayılan gizli dört sütun eklendi: **Okuma, Yazma, Bellek, Bağlanma
  zamanı** — ihtiyaç duyanlar menüden açar.

## [0.9.1] — 2026-08-06

### Düzeltilen

- **Geniş tablolarda "Kes" düğmesi ekran dışında kalıyordu.** Oturumlar tablosu yatay
  kaydırılabiliyordu ama bu görünmüyordu, dolayısıyla tablo kesik sanılıyordu. Artık:
  - İşlem sütunu sağ kenara **sabitlendi** — kaydırmadan hep erişilebilir
  - Kaydırılabilir alanın kenarında gölge var; devamı olduğu görünüyor
  - Detay ekranı geniş masaüstlerinde daha fazla yer kullanıyor (1400 px)
  - Uzun uygulama/makine adları sütunu sonsuza kadar germiyor
- Inno Setup betiğinde  bölümü iki  içeriyordu ve derlenmiyordu.

### Eklenen

- **** — müşteriye verilecek tek dosya. Artık macOS/Linux
  üzerinde de derlenebiliyor (Docker içindeki Inno Setup ile); Windows makine gerekmiyor.

## [0.9.0] — 2026-08-05

Giriş güvenliği ve sıralama.

### Eklenen

- **Captcha** — kendi ürettiğimiz, SVG, **internet gerektirmez**. reCAPTCHA/hCaptcha/Turnstile
  bilinçli olarak elendi: panel müşteri ağında çalışıyor ve dış widget yüklenemezse giriş
  tamamen kilitlenir. Cevap şifreli, 5 dakikalık, durumsuz bir token içinde taşınır.
  - **Her girişte değil**, aynı adresten 2 başarısız denemeden sonra istenir. Nöbetçi gece
    yarısı telefondan girerken her seferinde kod okumak zorunda kalmaz; bot ise ikinci
    denemeden sonra duvara çarpar.
  - Başarılı girişte sayaç sıfırlanır. Karışan karakterler (0/O, 1/I) kullanılmaz.
- **Rate limiting**:  için adres başına dakikada 10 istek. Identity'nin hesap
  kilidi (5 hatalı parola → 15 dk) zaten vardı; bu, denemelerin hızını da sınırlar.
- **Tıklanabilir sıralama** — oturumlar, çalışan sorgular, veritabanları, servisler, sunucu
  listesi, adres listesi ve alarm geçmişi. Sayılar önce büyükten küçüğe; boş değerler her iki
  yönde de dibe iner; metin Türkçe sıralama kurallarıyla.

### Düzeltilen

- **İlk SignalR bağlantısı başarısız olursa bir daha denenmiyordu.** 
  yalnız kurulmuş bir bağlantı koptuğunda devreye giriyor; sayfa açılışında token yenileme
  gerekiyorsa ekranda sonsuza kadar "bağlı değil" kalıyordu. Artık geri çekilmeli yeniden
  deneme var.

## [0.8.0] — 2026-08-05

Tek dağıtım yolu: Windows servisi. Docker kaldırıldı.

### Eklenen

- **`setup/SunucuIzleme.iss`** — Inno Setup betiği. Müşteriye verilecek tek dosya
  (`SunucuIzleme-Setup-*.exe`): çift tıklanır, kurulumda yönetici hesabı ve genel adres
  sorulur, servis kurulup başlatılır. Kullanıcı hiçbir yapılandırma dosyası açmaz.
  - Genel adres girilmezse yalnız loopback'e bağlanır, güvenlik duvarı kuralı açılmaz.
  - `https://` olmayan adres için uyarır (parolalar ağda açık gider).
  - Kaldırmada veri klasörü **korunur** — veri koruma anahtarları silinirse kayıtlı SQL
    parolaları bir daha çözülemez.
- **`setup/sql-kurulum.sql`** — SSMS'te çalıştırılacak hazır betik: SQL girişi ve Windows
  hesabı seçenekleri, doğrulama sorgusu, kaldırma bölümü. `GRANT ALTER ANY CONNECTION`
  yalnız KILL kullanılacaksa gerekir ve ayrı işaretlendi.

### Kaldırılan

- `Dockerfile`, tüm compose dosyaları, Docker teşhis betiği, systemd unit'i ve belgelerdeki
  Docker bölümleri. Docker Hub depoları da silindi.

### Neden

Müşterilerde Docker bulunmuyor; panel zaten SQL Server'ın yanında Windows servisi olarak
çalışabiliyordu. İki dağıtım yolunu birden sürdürmek her kurulumda "hangisi?" sorusunu
doğuruyor ve iki ayrı belge seti gerektiriyordu.

## [0.7.0] — 2026-08-05

Agent kaldırıldı.

### Kaldırılan

- `MssqlRealtime.Agent` projesi, agent hub'ı, kayıt/anahtar yönetimi, sessizlik alarmı,
  agent yönetim ekranı, `Dockerfile.agent` ve agent compose dosyaları.
- `ServerProfile.AgentId` kolonu ve `Agents` tablosu (migration: `RemoveAgentSupport`).

### Neden

Dağıtım modeli netleşti: **her müşteride bir hub**, kendi Portainer makinesinde, aynı LAN'daki
SQL sunucularını doğrudan izliyor. Agent'ın tek işlevi ulaşılamayan bir ağın içinden dışa
bağlanmaktı — bu modelde hiç devreye girmiyor ve varlığı her kurulumda *"agent kurmalı
mıyım?"* sorusunu doğuruyordu.

Kod git geçmişinde duruyor (`v0.6.0`); ulaşılamayan bir müşteri çıkarsa oradan geri alınır.

## [0.6.0] — 2026-08-05

Çoklu panel: her müşteri kendi hub'ında.

### Eklenen

- **Telefonda birden fazla panel.** Kayıtlı müşteri panelleri listelenir, aralarında tek
  dokunuşla geçilir; her panel **kendi oturumunu** saklar, geçişte yeniden giriş gerekmez.
  Üst barda hangi müşterinin panelinde olduğun ve adresi görünür.
- Giriş ekranı artık boş bir adres kutusu değil, "hangi müşteri" sorusuyla başlar.

### Neden

Dağıtım kararı netleşti: **her müşteride bir hub**, kendi Portainer makinesinde, kendi SQL
sunucularını doğrudan izler. İki makine aynı ağda olduğu için agent gerekmiyor. Ama bu,
telefonda N ayrı panel adresi demek — tek adres saklayan istemci her geçişte çıkış
yapmayı gerektiriyordu.

> Agent kodu duruyor ve çalışıyor; müşteri ağına erişimin olmadığı senaryo için gerekli
> kalacak (`docs/07-agent.md`). Bu dağıtım modelinde kullanılmıyor.

## [0.5.1] — 2026-08-05

Docker imajları ilk kez gerçekten derlendi ve çalıştırıldı — iki hata çıktı, ikisi de
düzeltildi.

### Düzeltilen

- **`HEALTHCHECK` hiçbir zaman geçmiyordu.** `aspnet:10.0` imajında curl (ve wget) yok;
  konteyner sonsuza kadar "starting" kalıyor, `depends_on: service_healthy` çalışmıyordu.
  İmaja curl eklendi.
- **Agent, `runtime:10.0` imajında başlamıyordu** — `Microsoft.AspNetCore.App` bulunamıyor.
  Agent endpoint map'lemese de bu framework referansını `Core`/`Modules.Mssql` üzerinden
  miras alıyor. Agent imajı `aspnet:10.0` tabanına alındı.

### Eklenen

- `Dockerfile.agent` ve `docker-compose.agent.yml`: agent'ı müşteri tarafında konteyner
  olarak çalıştırmak için. Hiçbir port yayınlanmaz.

### Ölçülen

- Tam yığın konteynerde: hub (healthy) ← SignalR ← agent konteyneri → SQL Server 16.0.4252.3
  ölçüldü, alarm hub'da üretildi. `/data` biriminde veritabanı ve anahtar halkası kalıcı.
- İmaj boyutları: hub 486 MB, agent ~450 MB.

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
