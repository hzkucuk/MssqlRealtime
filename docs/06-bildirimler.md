# Bildirimler — uygulama kapalıyken haber almak

## Sorun

Uygulama içi bildirim yalnız telefon bağlıyken çalışır. Gerçek push (APNs/FCM) Apple
Developer üyeliği, Firebase projesi ve mağaza sürümü ister — ve Tauri 2'nin resmi push
eklentisi yoktur.

## Çözüm: sunucu bildirimi kendisi gönderir

Servis zaten 7/24 ölçüyor. Alarm oluştuğunda telefonun bağlı olmasını beklemek yerine
**sunucu doğrudan gönderir**:

| Kanal | Uygulama kapalıyken | Kurulum maliyeti | Not |
|---|---|---|---|
| **Telegram** | ✅ | Ücretsiz, 2 dakika | Kilit ekranında bildirim; önerilen |
| **E-posta (SMTP)** | ✅ | Mevcut mail sunucun | Yavaş ama her yerde var |
| **Webhook** | ✅ | Bir URL | Slack, Teams, kendi sistemin |
| Uygulama içi | ❌ (yalnız açıkken) | — | Anında, en zengin görünüm |

Hepsi aynı anda açık olabilir; her biri kendi seviye eşiğine sahiptir.

## Telegram — adım adım

1. Telegram'da **@BotFather**'a yaz → `/newbot` → bota bir ad ver.
2. BotFather bir **token** verir: `123456789:AAH...`
3. Kendi botuna bir mesaj gönder (bot sana ilk mesajı atamaz).
4. Tarayıcıda aç: `https://api.telegram.org/bot<TOKEN>/getUpdates`
   → `"chat":{"id":123456789}` içindeki sayı senin **chat id**'in.
5. Uygulamada **⚙️ → Telegram**: token ve chat id'yi gir, *Açık* işaretle, **Test gönder**.

Grup kullanacaksan botu gruba ekle; grup id'si eksi ile başlar (`-1001234567890`).

## Seviye eşiği

Her kanal için ayrı ayarlanır:

- **Tümü** — uyarı ve kritik
- **Uyarı ve üstü** — varsayılan
- **Yalnız kritik** — gece uyanmak istemediğin kanallar için

"Normale döndü" mesajları ayrıca açılıp kapatılabilir.

⚠️ Bu eşik, sunucu profilindeki **alarm sınırlarından farklıdır**. Sınırlar alarmın *oluşup
oluşmayacağını*, buradaki seviye ise oluşan alarmın *bu kanaldan gidip gitmeyeceğini* belirler.

## Gürültü kontrolü — zaten yapılmış olanlar

Bildirim kanalı ekleyince "her 3 saniyede bir mesaj" korkusu haklıdır. Motor bunu engeller:

- Sınır **üst üste N ölçümde** aşılmadıkça alarm oluşmaz (varsayılan 3).
- Süren bir alarm en fazla **tekrar bildirim penceresi** kadar sıklıkta yeniden bildirilir
  (varsayılan 15 dk).
- Uyarı → kritik yükselmesi pencereyi delip geçer, çünkü bu gerçekten haberdir.
- **Servis yeniden başladığında süren alarmlar tekrar bildirilmez** — ölçüldü, 2026-08-05.

## Webhook gövdesi

```json
{
  "title": "🔴 Merkez SQL",
  "body": "İşlemci %97 — sınır %85",
  "isCleared": false,
  "raisedAtUtc": "2026-08-05T06:23:54Z",
  "moduleId": "mssql",
  "targetId": "019fcdbd257c7c2c8cd6b1b301f2bbfe",
  "targetName": "Merkez SQL",
  "groupName": "Acme Ltd.",
  "ruleId": "cpu",
  "ruleTitle": "İşlemci",
  "severity": "Critical",
  "value": 97,
  "threshold": 85,
  "unit": "%"
}
```

`format` alanına `slack` yazarsan Slack/Teams'in beklediği `{"text": "…"}` gövdesi gönderilir.

**İmza anahtarı** girersen gövdenin HMAC-SHA256 imzası `X-Signature` başlığında gider;
alıcı tarafta doğrulaman şu şekilde olur:

```python
import hmac, hashlib
expected = hmac.new(secret.encode(), body.encode(), hashlib.sha256).hexdigest()
if not hmac.compare_digest(expected, request.headers["X-Signature"]):
    abort(401)
```

## Yerel deneme

```bash
node tools/webhook-alici.mjs 9099        # basit alıcı
# Uygulamada webhook adresini http://localhost:9099/ yap ve "Test gönder"e bas
```

## Alarm geçmişi

Bildirim kaçsa bile kayıt kalır: **🔔 → Tüm alarm geçmişi**. Her satır ne zaman başladı, ne
kadar sürdü, bitti mi gösterir. Kayıtlar 90 gün saklanır; süren bir alarm yaşı ne olursa
olsun silinmez.

## Güvenlik

- Bot token'ı ve SMTP parolası Data Protection ile **şifreli** saklanır ve API'den asla
  geri dönmez — arayüz yalnız "kayıtlı" bilgisini görür.
- Anahtar halkası (`data/keys`) kaybolursa kanal sırları okunamaz; kanal kendini
  yapılandırılmamış sayar ve yeniden girmen gerekir.
- Alarm mesajları sunucu adı, müşteri adı ve ölçülen değeri içerir — sorgu metni **içermez**.
  Telegram'a giden içerik üçüncü taraf bir sunucudan geçer; bunu kabul etmiyorsan
  e-posta veya kendi webhook'unu kullan.
