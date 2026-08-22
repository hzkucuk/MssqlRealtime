# Kurulum ve yayına alma

## Desteklenen sunucular

**Windows Server 2016 ve üzeri.** Paket self-contained olduğu için hedef makineye .NET
kurulmaz; işletim sisteminde hangi .NET sürümünün bulunduğu önemli değildir.
Server 2016 üzerinde çalıştığı ölçüldü (2026-08-06).

## Ne nerede çalışır

| Bileşen | Nerede | Nasıl |
| - | - | - |
| İzleme paneli | Müşterinin Windows makinesi — genelde SQL Server'ın yanı | Windows servisi |
| İzlenen SQL Server'lar | Aynı makine ya da aynı LAN | **hiçbir kurulum yok**, salt okunur hesap |
| Nginx Proxy Manager (TLS) | Var olan proxy makinesi | `deploy/nginx/README.md` |
| Mobil / masaüstü uygulama | Telefon, PC | Tauri paketleri — ya da doğrudan tarayıcı |

Panel bir arka plan servisidir: telefon kapalıyken de ölçer, alarm üretir, bildirim gönderir.

---

## 1. Paketle (kendi makinende)

```bash
./tools/windows-paketle.sh
```

`windows-publish/` çıkar (~123 MB): .NET runtime ve web arayüzü gömülü — müşteri sunucusuna
.NET kurmak gerekmez.

## 1b. Paketi Windows'a indirirken

⚠️ Windows Server 2016/2019 + PowerShell 5.1'de `Invoke-WebRequest` GitHub'dan indiremez:
*"SSL/TLS güvenli kanalı oluşturulamadı"* — PowerShell TLS 1.0/1.1 dener, GitHub 1.2+ ister.

```powershell
# ya bu satırı önce çalıştırın (yalnız o oturum için geçerli)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

Invoke-WebRequest -UseBasicParsing -Uri "<release-url>" -OutFile "$env:TEMP\SunucuIzleme.zip"
```

`curl.exe` de bir seçenek ama **her sunucuda yok** (Windows'a 1803 ile geldi; **Server 2016'da
hiç yoktur**, eski 2019 build'lerinde de bulunmaz). Ayrıca `curl` değil `curl.exe` yazılmalı —
PowerShell'de `curl` takma adı `Invoke-WebRequest`'e gider.

⚠️ Güncellemede **`Stop-Service` indirmeden sonra** gelmeli: servis çalışırken dosyalar
kilitlidir ve `Expand-Archive` erişim hatası verir.

## 2. Kur (müşterinin Windows makinesinde)

**A) Setup ile** — müşteriye tek dosya verilecekse (önerilen)

Setup'ı bir kez derle — Mac/Linux'ta da olur, Docker yeter (Inno Setup Wine altında
çalışıyor, ölçüldü 2026-08-06):

```bash
./tools/setup-derle.sh
```

Çıkan `setup\output\SunucuIzleme-Setup-*.exe` çift tıklanır; kurulumda yönetici hesabı ve
genel adres sorulur, servis kurulup başlatılır. Ayrıntı: `setup/README.md`.

**B) Elle** — kendi/test kurulumlarında

`windows-publish` klasörünü kopyala (ör. `C:\SunucuIzleme`), yönetici PowerShell'de:

```powershell
.\windows-kur.ps1 -AdminPassword 'guclu-parola' -PublicOrigin 'https://izleme.musteri.com'
```

Betik servisi kurar, çökerse yeniden başlatma politikası tanımlar, güvenlik duvarını açar ve
**sağlık ucu cevap verene kadar bekler** — doğrulamadan "başarılı" demez.

`-PublicOrigin` verilmezse yalnız `127.0.0.1`'e bağlanır ve güvenlik duvarı kuralı açılmaz;
varsayılan kurulum kazara LAN'a açılmasın diye.

Windows kimlik doğrulaması kullanacaksan servisi etki alanı hesabıyla çalıştır:

```powershell
.\windows-kur.ps1 -AdminPassword '...' -Account 'DOMAIN\svc_izleme'
```

Bu yolda panelde *Windows (entegre)* seçilir ve **SQL parolası hiç saklanmaz**.

## 3. SQL Server tarafı

İzlenen sunucuya kurulum yok; yalnız salt okunur bir hesap gerekir. Hazır betik:
**`setup/sql-kurulum.sql`** — SSMS'te aç, ihtiyacına uyan bölümü çalıştır.

Özet:

```sql
CREATE LOGIN [izleme] WITH PASSWORD = N'...', CHECK_POLICY = ON;
GRANT VIEW SERVER STATE TO [izleme];      -- zorunlu
GRANT VIEW ANY DEFINITION TO [izleme];    -- zorunlu
-- msdb okuma: "son yedek" sütunu için (isteğe bağlı)
-- GRANT ALTER ANY CONNECTION: yalnız arayüzden oturum kesecekseniz
```

`GRANT ALTER ANY CONNECTION` verilmezse ürün sorunsuz çalışır; yalnız **"Kes"** düğmesi
yetki hatası döner.

Panelde sunucu eklerken **"Bağlantıyı sına"** düğmesi bu izinleri kontrol eder ve eksikse
hangi `GRANT`'in gerektiğini ekranda söyler.

## 4. İlk giriş

Kurulumda girdiğin e-posta ve parolayla `https://<adres>` — ya da yerelden
`http://127.0.0.1:5199`.

Setup kullanmadıysan ve parola vermediysen rastgele bir parola üretilip loga **bir kez**
yazılır: `C:\SunucuIzleme\data\logs\`.

Kayıt (`/api/auth/register`) ucu kapalıdır — ikinci bir kullanıcı oluşturulamaz.

## 5. Nginx Proxy Manager

`deploy/nginx/README.md`. Kritik iki nokta:

- **Forward Hostname/IP** = panelin kurulu olduğu Windows makinenin IP'si, **Port** = 5199
- **Websockets Support** açık olmalı — kapalıysa hata alınmaz, SignalR sessizce
  long-polling'e düşer ve telefon pili erir
- 🔴 **`/hubs` için ayrı bir Custom location tanımlama.** Varsayılan `/` yönlendirmesi bu
  yolu zaten kapsıyor. Ayrı bir location varsa ve hedefi yanlışsa ortaya çok kandırıcı bir
  tablo çıkar: `/api/*` çalışır (panel açılır, araç listesi gelir, giriş yapılır) ama
  `/hubs/*` **502** döner ve uygulama yalnız "bağlı değil" der. Ölçüldü 2026-08-22, bkz.
  `docs/05-olculen-bulgular.md` — orada silinince düzelen bloğun tamamı var. Blok yanlış
  yazılmış olmak zorunda değil; WebSocket başlıkları doğru olsa bile **hedef adres**
  varsayılan yönlendirmeninkinden farklıysa aynı sonuç çıkar.

Bir dakikada ayırmanın yolu — ikisini karşılaştır:

```bash
curl -s -o /dev/null -w "%{http_code}\n" https://<alan-adı>/api/health   # 200 bekleniyor
curl -s -o /dev/null -w "%{http_code}\n" https://<alan-adı>/hubs/xyz     # 200 bekleniyor
```

İkincisi 502 veriyorsa sorun panelde değil, vekil sunucudadır: `/hubs` panele hiç
ulaşmıyordur. (Olmayan bir yol bilerek seçildi: panele ulaşırsa arayüzün `index.html`'i
döner, yani **200**. 502 yalnız vekil sunucudan gelir.)

## 6. Bildirimleri aç — atlanırsa uygulama kapalıyken kimse haber almaz

Panelde **⚙️ Bildirimler** → en az bir kanal: Telegram (en hızlısı, ~2 dk), e-posta veya
webhook. Her birinde **Test gönder** var; token'ın yanlış olduğunu gece 03:00'te değil şimdi
öğren. Ayrıntı: `docs/06-bildirimler.md`.

> Her panelin kendi veritabanı vardır: bir müşteride Telegram'ı kurmak diğerinde kurulmuş
> saymaz. Aynı bota gönderebilirsin — mesajlarda sunucu ve müşteri adı yazıyor.

## 7. Mobil uygulama

```bash
cd app
npx tauri build           # masaüstü (macOS/Windows/Linux)
npx tauri ios build       # Xcode gerekli
npx tauri android build   # Android SDK + NDK gerekli
```

Uygulama birden fazla müşteri panelini kaydeder ve aralarında tek dokunuşla geçer; her panel
kendi oturumunu saklar. Tarayıcıdan da aynı adres kullanılabilir — uygulama şart değil.

## 8. Yedekleme — atlanırsa acıtır

`C:\SunucuIzleme\data` klasörünün tamamı:

| Dosya | Kaybedilirse |
| - | - |
| `mssqlrealtime.db` | Sunucu profilleri, alarm geçmişi ve yönetici hesabı gider |
| `keys\` | 🔴 **Kayıtlı tüm SQL parolaları okunamaz hâle gelir** |
| `logs\` | Denetim izi (kim hangi oturumu kesti) gider |

## 9. Güncelleme

Yeni setup'ı çalıştır — servis durur, dosyalar değişir, servis yeniden kurulup başlar.
`C:\SunucuIzleme\data` altındaki veri korunur ve veritabanı şeması açılışta otomatik güncellenir.

Elle kurulumda:

```powershell
Stop-Service SunucuIzleme
# dosyaları kopyala
Start-Service SunucuIzleme
```

⚠️ Yükseltmeden önce yine de `C:\SunucuIzleme\data` klasörünü yedekle: migration geri
alınamaz.
