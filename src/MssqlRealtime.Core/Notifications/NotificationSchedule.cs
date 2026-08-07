namespace MssqlRealtime.Core.Notifications;

/// <summary>
/// Ne zaman telefonun titreyeceğini belirler.
/// </summary>
/// <remarks>
/// Mesai dışında bildirimi <b>kesmek</b> yerine <b>sessiz</b> göndeririz. Kesmek, gelmeyen
/// alarm demektir ve bir izleme panelinin yapabileceği en kötü şeydir; sessiz göndermek ise
/// mesajı ve geçmişi eksiltmeden yalnız zili kapatır. Kullanıcının kendi ifadesiyle:
/// "gece uyanırsam uykusuzluktan zaten kimse bakamaz."
/// </remarks>
public sealed record NotificationSchedule
{
    /// <summary>
    /// Varsayılan <c>true</c>. Ölçüldü 2026-08-08 02:35: kapalı varsayılanla kimse ayarı
    /// açmıyor ve gece 02:35'te gelen alarm telefonu sesli çaldırıyor — özellik yazılmış
    /// ama hiç devreye girmemiş oluyor. Sessizlik zaten alarmı kesmiyor, yalnız zili
    /// kapatıyor; bu yüzden açık gelmesinin bir bedeli yok.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Çalışma günleri. Boşsa hafta içi kabul edilir.</summary>
    public IReadOnlyList<DayOfWeek> WorkDays { get; init; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public TimeOnly Start { get; init; } = new(8, 30);
    public TimeOnly End { get; init; } = new(18, 0);

    /// <summary>Resmî tatiller ve bayramlar da mesai dışı sayılsın mı?</summary>
    public bool QuietOnHolidays { get; init; } = true;

    /// <summary>Kullanıcının elle eklediği günler (idari izin, şirket tatili, düzeltme).</summary>
    public IReadOnlyList<DateOnly> ExtraHolidays { get; init; } = [];

    /// <summary>
    /// Kritik alarmlar mesai dışında da sesli gitsin mi? Varsayılan <c>false</c>: gece
    /// uyandırmanın bir karşılığı yoksa uyandırmak zarardır.
    /// </summary>
    public bool CriticalAlwaysLoud { get; init; }

    /// <summary>Sessiz mi göndermeli? Kapalıysa her şey normal (sesli) gider.</summary>
    public bool IsQuietAt(DateTimeOffset localTime, bool isCritical, Func<DateOnly, bool> isHoliday)
    {
        if (!Enabled)
        {
            return false;
        }

        if (isCritical && CriticalAlwaysLoud)
        {
            return false;
        }

        var date = DateOnly.FromDateTime(localTime.DateTime);

        if (QuietOnHolidays && (isHoliday(date) || ExtraHolidays.Contains(date)))
        {
            return true;
        }

        var days = WorkDays.Count == 0
            ? new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
            : [.. WorkDays];

        if (!days.Contains(localTime.DayOfWeek))
        {
            return true;
        }

        var now = TimeOnly.FromDateTime(localTime.DateTime);

        // Gece yarısını aşan aralık (ör. 22:00–06:00) da desteklenir.
        return Start <= End
            ? now < Start || now >= End
            : now < Start && now >= End;
    }
}
