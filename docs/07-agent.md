# Agent — NAT arkasındaki müşteri sunucuları

## Sorun

Merkezi sunucu, müşterinin SQL Server'ının 1433 portuna ulaşabilmek zorundadır. Gerçekte
çoğu müşteride bu **mümkün değildir**: NAT arkasında, güvenlik duvarı kapalı, sabit IP yok.
"Bize port açar mısınız" demek çoğu zaman cevapsız kalır — haklı olarak.

## Çözüm: bağlantı yönünü ters çevir

```
   Müşteri sunucusu                       Senin sunucun
┌────────────────────┐                 ┌──────────────────┐
│  SQL Server        │◀── localhost ───│                  │
│  MssqlRealtime     │                 │                  │
│  .Agent (servis)   │══ dışa doğru ══▶│  Merkezi hub     │
└────────────────────┘   HTTPS/WSS     └──────────────────┘
   hiçbir port açılmaz                    alarm · bildirim · UI
```

Agent müşteri sunucusunda çalışır, **dışarı doğru** bağlanır, SQL Server'ı yerelden ölçer
ve sonucu yukarı gönderir. Müşteri güvenlik duvarında hiçbir şey açılmaz — giden 443
zaten açıktır.

## Neyi kim yapar

| | Agent | Hub |
|---|---|---|
| SQL'e bağlanmak | ✅ | — |
| Ölçmek (problar) | ✅ | (kendi sunucuları için) |
| **Eşiği uygulamak** | ❌ | ✅ |
| Alarm üretmek / bastırmak | ❌ | ✅ |
| Bildirim göndermek | ❌ | ✅ |
| Geçmişi tutmak | ❌ | ✅ |

🔴 **Agent hiçbir şeye karar vermez.** Ölçer ve gönderir. Eşikler, alarm mantığı ve bildirim
merkezde kalır — böylece agent üzerinden izlenen bir sunucu, doğrudan izlenen bir sunucuyla
**aynı** sonuçları verir; ve eski ya da ele geçirilmiş bir agent bir alarmı bastıramaz.

Problar da paylaşılır: agent, hub'ın kullandığı **aynı** `ISqlProbe` sınıflarını çalıştırır.

## Kurulum

### 1. Merkezde agent oluştur

Uygulamada **Yönetim → Agent'lar → Yeni agent**. Bir kayıt anahtarı üretilir ve
**yalnız bir kez** gösterilir (sunucuda hash'i saklanır).

### 2. Agent'ı müşteri sunucusuna kur

```powershell
# Yayınla (kendi makinende)
dotnet publish src\MssqlRealtime.Agent -c Release -r win-x64 --self-contained false -o .\agent-publish

# Müşteri sunucusuna kopyala, ör. C:\MssqlRealtimeAgent
```

`appsettings.json` içine merkezin adresini ve anahtarı yaz:

```json
{
  "Agent": {
    "HubUrl": "https://izleme.example.com",
    "EnrollmentKey": "buraya-kayit-anahtari"
  }
}
```

Windows servisi olarak kur:

```powershell
sc.exe create MssqlRealtimeAgent binPath= "C:\MssqlRealtimeAgent\MssqlRealtime.Agent.exe" start= auto
sc.exe description MssqlRealtimeAgent "Sunucu izleme agent'i"
sc.exe start MssqlRealtimeAgent
```

Linux için `deploy/systemd/` içindeki unit dosyası örnek alınabilir.

### 3. Sunucuyu agent'a ata

MSSQL aracında sunucu ayarlarında **agent** seç. Atama anında agent'a iletilir — yeniden
başlatmaya gerek yok. Agent'ı boşa çıkarmak için atamayı kaldır: sunucu merkezin kendi
poller'ına geri döner.

## Kimlik ve gizlilik

- Agent'ın tek kimliği **kayıt anahtarıdır** (32 bayt rastgele). Sunucuda SHA-256 hash'i
  saklanır; kaybedilirse yenisi üretilir (*Yeni anahtar*), eski anında geçersiz olur.
- SQL parolası agent'ın **diskine yazılmaz**. Hub'dan TLS bağlantısı üzerinden gelir ve
  yalnız bellekte durur — agent kurulu makine sonradan elden çıkarsa geriye sır kalmaz.
- Agent'ın kendi `appsettings.json` dosyasında yalnız hub adresi ve kayıt anahtarı bulunur.
- 🔴 **HubUrl mutlaka `https://` olmalı.** Düz HTTP kullanırsan hem kayıt anahtarı hem SQL
  parolası ağda açık akar.

## Bağlantı koptuğunda

- Agent sonsuza kadar yeniden bağlanmayı dener (0/2/5/10/30/60 sn).
- Bağlantı yokken **ölçüm biriktirilmez**: bir snapshot anlık fotoğraftır, beş dakika
  öncesinin "canlı" diye gösterilmesi yanıltıcı olur.
- Hub, agent'ı 2 dakika sessiz kalınca *bağlı değil* gösterir.
- ⚠️ Agent çevrimdışıyken o sunucu için **alarm üretilmez** — ölçüm gelmiyor demek, sorun yok
  demek değildir. Bu bugünkü davranıştır ve `docs/04-kirilma-noktalari.md` içinde kayıtlıdır.

## Sürüm uyumu

`AgentProtocol.ProtocolVersion` uyuşmazsa hub kaydı **reddeder** ve agent'ın güncellenmesi
gerektiğini söyler. Sessizce yanlış veri göndermektense bağlanmaması tercih edildi.

## Doğrulama

```bash
# Agent tarafında
journalctl -u mssqlrealtime-agent -f     # veya Windows'ta olay günlüğü / logs\agent-*.log

# Beklenen:
#   Hub'a bağlanılıyor: https://izleme.example.com/hubs/agent
#   Kayıt başarılı: <agent adı> (N sunucu atanmış)
#   İzleme başladı: <sunucu> (host:1433) her N sn
```

Merkezde **Agent'lar** ekranında yeşil nokta ve makine adı görünür; MSSQL aracında o sunucu
normal şekilde canlı akar.
