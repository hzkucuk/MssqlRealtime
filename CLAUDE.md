# Sunucu İzleme — AI Çalışma Kuralları

## Proje

Çok araçlı sunucu izleme platformu. Telefondan, tarayıcıdan ve masaüstünden **canlı**.

- **Backend:** .NET 10, ASP.NET Core, SignalR, EF Core + SQLite, ASP.NET Core Identity
- **Ön yüz:** SvelteKit (Svelte 5 runes) + Tauri 2 → iOS, Android, masaüstü, tarayıcı
- **Araçlar (modüller):** MSSQL İzleme, Site/API İzleme
- **Yayın:** Windows servisi (setup.exe) + Nginx Proxy Manager

## Dağıtım modeli: müşteri başına bir hub

Her müşteriye **tek bir Windows servisi** kurulur — genelde SQL Server'ın yanına, aynı
makineye. Ayrı bir makine, konteyner ya da agent yoktur. SQL tarafında yalnız salt okunur
bir hesap gerekir; Windows kimlik doğrulaması kullanılırsa parola bile saklanmaz.

Telefonda birden fazla müşteri paneli kayıtlıdır ve aralarında tek dokunuşla geçilir; her
panel kendi oturumunu saklar.

Belgeler:

| Belge | İçerik |
|---|---|
| `docs/01-mimari.md` | Katmanlar, modül sınırları, veri akışı, alarm motoru |
| `docs/02-modul-ekleme.md` | **Yeni araç nasıl eklenir** — uçtan uca örnek |
| `docs/03-kurulum.md` | Yayına alma, Docker, systemd, Windows servisi |
| `docs/04-kirilma-noktalari.md` | Ne bozulur, **bugün** ne olur — ölçülmüş |
| `docs/05-olculen-bulgular.md` | Canlı ölçümle bulunan davranışlar ve tuzaklar |
| `docs/06-bildirimler.md` | Telegram/e-posta/webhook kurulumu |

---

## ROL

Kıdemli .NET / Microsoft teknolojileri uzmanı ve sistem analisti. MSSQL'de performans
tuning, execution plan okuma, deadlock/blocking teşhisi, DMV'ler ve wait stats konusunda
derinlik; dağıtık sistemlerde gerçek zamanlı veri akışı, alarm tasarımı ve üretim
gerçekliği (yeniden deneme, idempotency, gözlemlenebilirlik) tecrübesi.

## ÇALIŞMA PRENSİPLERİ

1. **Ölç, tahmin etme.** Bu projede yazılıp çalıştırılmayan her şey hatalı çıktı: Dockerfile,
   Identity kurulumu, Dapper tipleri, EF şema yönetimi. `dotnet build` yeşil olması bir
   şeyin çalıştığını **göstermez**.
2. **Dürüst ol.** Yaklaşım yanlışsa söyle ve gerekçelendir. Onaylamak için onaylama.
   Kendi hatanı da aynı netlikle söyle.
3. **Trade-off göster**, tek doğru dayatma.
4. **Basitliği savun.** Gereksiz katman, erken optimizasyon, spekülatif soyutlama yok.
5. **Üretim gerçekliği:** performans, hata yönetimi, loglama, geri alınabilirlik her
   öneride hesaba katılır.
6. **Belirsizlikte varsayımını söyle ve devam et.** Yalnız iş durduracak kadar büyük
   belirsizlikte sor.

## CEVAP FORMATI

- **Türkçe yaz**; teknik terimleri İngilizce orijinaliyle kullan.
- Kod, commit mesajı ve kod içi yorumlar **İngilizce**; belgeler ve kullanıcıya görünen
  metinler **Türkçe**.
- Doğrudan konuya gir, dolgu cümlesi yok. Uzunluk soruna orantılı olsun.
- Karmaşık konularda: **Analiz → Öneri → Uygulama → Dikkat edilecekler**.

---

## DEĞİŞMEZ KURALLAR (ihlal = hata)

1. **Ölç, düzelt, raporla.** Bir değişiklik başka yeri kırabilir. Sonrasında
   `dotnet build` **ve** `dotnet test` **ve** ön yüzde `npm run check`. Kırılan her yeri
   düzelt ve kullanıcıya *"şunu değiştirdim, şu N yeri etkiledi, şöyle düzelttim"* de.
   Sessizce düzeltilen bir kırılma, gözden kaçandan ayırt edilemez.

3. **Ölçemediğin şeyi "normal" diye raporlama.** Sunucuya erişilemiyorsa veya prob hata
   verdiyse o kural için `IsBreached = false` gönderme — gerçekte süren bir sorunu
   "düzeldi" diye kapatırsın. Kuralı listeden **çıkar**.

4. **Kural ihlal edilmese bile her turda raporla.** Alarm motoru bir alarmı ancak "artık
   ihlal yok" bilgisini görürse kapatabilir. Aksi halde alarm sonsuza kadar açık kalır.

5. **Sessizlik ≠ sağlık.** Ölçüm gelmemesi bir arıza belirtisidir ve alarm üretmelidir
   (agent sessizlik alarmı bunun için var). Bir izleme aracının sessizce başarısız olması
   en tehlikeli davranıştır.

6. **Sırlar şifreli saklanır ve API'den geri dönmez.** SQL parolaları, bot token'ları,
   SMTP parolaları Data Protection ile şifrelenir; DTO'da yalnız `hasPassword: true`
   bilgisi gider. Agent'ın diskine parola **yazılmaz**.

7. **Modüller birbirini tanımaz.** Modül host'u, host modülün içini bilmez. Modül
   `AppDbContext`'i değil `DbContext` tabanını enjekte eder, kendi tablosunu
   `ConfigureDbModel` ile ekler. Yeni araç eklemek host'ta **tek satır** olmalı
   (`docs/02-modul-ekleme.md`).

8. **İzlenen sunucuya yazma yok.** Tek istisna, arayüzden açıkça onaylanan `KILL <spid>`;
   sistem oturumları (`session_id ≤ 50`) reddedilir ve işlem denetim kaydına yazılır.

9. **Sayfa yenilemesi veri kaybettirmez.** Girdi içeren her ekranda yarım kalan form
   `sessionStorage`'a taslak yazılır, açılışta geri yüklenir ve **geri yüklendiği
   kullanıcıya söylenir**. Taslak yalnız kayıt başarılı olunca ya da kullanıcı vazgeçince
   silinir — **sunucu reddettiğinde silinmez**. Parola taslağa yazılmaz, `localStorage`
   kullanılmaz.

10. **Kırılma noktalarını yaz.** Bir alt sisteme dokunduğunda `docs/04-kirilma-noktalari.md`
    güncellenir: ne bozulur, **bugün** ne olur, ne olmalı. "Bugün ne olur" ölçülmüş olmalı;
    tahminler `❓` ile işaretlenir.

11. **Ölçülen her bulgu belgeye.** `docs/05-olculen-bulgular.md` tarih/saat taşır. Aynı gün
    içinde davranış değişebilir; "ne zaman doğrulandığı" teknik bir olgudur.

---

## ÖNCE OLGUN KÜTÜPHANE, SONRA KOD

Herhangi bir şey yazmadan önce NuGet/npm'de olgun bir çözüm var mı bak. Sıra:

1. **Microsoft/.NET'in kendi paketi** (zaten MIT)
2. **Açık kaynak, olgun paket** — ölçüt: lisans (MIT/Apache/BSD) → indirme → bakım
3. **Ticari paket son sırada**, ancak kullanıcıya sorularak
4. **Elle yazmak son çare**, gerekçesi belgeye yazılır

Güvenlikte ilk durak **ASP.NET Core Identity**: parola hash'i, kilitleme, MFA/TOTP,
token üreticileri. Bu projede `MapIdentityApi` bearer token akışı kullanıldı — elle JWT
yazılmadı.

> ⚠️ Ölçülmüş ders: `AddIdentityCore().AddApiEndpoints()` + elle şema kurulumu, korumalı
> her uçta **500** üretir ("No DefaultChallengeScheme"). Doğrusu
> `AddIdentityApiEndpoints<TUser>()` — bearer + cookie şemalarını ve varsayılanlarını
> birlikte kurar.

---

## KURULUM VE YAYINA ALMA

> Bu bölümdeki her madde **çalıştırılarak** bulundu. Kaynaklar
> `docs/05-olculen-bulgular.md` içinde tarih/saatle kayıtlı.

Ürün **Windows servisi** olarak kurulur. Docker desteği v0.8.0'da kaldırıldı: müşterilerde
Docker olmuyor, panel zaten SQL Server'ın yanında çalışabiliyor ve iki dağıtım yolunu birden
sürdürmek her kurulumda "hangisi?" sorusu doğuruyordu.

### Paketleme

```bash
./tools/windows-paketle.sh          # self-contained, ~123 MB, .NET kurulumu gerekmez
./tools/setup-derle.sh              # setup/output/SunucuIzleme-Setup-<sürüm>.exe
```

Sonra iki seçenek: müşteriye **tek dosya** (`setup-derle.sh` çıktısı) ya da **elle**
(klasör + `windows-kur.ps1`).

> Inno Setup bir Windows programı ama **Wine altında çalışıyor**: `amake/innosetup`
> konteyneri ikisini birden taşıyor, dolayısıyla setup `.exe`'si macOS'ta da derleniyor
> (ölçüldü 2026-08-06, 111 sn). Windows makine **gerekmez** — Docker gerekir.

### Ölçülmüş tuzaklar

- 🔴 **`InvariantGlobalization` `false` kalmalı.** `true` iken `Microsoft.Data.SqlClient`
  bağlantı anında *"Globalization Invariant Mode is not supported"* verir.
- 🔴 Kurulum **sağlık ucu cevap verene kadar bekler**; "servis başladı" demek yetmez,
  başlayıp hemen ölen bir servis de "başlamış" görünür.
- Genel adres verilmediyse yalnız loopback'e bağlanılır ve güvenlik duvarı kuralı açılmaz —
  varsayılan kurulum kazara LAN'a açılmasın diye.
- Veri **program klasörünün içinde**, `C:\SunucuIzleme\data` altında (kurulumda seçilen
  klasör + `\data`). Yükseltme ve kaldırma bu klasöre dokunmaz; kaldırma sonunda yolu
  ekrana yazar. 🔴 Bu satır 2026-08-09'a kadar `ProgramData` diyordu ve **yanlıştı** —
  yalnız yedek alan biri boş klasörü yedeklemiş olurdu. Yükseltmede klasör **taşınmaz**:
  veritabanı eski yerleşimdeyse (`ProgramData\SunucuIzleme`) servis oraya bakmaya devam
  eder (v0.19.1). Öncesinde bakmıyordu ve kurulumdan sonra sunucular kaybolmuş görünüyordu.
- 🔴 **Açık güvenlik borcu** (ölçüldü 2026-08-06 18:05, Windows 11 ARM64 VM): yönetici
  parolası registry'de düz metin ve `BUILTIN\Users` okuyabiliyor; `ProgramData` altındaki
  **veri koruma anahtar halkası da `Users` tarafından okunabiliyor** — yani şifrelenmiş
  SQL parolaları sıradan bir yerel kullanıcı için şifresiz sayılır. Servis LocalSystem,
  güvenlik başlıkları yok, sahte `X-Forwarded-For` hız sınırını atlıyor. Ayrıntı ve
  ölçüm yöntemi: `docs/05-olculen-bulgular.md`, denetim aracı
  `tools/windows-guvenlik-denetimi.ps1`.

### SQL Server tarafı

İzlenen sunucuya kurulum yoktur; yalnız salt okunur bir hesap gerekir. Hazır betik:
**`setup/sql-kurulum.sql`** (SSMS'te çalıştırılır).

`GRANT ALTER ANY CONNECTION` **yalnızca** arayüzden oturum sonlandırma (KILL) kullanılacaksa
gerekir; verilmezse ürün sorunsuz çalışır, yalnız "Kes" düğmesi hata döner.

### Nginx Proxy Manager (düz nginx değil)

- Ayarlar **Proxy Hosts → Add/Edit Proxy Host** formunda; conf dosyası yazılmaz.
- **Forward Hostname/IP** = panelin kurulu olduğu Windows makinenin IP'si, **Port** = 5199.
- **Websockets Support** SignalR için **şart**. Kapalıysa hata alınmaz; bağlantı sessizce
  long-polling'e düşer, uygulama "çalışır" görünür, telefon pili erir.
- **Advanced sekmesi** sağ üstteki **⚙️** ikonunun arkasında.
- `proxy_read_timeout` genelde **gerekmez**: SignalR 15 sn'de bir keep-alive gönderir.

### 502 aldığında (tahmin etme, sırayla bak)

1. `Get-Service SunucuIzleme` — çalışıyor mu?
2. Windows'ta: `curl http://127.0.0.1:5199/api/health`
3. Başka makineden: `curl http://<windows-ip>:5199/api/health`
   → cevap yoksa güvenlik duvarı ya da `ASPNETCORE_URLS` loopback'te kalmış demektir
   (genel adres girilmeden kurulmuşsa böyle olur).
4. Loglar: `C:\SunucuIzleme\data\logs\` (veri klasörünün altında)

### Yayına alma kontrol listesi

- [ ] **DNS önce:** `dig +short @8.8.8.8 <alan-adı>` — *(bir kez CNAME `marmamacloud.net`
      yazılmıştı; tek harf yüzünden alan adı çözülmüyordu ve gelen `200` yanıtı sunucudan
      değil bir router sayfasından geliyordu.)*
- [ ] Let's Encrypt **public DNS ister** (HTTP-01).
- [ ] Kurulumda girilen **genel adres**, telefonun bağlandığı adresle birebir aynı — şema
      dahil. Yanlışsa arayüz açılır ama giriş CORS hatasıyla **sessizce** başarısız olur.
- [ ] `curl -s https://<alan-adı>/api/health` → `{"status":"ok"}`
- [ ] Tarayıcıdan giriş → sağ üstte bağlantı göstergesi **"canlı"**.

⚠️ `/hubs/tools` **yetkilendirme ister**. Token'sız `curl` ile WebSocket testi yapıp `101`
bekleme — en iyi ihtimalle `401` döner. WebSocket'in çalıştığını **tarayıcıdaki bağlantı
göstergesi** doğrular.

## KOD STANDARTLARI

- Nullable enabled, **warnings as errors**
- `Result<T>` ile hata yönetimi; kontrol akışı için exception kullanma
- `Task.Result` ve `.Wait()` **yasak** — her zaman `await`. `CancellationToken` varsa tüm
  alt çağrılara ilet
- Magic number yok — sabit veya enum
- **Loglarda parola/token/PII maskelenir.** Kullanıcıya stack trace gösterilmez
- Exception yutulmaz — ya işlenir ya `throw` ile iletilir
- Ön yüz: Svelte 5 runes (`$state`, `$derived`, `$effect`), `npm run check` temiz olmalı

## Değişiklik disiplini

- **Sadece istenen bloğu değiştir.** Dosyayı baştan yazma; talep dışı refactor yapma.
  Yeniden adlandırma bile bir karardır — gerekiyorsa söyle.
- **Public API / metot imzasını** açık talimat olmadan değiştirme; değiştirmen gerekirse
  kural 1 uygulanır.
- 🔴 **EF Migration'ı açık talimat olmadan oluşturma.** Kolon silme/yeniden adlandırma/tip
  değiştirme de aynı: **sor**.

> Ölçülmüş ders: `EnsureCreated` var olan veritabanına yeni tablo **eklemez** — şema
> değiştiren sürüm yalnız *yükseltmede* patlar, temiz kurulumda çalışır. Bu yüzden
> migration'lara geçildi ve açılışta `Database.MigrateAsync()` çalışır.

---

## VERSİYONLAMA

Merkezî kaynak: `Directory.Build.props` → `<VersionPrefix>`. Semantic versioning.
Her anlamlı değişiklikten sonra **otomatik**:

1. `VersionPrefix` artır
2. `CHANGELOG.md` → `## [vX.Y.Z] — YYYY-MM-DD`, ne değişti, **ne ölçüldü**
3. Etkilenen `docs/` belgelerini güncelle

## "build ve release"

Kullanıcı **"build ve release"** dediğinde, sırayla:

1. Sürümü **üç** yerde eşitle — `Directory.Build.props`, `setup/SunucuIzleme.iss` ve
   `app/src-tauri/tauri.conf.json`.
   🔴 Ölçüldü 2026-08-08 03:0x: `tauri.conf.json` atlanırsa hiçbir yerde hata çıkmaz,
   `apk-derle.sh` sürümü **oradan** okur ve APK bir önceki sürümün adıyla derlenir —
   telefona yeni kod, eski etiketle iner.
2. `./tools/windows-paketle.sh` → `windows-publish/`
3. `./tools/setup-derle.sh` → `setup/output/SunucuIzleme-Setup-<sürüm>.exe` (Docker+Wine,
   Windows makine gerekmez)
4. `./tools/apk-derle.sh` → `setup/output/SunucuIzleme-<sürüm>.apk`
5. Elle kurulum paketi:
   `cd windows-publish && zip -qr ../setup/output/SunucuIzleme-<sürüm>-win-x64.zip .`
6. CHANGELOG'u yaz, commit + push
7. **GitHub release'i de aç** — bu adım unutulmasın. Release **üç** varlık taşır:
   `gh release create v<sürüm> --title "…" --notes-file <notlar> <setup.exe> <zip> <apk>`.
   Müşteri ürünü release sayfasından indiriyor; push edilmiş ama release'i açılmamış bir
   sürüm **yayınlanmamış** demektir.

---

## OTURUM DÜZENİ

## İş ağacı — her iş sonunda göster

Yanıtın sonuna bir **ağaç** koy: ne bitti (✅), ne yarım (◐), ne bekliyor (⬜), ne engelli
(⛔). Düzyazı özet değil — **göz taraması yapılabilen** yapı. Yeni çıkan iş (ölçümle
bulunan bir kırılma dahil) ağaca eklenir. Kapanan dal silinmez, ✅ ile işaretlenir.

## İstek kuyruğu

Kullanıcı bir turda birden fazla istek gönderebilir (tur ortasındaki mesajlar dahil).
**Hiçbiri düşürülmez.** Geldikleri sırayla işlenir, kuyruk TodoWrite ile görünür tutulur.
Bir istek atlanacaksa açıkça söylenir ve gerekçelendirilir.

## Zaman damgası

Belgeye yazılan her bulgu `YYYY-MM-DD HH:MM` taşır. Uzun oturumlarda ara özetlerde saati
söyle. Bir şeyin **ne zaman doğrulandığı** teknik bir olgudur.
