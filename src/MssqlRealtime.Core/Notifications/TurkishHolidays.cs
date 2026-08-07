using System.Globalization;

namespace MssqlRealtime.Core.Notifications;

/// <summary>
/// Türkiye resmî tatilleri ve dinî bayramlar.
/// </summary>
/// <remarks>
/// Hazır kütüphane arandı: <c>Nager.Date</c> (22,8M indirme, MIT etiketli) denendi ve
/// çalışma anında <c>LicenseKeyException</c> attı — lisans anahtarı için GitHub sponsorluğu
/// istiyor (ölçüldü 2026-08-07). O yüzden elle hesaplanıyor.
/// <para>
/// Sabit tarihli tatiller kesindir. Dinî bayramlar ay takvimine bağlı olduğu için
/// <see cref="UmAlQuraCalendar"/> ile hesaplanır; Diyanet takvimiyle **bir gün** şaşabilir.
/// Bu yüzden kullanıcı kendi listesine gün ekleyip düzeltebiliyor.
/// </para>
/// </remarks>
public static class TurkishHolidays
{
    private static readonly UmAlQuraCalendar Hijri = new();

    public static IReadOnlyList<DateOnly> ForYear(int year)
    {
        var days = new List<DateOnly>
        {
            new(year, 1, 1),    // Yılbaşı
            new(year, 4, 23),   // Ulusal Egemenlik ve Çocuk Bayramı
            new(year, 5, 1),    // Emek ve Dayanışma Günü
            new(year, 5, 19),   // Atatürk'ü Anma, Gençlik ve Spor Bayramı
            new(year, 7, 15),   // Demokrasi ve Millî Birlik Günü
            new(year, 8, 30),   // Zafer Bayramı
            new(year, 10, 29)   // Cumhuriyet Bayramı
        };

        // Ramazan Bayramı: 1 Şevval (10. ay), 3 gün. Kurban: 10 Zilhicce (12. ay), 4 gün.
        days.AddRange(FromHijri(year, month: 10, day: 1, length: 3));
        days.AddRange(FromHijri(year, month: 12, day: 10, length: 4));

        return days.Distinct().OrderBy(d => d).ToList();
    }

    private static IEnumerable<DateOnly> FromHijri(int gregorianYear, int month, int day, int length)
    {
        // Bir miladi yıl iki hicri yıla yayılır; ikisini de deneyip yılın içine düşenleri alırız.
        var hijriYears = new[]
        {
            Hijri.GetYear(new DateTime(gregorianYear, 1, 1)),
            Hijri.GetYear(new DateTime(gregorianYear, 12, 31))
        }.Distinct();

        foreach (var hijriYear in hijriYears)
        {
            DateTime start;
            try
            {
                start = Hijri.ToDateTime(hijriYear, month, day, 0, 0, 0, 0);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Takvimin desteklediği aralığın dışı: o yıl için bayram üretmeyiz.
                continue;
            }

            for (var i = 0; i < length; i++)
            {
                var date = start.AddDays(i);
                if (date.Year == gregorianYear)
                {
                    yield return DateOnly.FromDateTime(date);
                }
            }
        }
    }

    public static bool IsHoliday(DateOnly date) => ForYear(date.Year).Contains(date);
}
