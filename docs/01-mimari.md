# Mimari

## Karar: müşteri başına bir panel

Seçilen topoloji (2026-08-05):

```
Telefon / Masaüstü / Tarayıcı
            │  HTTPS + WSS (SignalR)
            ▼
   ┌─────────────────────┐
   │  Nginx Proxy Mgr    │  TLS, Let's Encrypt
   └──────────┬──────────┘
              │ http://<windows-ip>:5199
   ┌──────────▼──────────────────────────┐
   │  MÜŞTERİ WINDOWS MAKİNESİ           │
   │  ┌───────────────────────────────┐  │
   │  │ Panel (Windows servisi)       │  │  ProgramData: profiller,
   │  │ poller + hub + bildirim       │  │  kullanıcı, DP anahtarları
   │  └──────────────┬────────────────┘  │
   │                 │ TDS 1433, salt okunur DMV
   │  ┌──────────────▼────────────────┐  │
   │  │ SQL Server                    │  │
   │  └───────────────────────────────┘  │
   └─────────────────────────────────────┘

Her müşteride bu yapıdan bir tane. Telefon uygulaması panelleri kaydeder ve aralarında geçer.
```

**Şartı:** panelin izlenecek SQL Server'a erişebilmesi — genelde aynı makinede olduğu için
sorun olmaz. Panel müşterinin kendi ağında çalışır; dışarıdan erişim yalnız ters vekil
sunucu üzerinden, TLS ile olur.

Agent modu v0.4.0'da yazılmış, v0.7.0'da kaldırılmıştı: bu topolojide hiç devreye girmiyor.
Erişilemeyen bir müşteri çıkarsa git geçmişinden (`v0.6.0`) geri alınabilir.

## Katmanlar

| Proje | Sorumluluk | Neyi bilmez |
|---|---|---|
| `Core` | Modül sözleşmesi, alarm motoru, `Result<T>`, taşıma soyutlaması | SQL Server'ı, SignalR'ı, EF'i |
| `Infrastructure` | Identity, `DbContext`, Data Protection ile şifreleme | Hangi araçların olduğunu |
| `Modules.Mssql` | Problar, poller, MSSQL uçları, eşik kuralları | Host'u, SignalR'ı, `AppDbContext`'i |
| `Api` | Host: hub, Identity uçları, statik ön yüz, modül kaydı | Modüllerin içini |

Modül `AppDbContext`'i değil, `DbContext` tabanını enjekte eder; kendi tablosunu
`ConfigureDbModel` ile ekler. Böylece host modülleri, modüller de host'u tanımaz.

## Veri akışı

1. `MssqlPollingService` her sunucu için **bağımsız** bir döngü tutar (biri yavaşsa diğerini
   bekletmez). Profil listesi 15 saniyede bir yenilenir — telefondan eklenen sunucu restart
   gerektirmez.
2. `ServerPoller` bağlantıyı açar, probları `Order` sırasıyla çalıştırır. Pahalı problar
   `EveryNthPoll` ile seyreltilir; **ilk turda hepsi çalışır**.
3. Bir prob patlarsa snapshot yine üretilir; hata `ErrorMessage` içinde taşınır.
4. `MssqlAlertRules` eşikleri değerlendirir → `AlertCandidate` listesi.
5. `AlertEngine` neyin **haber** olduğuna karar verir (aşağıda).
6. Snapshot önbelleğe yazılır + hub'a gönderilir. Bildirim gerekiyorsa ayrı kanaldan gider.

Önbellek şart: uygulamayı yeni açan telefon, ilk push'u beklemeden dolu bir ekran görür.

## Alarm motoru — neden ayrı bir parça

Ham eşik karşılaştırması 3 satırdır; zor olan **ne zaman susulacağı**. Motor modülden
bağımsızdır, dolayısıyla sonradan eklenen her araç bunları yazmadan kazanır:

| Davranış | Sebep |
|---|---|
| Ardışık N ihlal şartı | 5 saniyelik bir CPU sıçraması telefonu uyandırmamalı |
| İhlal bitince sayaç sıfırlanır | Aralıklı ihlaller birikip yanlış alarm üretmesin |
| Tekrar bildirim penceresi (varsayılan 15 dk) | Süren bir sorun her 3 saniyede bir bildirim göndermesin |
| Şiddet artışı pencereyi delip geçer | Uyarı → Kritik gerçekten haberdir |
| "Normale döndü" yalnızca kullanıcıya bildirildiyse gönderilir | Hiç duyulmamış alarmın kapanışı gürültüdür |
| Hedef silinince durum unutulur | Yeniden eklenen sunucu temiz başlar |

## Gerçek zamanlı taşıma

Tek hub (`/hubs/tools`), üç grup türü:

- `alerts` — giriş yapan herkes; bildirim buradan
- `module:<id>` — bir aracın tüm hedefleri (özet ekranı)
- `target:<id>:<hedef>` — tek hedef (detay ekranı)

İstemci tarafında **tek bağlantı** tüm araçlarca paylaşılır. Yeniden bağlanmada grup üyeliği
sunucuda kaybolduğu için istemci abonelikleri kendisi tekrarlar (`#resubscribe`).

WebSocket el sıkışması `Authorization` başlığı taşıyamaz; token sorgu parametresiyle gider ve
host bunu **yalnızca hub yolunda** kabul eder (`BearerTokenOptions.Events.OnMessageReceived`).

## Kimlik

ASP.NET Core Identity + `MapIdentityApi` (bearer token). Elle JWT/parola hash'i yazılmadı.
Tek operatör hesabı açılışta seed edilir; `/api/auth/register` middleware ile 404'e düşürülür.

## Güvenlik sınırları

- SQL parolaları Data Protection ile şifreli saklanır; anahtar halkası
  `C:\ProgramData\SunucuIzleme\keys` altında —
  **yedekle**, kaybolursa parolalar okunamaz.
- Parola hiçbir DTO'da dönmez; yalnızca `hasPassword: true` bilgisi gider.
- İzlenen sunucuya tek yazma işlemi `KILL <spid>`; session id tamsayı olarak doğrulanır ve
  `≤ 50` reddedilir (sistem oturumları).
- Sorgu metinleri 4000 karakterde kesilir ve **loglanmaz** (parametre değerleri PII olabilir).
