# Kırılma noktaları

> Ne bozulur · **bugün** ne olur · ne olmalı. "Bugün ne olur" sütunu ölçülmüştür; tahmin
> olanlar `❓` taşır.

## Bildirim ve alarm

| Ne bozulur | Bugün ne olur | Ne olmalı |
|---|---|---|
| **Uygulama kapalıyken eşik aşılır** | ✅ Çözüldü (v0.2.0): sunucu alarmı **Telegram / e-posta / webhook** ile kendisi gönderir. Ölçüldü 2026-08-05: hiçbir istemci bağlı değilken iki alarm webhook'a ulaştı. Uygulama içi bildirim hâlâ yalnız açıkken çalışır. | Kanal yapılandırılmamışsa hâlâ sessizdir — kurulumda en az bir kanal açılmalı (`docs/06-bildirimler.md`). |
| Telefon uykuda / ağ yok | Anlık push kaçar ama **kayıp değil**: alarm SQLite'a yazılır ve *Alarm geçmişi* ekranında görünür; kanal bildirimi zaten ayrı yoldan gitmiştir. | ✅ |
| Bildirim izni reddedilmiş | Alarm yalnız uygulama içi listede görünür; sessizce kaybolmaz. | ✅ Yeterli. Ayarlarda "bildirimler kapalı" uyarısı gösterilebilir. |
| Servis yeniden başlar | ✅ Çözüldü (v0.2.0): açık alarmlar SQLite'tan geri yüklenir, başlangıç saatleri korunur ve **yeniden bildirilmez**. Ölçüldü 2026-08-05: "Restored 2 alert(s)", sonrasında 0 yeni teslimat. | ✅ |

## Ölçüm doğruluğu

| Ne bozulur | Bugün ne olur | Ne olmalı |
|---|---|---|
| CPU değeri eski | Ring buffer dakikada bir yazılır. Değerin yaşı ölçülür, 90 sn'yi geçerse arayüzde ve alarm mesajında belirtilir. | ✅ Ölçülüp gösteriliyor. Daha tazesi için sunucuda agent gerekir. |
| SQL Server yeniden başlar | `dm_os_wait_stats` sıfırlanır → delta negatife düşer; prob bunu **algılar** ve baz çizgisini atar (yanlış devasa değer üretmez). | ✅ |
| İzleyen kullanıcıda `VIEW SERVER STATE` yok | Bağlantı testi bunu **kaydetmeden önce** yakalar ve gereken `GRANT` komutunu gösterir. Poller'da ilgili prob hata verir, snapshot'ın geri kalanı gelir. | ✅ |
| Prob bir sunucuda patlar | Yalnız o prob boş kalır; snapshot yayınlanır, hata `errorMessage` içinde taşınır. | ✅ |

## Bağlantı ve ölçek

| Ne bozulur | Bugün ne olur | Ne olmalı |
|---|---|---|
| Müşteri sunucusuna erişim yok (NAT/firewall) | Bu dağıtım modelinde oluşmaz: hub müşterinin kendi ağında çalışır. Uzaktan izlenen bir müşteri çıkarsa VPN gerekir ya da agent v0.6.0'dan geri alınır. | ❓ ölçülmedi |
| Çok sayıda sunucu | Her sunucu **bağımsız** döngüde; biri yavaşsa diğerini bekletmez. Yük tek sunucuyla ölçüldü, N sunucuda ölçülmedi ❓ | 50+ sunucuda ölçüp poller havuzu sınırlanmalı. |
| Bir müşteri SQL'i çok yavaş | `CommandTimeout` (varsayılan 15 sn) sonrası prob hata verir; döngü devam eder. | ✅ |
| SignalR yerine long-polling'e düşülür | nginx'te WebSocket upgrade yoksa **sessizce** olur; işlev aynı, mobil veri/pil tüketimi artar. | `deploy/nginx/README.md` içindeki 101 testi ile doğrula. |
| Aynı anda çok istemci | Snapshot her gruba ayrı gönderilir; istemci `(moduleId,targetId,sentAt)` ile tekilleştirir. Çok sayıda istemcide yük ölçülmedi ❓ | |

## Bildirim kanalları

| Ne bozulur | Bugün ne olur | Ne olmalı |
|---|---|---|
| Telegram/webhook erişilemez | Dağıtıcı hatayı loglar, **diğer kanallar etkilenmez**; alarm yine geçmişe yazılır. Yeniden deneme **yok** — o bildirim kaçar. | Kalıcı kuyruk + geri çekilmeli yeniden deneme. |
| Kanal yavaş (SMTP 10 sn) | Teslimat ayrı kuyrukta; poller **beklemez**. | ✅ |
| Kuyruk dolar (500) | En eski bildirim düşürülür, log'a uyarı yazılır. | Kalıcı kuyruk gerekirse artırılmalı. |
| Hiçbir kanal açık değil | Uygulama kapalıyken **kimse haber almaz** — sessiz başarısızlık. | Kurulum sonrası uyarı gösterilmeli ❓ |
| Bot token'ı sızar | Saldırgan yalnız o sohbete mesaj gönderebilir; izleme verisine erişemez. | Token'ı arayüzden değiştir. |

## Veri ve güvenlik

| Ne bozulur | Bugün ne olur | Ne olmalı |
|---|---|---|
| **Data Protection anahtar halkası kaybolur** | Kayıtlı **tüm SQL parolaları okunamaz** hale gelir; her sunucu için parola yeniden girilmeli. Hata mesajı bunu açıkça söyler. | `data/keys` yedeklenmeli — kurulum belgesinde yazılı. |
| SQLite dosyası bozulur | Profiller ve kullanıcı hesabı kaybolur; uygulama açılışta boş şema kurar. | Düzenli yedek (`data/` klasörü). |
| Uygulama portu internete açık kalır | Kimlik doğrulama var ama panel dışarıya açık; `X-Forwarded-For` taklidi mümkün hale gelir. | Güvenlik duvarı kuralı — C senaryosunda **şart**. |
| İki sunucu arası trafik düz HTTP | nginx ayrı sunucudaysa bearer token ve snapshot içeriği ağda **açık** akar. | Özel ağ ya da o bacakta da TLS. |
| Sorgu metninde hassas veri | İfade metni 4000 karakterde kesilir, **loglanmaz**, ama arayüzde görünür ve WebSocket üzerinden akar. | ✅ TLS altında kabul edilebilir; maskeleme gerekirse prob seviyesinde yapılmalı. |
| `KILL` yanlış oturuma basılır | Onay sorulur; `session_id ≤ 50` reddedilir; işlem `LogWarning` ile denetim kaydına yazılır. | Kullanıcı bazlı yetki ayrımı yok (tek kullanıcı var). |

## Sürüm uyumu

| Ne bozulur | Bugün ne olur | Ne olmalı |
|---|---|---|
| Sunucuda yeni modül var, telefon eski | Ana ekranda "bu araç sunucuda var ama uygulamada ekranı yok" diye **soluk** görünür, çökmez. | ✅ |
| Sunucuda yeni bildirim kanalı var, telefon eski | Ayar formu sunucudan gelen alan tanımlarıyla üretilir; yeni kanal **uygulama güncellemesi olmadan** görünür. | ✅ |
| Sunucu snapshot'a yeni alan ekler | Eski istemci bilmediği alanı yok sayar. | ✅ |
| Telefon eski uçları çağırır | Uç kaldırılırsa 404 → ekranda hata. | Uçlar kırılmadan önce sürümlenmeli. |
