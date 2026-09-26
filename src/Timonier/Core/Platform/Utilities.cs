using System.Globalization;
using System.Management;

namespace Timonier.Core.Platform;

/// <summary>Requêtes WMI en lecture (délai borné). Ne jamais construire de WQL à partir de saisie utilisateur non validée.</summary>
public static class WmiQuery
{
    public static List<Dictionary<string, object?>> Query(string wql, string scope = @"root\cimv2", int timeoutSeconds = 15)
    {
        var rows = new List<Dictionary<string, object?>>();
        using var searcher = new ManagementObjectSearcher(scope, wql)
        {
            Options = { Timeout = TimeSpan.FromSeconds(timeoutSeconds), ReturnImmediately = true },
        };
        foreach (ManagementObject mo in searcher.Get())
        {
            using (mo)
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in mo.Properties) row[prop.Name] = prop.Value;
                rows.Add(row);
            }
        }
        return rows;
    }

    /// <summary>Échappe une valeur littérale pour une clause WHERE WQL entre apostrophes.</summary>
    public static string Escape(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
}

/// <summary>Formatage pour l'affichage, dans la langue et la culture de l'interface (<see cref="Localization.Loc.Culture"/>).</summary>
public static class Format
{
    private static CultureInfo Culture => Localization.Loc.Culture;

    public static string Bytes(long bytes)
    {
        string[] units = [LC("unit", "B"), LC("unit", "KB"), LC("unit", "MB"), LC("unit", "GB"), LC("unit", "TB")];
        double v = bytes;
        var i = 0;
        while (Math.Abs(v) >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return L("{0} {1}", v.ToString(i == 0 ? "0" : v < 10 ? "0.0" : "0", Culture), units[i]);
    }

    public static string Percent(double ratio) => ratio.ToString("P0", Culture);

    /// <summary>Date et heure (ex. « 3 mars 2026 à 14:05 »).</summary>
    public static string Date(DateTime d) => L("{0} at {1}", Day(d), d.ToString("t", Culture));

    /// <summary>Date longue sans le jour de la semaine, dans le format de la culture (ex. « 3 mars 2026 », « March 3, 2026 »).</summary>
    public static string Day(DateTime d) => d.ToString(LongDateWithoutWeekday(), Culture);

    private static string LongDateWithoutWeekday()
    {
        var pattern = Culture.DateTimeFormat.LongDatePattern;
        var cleaned = System.Text.RegularExpressions.Regex.Replace(pattern, @"\s*,?\s*dddd\s*,?\s*", " ").Trim(' ', ',');
        return cleaned.Length > 0 ? cleaned : "D";
    }

    public static string Duration(TimeSpan t) =>
        t.TotalDays >= 1 ? L("{0} d {1} h", (int)t.TotalDays, t.Hours)
        : t.TotalHours >= 1 ? L("{0} h {1} min", (int)t.TotalHours, t.Minutes)
        : t.TotalMinutes >= 1 ? L("{0} min", (int)t.TotalMinutes)
        : L("{0} s", Math.Max(0, (int)t.TotalSeconds));

    /// <summary>« il y a 3 j 2 h », « à l'instant »…</summary>
    public static string Ago(DateTime past)
    {
        var d = DateTime.Now - past;
        return d.TotalMinutes < 1 ? L("just now") : L("{0} ago", Duration(d));
    }
}
