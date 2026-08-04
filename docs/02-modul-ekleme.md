# Yeni araç (tool) ekleme

> Bu belge ürünün en önemli sözleşmesidir: **yeni bir araç eklemek, var olan hiçbir dosyayı
> değiştirmemeyi gerektirir** — host'ta tek satır kayıt dışında.

Bir araç iki parçadan oluşur:

| Parça | Yer | Ne sağlar |
|---|---|---|
| Backend modülü | `src/MssqlRealtime.Modules.<Ad>/` | Veri modeli, arka plan işi, HTTP uçları, alarm kuralları |
| Ön yüz modülü | `app/src/lib/modules/<id>/` | Ekranlar |

Platformdan **bedava** gelenler: kimlik doğrulama, çoklu hedef yönetimi, şifreli sır saklama,
SignalR taşıma, alarm motoru (ardışık ihlal + tekrar bastırma), telefon bildirimi, araç
listesi ekranı.

---

## 1. Backend modülü

```bash
dotnet new classlib -n MssqlRealtime.Modules.Disk -o src/MssqlRealtime.Modules.Disk -f net10.0
dotnet sln MssqlRealtime.slnx add src/MssqlRealtime.Modules.Disk
dotnet add src/MssqlRealtime.Modules.Disk reference src/MssqlRealtime.Core
dotnet add src/MssqlRealtime.Api reference src/MssqlRealtime.Modules.Disk
```

`csproj`'a ASP.NET referansı ekle (uç eşlemek için):

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

### Modül sınıfı

```csharp
public sealed class DiskModule : IToolModule
{
    public const string ModuleId = "disk";

    public string Id => ModuleId;
    public string Title => "Disk İzleme";
    public string Icon => "💽";
    public int Order => 20;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDiskProbe, SmartProbe>();
        services.AddHostedService<DiskPollingService>();
    }

    // Modülün kendi tablosu — host'un DbContext'i buna dokunmaz.
    public void ConfigureDbModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<DiskTarget>(e => e.ToTable("DiskTargets").HasKey(x => x.Id));

    // /api/modules/disk/... altına eşlenir, yetkilendirme zaten uygulanmıştır.
    public void MapEndpoints(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/targets", async (IDiskTargetStore store, CancellationToken ct) =>
            Results.Ok(await store.GetAllAsync(ct)));
    }

    public ToolDescriptor Describe() => new()
    {
        Id = Id, Title = Title, Icon = Icon, Order = Order, Version = "1.0.0",
        Description = "Disk doluluk ve S.M.A.R.T. durumu",
        Capabilities = ["targets", "alerts", "realtime"]
    };
}
```

### Host'ta kayıt — değiştirilecek **tek** satır

`src/MssqlRealtime.Api/Program.cs`:

```csharp
builder.Services.AddToolModule<MssqlModule>(builder.Configuration);
builder.Services.AddToolModule<DiskModule>(builder.Configuration);   // ← yeni
```

### Veri yayınlama

Arka plan servisinden `IRealtimePublisher` ile:

```csharp
await publisher.PublishAsync(DiskModule.ModuleId, targetId, "snapshot", snapshot, ct);
```

İstemci `moduleEvent` olayında `moduleId` ile süzer. SignalR'a doğrudan bağımlılık yok.

### Alarm üretme

Modül yalnızca **kuralın ihlal edilip edilmediğini** söyler; ne zaman bildirim gideceğine
motor karar verir:

```csharp
var candidates = new List<AlertCandidate>
{
    new()
    {
        RuleId = "disk-full",
        RuleTitle = "Disk doluluk",
        IsBreached = usedPercent >= target.DiskAlertPercent,
        Severity = usedPercent >= 95 ? Severity.Critical : Severity.Warning,
        Message = $"C: %{usedPercent:0} dolu — sınır %{target.DiskAlertPercent}",
        Value = usedPercent,
        Threshold = target.DiskAlertPercent,
        Unit = "%",
        RequiredConsecutiveBreaches = target.AlertConsecutiveBreaches,
        RenotifyMinutes = target.AlertRenotifyMinutes
    }
};

var outcome = alertEngine.Evaluate(target, candidates, DateTimeOffset.UtcNow);
foreach (var notification in outcome.ToNotify)
    await publisher.PublishAlertAsync(notification, ct);
```

🔴 **Kural ihlal edilmese bile listeye ekle.** Motor bir alarmı ancak "artık ihlal yok"
bilgisini görürse kapatabilir ve kullanıcıya "normale döndü" diyebilir. İhlal yoksa kuralı
listeden çıkarmak, alarmı sonsuza kadar açık bırakır.

⚠️ **Ölçemediğin şeyi "normal" olarak bildirme.** Sunucuya erişilemiyorsa veya prob hata
verdiyse o kuralı hiç ekleme — `IsBreached = false` göndermek, gerçekte devam eden bir sorunu
"düzeldi" diye kapatır. MSSQL modülü tam bunu yapar: çevrimdışıyken yalnızca `offline`
kuralını üretir.

---

## 2. Ön yüz modülü

`app/src/lib/modules/disk/index.ts`:

```ts
import type { UiModule } from '../registry';
import DiskHome from './DiskHome.svelte';
import DiskTarget from './DiskTarget.svelte';

export const diskModule: UiModule = {
  id: 'disk',              // backend'deki IToolModule.Id ile aynı olmak zorunda
  home: DiskHome,
  target: DiskTarget
};
```

`app/src/lib/modules/registry.ts` içine ekle:

```ts
const modules: UiModule[] = [mssqlModule, diskModule];
```

Rota gerekmez: `/m/disk` ve `/m/disk/<hedef>` zaten dinamik olarak çözülür.

Canlı veriye abone olma kalıbı (`store.svelte.ts`):

```ts
realtime.onEvent((event) => {
  if (event.moduleId !== 'disk' || event.event !== 'snapshot') return;
  // ...
});
await realtime.subscribeModule('disk');
```

Tek bir hub bağlantısı tüm araçlar tarafından paylaşılır — modül kendi soketini **açmaz**.

---

## 3. Kontrol listesi

- [ ] `Id` backend ve ön yüzde birebir aynı
- [ ] Kurallar ihlal edilmese de her turda raporlanıyor
- [ ] Ölçülemeyen değer için kural üretilmiyor
- [ ] Sırlar `ISecretProtector` ile şifreleniyor, DTO'da **asla** dönmüyor
- [ ] Pahalı sorgular `EveryNthPoll` ile seyreltiliyor
- [ ] Bir prob patlarsa snapshot yine üretiliyor (hata `AddProbeError` ile)
- [ ] `dotnet build` **ve** `dotnet test` yeşil
- [ ] Eski uygulama sürümü yeni modülü görünce çökmüyor (ana ekranda "ekranı yok" der)
