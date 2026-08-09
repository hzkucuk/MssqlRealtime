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
| Bir DMV join'i satır çoğaltır | Snapshot'ta mükerrer `SessionId` olursa ön yüzdeki anahtarlı `{#each}` `each_key_duplicate` fırlatır ve **sekmenin tamamı çizilmez** — üretim derlemesinde de. Ekranda önceki sekmenin DOM'u kalır, hiçbir hata mesajı görünmez. (Ölçüldü 2026-08-09 16:41: gerçek sunucuda eski sorgu 12 oturumu **24 satıra** çoğaltıyor, yenisi 12 satır veriyor.) | ◐ Kaynakta düzeltildi: `dm_exec_connections` artık `OUTER APPLY … TOP 1` ile okunuyor, `RequestsProbe` `request_id`, `BlockingProbe` `BlockedRequestId` taşıyor; üç guard testi sorgu şeklini sabitliyor. Ön yüz hâlâ tek bir mükerrer anahtara karşı **kırılgan**: yeni bir 1:N join aynı sessiz çökmeyi geri getirir. |

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
| Panelin saat dilimi Türkiye değil | Sessiz pencere **kayar**: sessizlik `DateTimeOffset.Now` ile, yani Windows makinenin yerel saatiyle hesaplanır. Hata verilmez. | Zamanlamaya saat dilimi alanı ❓ |
| Kullanıcı sessiz saatleri kapatır | Bütün alarmlar 7/24 sesli. Ölçüldü 2026-08-08 02:35: varsayılan kapalıyken kimse açmıyor — bu yüzden v0.18.4'te varsayılan **açık** yapıldı. | ✅ |
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
| **Makinede sıradan bir yerel kullanıcı hesabı var** | Veri klasörünü (`C:\SunucuIzleme\data`) okuyabilir; içindeki veri koruma anahtarlarıyla kayıtlı SQL parolaları çözülebilir (ölçüldü 2026-08-06 18:05, o zamanki yol `ProgramData` idi). v0.12.1'de izinler daraltıldı, **v0.12.5'te geri alındı**: daraltma servisin kendi veritabanını açmasını engelledi ve üç sürüm boyunca kurulumu kırdı. | **Yapılmadı.** Doğru çözüm ölçülmeden tekrar denenmeyecek. Yönetici parolası artık registry'de tutulmuyor (v0.12.2). |
| Servis LocalSystem olarak çalışıyor | Uygulamada uzaktan kod çalıştırma olursa makinenin tamamı gider. | Sanal servis hesabına (`NT SERVICE\SunucuIzleme`) geçilmeli — **yapılmadı**, acil değil. Yazıldı ve geri alındı: Windows'ta doğrulanmadan gönderilemez. |
| Sahte `X-Forwarded-For` | ✅ v0.12.2'de kapandı: başlık yalnız loopback'ten ve kurulumda girilen vekil IP'sinden kabul ediliyor. Ölçüldü — LAN'dan 14 denemede `429` geldi (öncesinde 12 denemede hiç gelmiyordu). | ✅ |
| Güvenlik başlıkları | ✅ v0.12.2'de eklendi: `X-Frame-Options: DENY`, `X-Content-Type-Options`, `Referrer-Policy`, CSP `frame-ancestors 'none'`. Tam CSP yazılmadı — tarayıcı istemcisi hangi panele bağlanacağı önceden bilinmediği için `connect-src` sayılamıyor. | ✅ |

## Arayüz ve form taslakları

| Ne bozulur | Bugün ne olur | Ne olmalı |
|---|---|---|
| **Panel (müşteri) değiştirilir** | Yeni hub'a bağlanılır, eski soket kapatılır, modül store'ları ve alarm listesi temizlenir. v0.18.6 öncesinde **eski müşterinin hub'ında kalınıyordu**: header çoğu zaman eski adı yazıyor, gösterge "canlı" diyor, sayılar bıraktığın panelden geliyordu (ölçüldü 2026-08-09 17:50, gerçek hub ve tarayıcıyla). | ✅ Ölçüldü. Panel değişimi tek bir yerden (`enterActivePanel`) geçiyor; yeni bir store eklenirse **oraya da eklenmeli** — unutulursa aynı sınıf geri gelir. |
| Uygulama hub'a ulaşamıyor | Bağlantı göstergesi **"bağlı değil"** der. Yan etkisi olarak üst çubuktaki sürüm rozeti ve sunucu sayfasındaki firma adı da kaybolur — ikisi de gelen veriye bağlı. Bunlar ayrı arıza değil, aynı arızanın belirtisi. Sürüm bağlantı geri gelince yeniden sorulur (v0.18.6 öncesi sorulmuyordu). | ✅ Gösterge doğruyu söylüyor. Belirtilerin tek sebebe bağlı olduğu ekranda yazmıyor ❓ |
| Sayfa yenilenir / sunucu formu reddeder | Yazılanlar `sessionStorage`'daki taslaktan geri gelir ve kullanıcıya geri yüklendiği söylenir. Parola taslağa **yazılmaz**. | ✅ Ölçüldü 2026-08-09 17:03, tarayıcıda. |
| Form yalnızca açılır, hiçbir şey yazılmaz | Taslak **yazılmaz**. Öncesinde yazılıyordu: ikinci girişte "yarım kalan form geri yüklendi" uyarısı çıkıyor, üstelik bayat taslak sunucudan gelen profilin üstüne biniyordu (ölçüldü 2026-08-09 17:03, v0.18.6'da düzeltildi). | ✅ |
| Profil başka bir cihazdan değişir | Taslak yalnız gerçekten değiştirilmiş formda saklandığı için ekranda güncel profil görünür. | ✅ |
| Taslak biçimi sürümle değişir | Bozuk JSON yakalanır ve taslak silinir; alan eksik/fazlaysa taslak gerçek sayılıp geri yüklenir. | ❓ Sürümlü taslak anahtarı düşünülebilir. |
| Ön yüz davranışı bozulur | **Otomatik test yok.** `npm run check` yalnız tipleri görür; taslak/anahtar gibi davranışlar ancak elle ya da geçici düzenekle ölçülüyor. | Ön yüze test altyapısı (vitest + Playwright) — **yapılmadı**, karar bekliyor. |

## Güncelleme

| Ne bozulur | Bugün ne olur | Ne olmalı |
|---|---|---|
| Kurulum sırasında servis ayakta | Servis **dosyalara dokunulmadan önce** durdurulur ve gerçekten durana kadar (90 sn) beklenir; süreç de beklenir. Öncesinde `sc stop` gönderilip 1,5 sn uyunuyordu ve kurulum `DeleteFile ... kod 5` ile duruyordu (ölçüldü 2026-08-09, Windows 11). | ✅ Kaynakta düzeltildi (v0.19.1), **Windows'ta denenmedi**. |
| Yükseltme veri klasörünü değiştirir | Değiştirmez: veritabanı eski yerleşimdeyse (`ProgramData\SunucuIzleme`) servis oraya yönlendirilir. Öncesinde her zaman `{app}\data` veriliyordu ve panel **"henüz izlenen sunucu yok"** diyordu — veri duruyordu, bakılan yer değişmişti (ölçüldü 2026-08-09). | ✅ v0.19.1. Verinin gerçek yeri `C:\SunucuIzleme\data`; yedek alan oraya baksın. |
| İndirilen kurulum dosyası bozuk/değiştirilmiş | sha256, GitHub'ın verdiği özetle karşılaştırılır; tutmazsa dosya **silinir** ve kurulum hiç başlamaz. Özeti olmayan varlık kurulmaz. | ✅ Paket imzalı değil; tek koruma TLS + özet. Kod imzalama sertifikası alınırsa eklenmeli ❓ |
| Yeni sürüm açılmıyor | Yükseltici `/api/health` cevabını 180 sn bekler, gelmezse **çalışan sürümün kurulum dosyasıyla geri döner**. Her adım `logs/guncelleme-*.log` dosyasına yazılır. | ⚠️ **Windows'ta hiç denenmedi** (v0.19.0, 2026-08-09). VM'de bir kez koşturulmadan güvenilmemeli. |
| Çalışan sürümün release'i yok | Otomatik geri dönüş **yapılamaz**. Arayüz bunu güncellemeden **önce** söyler (`⚠ geri dönüş paketi yok`) ve onay metni uyarır. | ✅ Gizlenmiyor. |
| Güncelleme sırasında izleme durur | Servis `sc stop` + `sc delete` ile kaldırılıp yeniden kurulduğu için birkaç dakika ölçüm yapılmaz; onay metni bunu açıkça söyler. | ◐ Bu boşluk bildirim olarak da gitmiyor — sessizlik alarmı tetiklenebilir ❓ |
| GitHub erişilemez | "Sürüm listesine ulaşılamadı" denir. **"Güncelleme yok" ile karıştırılmaz.** | ✅ |
| Depo ele geçirilir | Panel oradan indirdiği kurulumu çalıştırır — güncelleme zincirinin güven kökü GitHub hesabıdır. | Depo hesabında 2FA şart; sürüm yayınlama yetkisi dar tutulmalı. |

## Sürüm uyumu

| Ne bozulur | Bugün ne olur | Ne olmalı |
|---|---|---|
| Sunucuda yeni modül var, telefon eski | Ana ekranda "bu araç sunucuda var ama uygulamada ekranı yok" diye **soluk** görünür, çökmez. | ✅ |
| Sunucuda yeni bildirim kanalı var, telefon eski | Ayar formu sunucudan gelen alan tanımlarıyla üretilir; yeni kanal **uygulama güncellemesi olmadan** görünür. | ✅ |
| Sunucu snapshot'a yeni alan ekler | Eski istemci bilmediği alanı yok sayar. | ✅ |
| Telefon eski uçları çağırır | Uç kaldırılırsa 404 → ekranda hata. | Uçlar kırılmadan önce sürümlenmeli. |
