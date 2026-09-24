using System.Globalization;
using System.Management;

namespace PcPilot.Core.Platform;

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

/// <summary>Formatage pour l'affichage (français).</summary>
public static class Format
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static string Bytes(long bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        double v = bytes;
        var i = 0;
        while (Math.Abs(v) >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return v.ToString(i == 0 ? "0" : v < 10 ? "0.0" : "0", Fr) + " " + units[i];
    }

    public static string Percent(double ratio) => (ratio * 100).ToString("0", Fr) + " %";

    public static string Date(DateTime d) => d.ToString("d MMMM yyyy à HH:mm", Fr);

    public static string Duration(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays} j {t.Hours} h"
        : t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min"
        : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min"
        : $"{Math.Max(0, (int)t.TotalSeconds)} s";

    /// <summary>« il y a 3 jours », « il y a 2 h »…</summary>
    public static string Ago(DateTime past)
    {
        var d = DateTime.Now - past;
        return d.TotalMinutes < 1 ? "à l'instant" : "il y a " + Duration(d);
    }
}
