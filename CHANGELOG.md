# Değişiklik günlüğü

Biçim: [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/) · Sürümleme: [SemVer](https://semver.org/lang/tr/)

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
