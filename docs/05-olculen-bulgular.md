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

## Doğrulanmayı bekleyenler

| Konu | Neden ölçülemedi |
|---|---|
| Windows Server 2019 üzerinde CPU ring buffer değeri | Elde Windows sunucu yok |
| `sys.dm_server_services` tam çıktısı | Linux konteynerde yalnızca SQL Agent listelendi |
| iOS/Android'de bildirim davranışı | Cihaz/simülatör derlemesi yapılmadı |
| Yüksek sunucu sayısında poller yükü | Tek sunucuyla ölçüldü |
