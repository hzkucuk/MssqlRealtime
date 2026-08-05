# Setup (Windows kurulum dosyası)

Müşteriye verilecek tek dosya: **`SunucuIzleme-Setup-<sürüm>.exe`**

Kullanıcı çift tıklar, üç ekranda e-posta/parola ve genel adresi girer, kurulum biter —
hiçbir yapılandırma dosyası düzenlemez, komut satırı açmaz.

## Üretmek

**1. Yayın klasörünü hazırla** (Mac/Linux/Windows, .NET SDK gerekir):

```bash
./tools/windows-paketle.sh
```

`windows-publish/` çıkar (~123 MB): .NET runtime ve web arayüzü gömülü, hedef makineye
hiçbir şey kurulmasını gerektirmez.

**2. Setup'ı derle** (yalnız Windows, [Inno Setup 6+](https://jrsoftware.org/isdl.php)):

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" setup\SunucuIzleme.iss
```

Sonuç: `setup\output\SunucuIzleme-Setup-0.8.0.exe`

> Inno Setup yalnızca Windows'ta çalışır. Mac'te `windows-paketle.sh` ile klasörü üretip
> derleme adımını bir Windows makinede yapmak gerekir.

## Setup ne yapıyor

| | |
| - | - |
| Dosyalar | `C:\Program Files\SunucuIzleme` |
| Veri | `C:\ProgramData\SunucuIzleme` — veritabanı + veri koruma anahtarları |
| Servis | `SunucuIzleme`, otomatik başlatma, çökerse yeniden başlatma |
| Güvenlik duvarı | **Yalnızca** genel adres girildiyse açılır (Domain + Private) |
| Bağlama | Genel adres boşsa `127.0.0.1`, doluysa `0.0.0.0` |

Kurulumda sorulanlar ortam değişkenlerine yazılır; parola ilk açılışta veritabanına
hash'lenerek kaydedilir.

## Kaldırma

Denetim Masası → Programlar. Servis durdurulur ve silinir, güvenlik duvarı kuralı kaldırılır.

🔴 **`C:\ProgramData\SunucuIzleme` bilerek silinmez.** İçinde izlenen sunucu profilleri ve
veri koruma anahtarları var; anahtarlar silinirse kayıtlı SQL parolaları bir daha
çözülemez. Kaldırma sonunda bu klasörün yeri ekranda gösterilir.

## Sürüm yükseltme

Yeni setup'ı çalıştırmak yeterli: servis durdurulur, dosyalar değiştirilir, servis yeniden
kurulup başlatılır. `ProgramData` altındaki veri korunur ve veritabanı şeması açılışta
otomatik güncellenir.

## Sürüm numarasını güncellemek

`SunucuIzleme.iss` içindeki `#define AppVersion` değeri `Directory.Build.props` →
`VersionPrefix` ile aynı olmalı.
