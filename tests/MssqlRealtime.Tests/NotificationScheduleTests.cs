using MssqlRealtime.Core.Notifications;

namespace MssqlRealtime.Tests;

/// <summary>
/// Sessiz saatlerin kendisi. Yanlış olduğunda ya gece 03:00'te telefon çalar ya da gündüz
/// duyulması gereken alarm sessiz düşer.
/// </summary>
public class NotificationScheduleTests
{
    private static bool NoHolidays(DateOnly _) => false;

    // 2026-08-08 Cumartesi 02:35 — gerçekte yaşanan olay.
    private static readonly DateTimeOffset SaturdayNight = new(2026, 8, 8, 2, 35, 0, TimeSpan.FromHours(3));
    private static readonly DateTimeOffset WednesdayNoon = new(2026, 8, 5, 12, 0, 0, TimeSpan.FromHours(3));

    /// <summary>
    /// Ölçüldü 2026-08-08 02:35: varsayılan kapalıyken ayar hiç devreye girmiyordu — yazılmış,
    /// belgelenmiş ve üç gün boyunca tek bir mesajı sessizleştirmemişti.
    /// </summary>
    [Fact]
    public void DefaultsToEnabled()
    {
        Assert.True(new NotificationSchedule().Enabled);
    }

    [Fact]
    public void NightOutsideWorkHoursIsQuietOutOfTheBox()
    {
        Assert.True(new NotificationSchedule().IsQuietAt(SaturdayNight, isCritical: false, NoHolidays));
    }

    /// <summary>Gece uyandırmanın karşılığı yoksa uyandırmak zarardır — kritikler de sessiz.</summary>
    [Fact]
    public void CriticalIsAlsoQuietUnlessAskedOtherwise()
    {
        Assert.True(new NotificationSchedule().IsQuietAt(SaturdayNight, isCritical: true, NoHolidays));

        var loud = new NotificationSchedule { CriticalAlwaysLoud = true };
        Assert.False(loud.IsQuietAt(SaturdayNight, isCritical: true, NoHolidays));
        Assert.True(loud.IsQuietAt(SaturdayNight, isCritical: false, NoHolidays));
    }

    [Fact]
    public void WorkHoursAreLoud()
    {
        Assert.False(new NotificationSchedule().IsQuietAt(WednesdayNoon, isCritical: false, NoHolidays));
    }

    /// <summary>Elle kapatan kurulum etkilenmez: kayıtlı değer varsayılanı yener.</summary>
    [Fact]
    public void DisabledScheduleIsNeverQuiet()
    {
        var off = new NotificationSchedule { Enabled = false };
        Assert.False(off.IsQuietAt(SaturdayNight, isCritical: false, NoHolidays));
    }

    [Fact]
    public void HolidayIsQuietAtAnyHour()
    {
        var schedule = new NotificationSchedule();
        Assert.True(schedule.IsQuietAt(WednesdayNoon, isCritical: false, _ => true));
    }

    /// <summary>
    /// <c>Start</c>/<c>End</c> <b>mesai</b> aralığıdır, sessiz aralık değil. 22:00–06:00
    /// girildiğinde gece vardiyası tanımlanmış olur: sessiz olan öğlendir, gece değil.
    /// </summary>
    [Fact]
    public void NightShiftInvertsWhichHoursAreQuiet()
    {
        var nightShift = new NotificationSchedule { Start = new TimeOnly(22, 0), End = new TimeOnly(6, 0) };

        // Cumartesi çalışma günü değil, o yüzden gün içi bir Çarşamba ile ölçülür.
        Assert.True(nightShift.IsQuietAt(WednesdayNoon, isCritical: false, NoHolidays));
        Assert.False(nightShift.IsQuietAt(
            new DateTimeOffset(2026, 8, 5, 23, 30, 0, TimeSpan.FromHours(3)), isCritical: false, NoHolidays));
    }
}
