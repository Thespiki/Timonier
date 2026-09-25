using System.Text.RegularExpressions;
using PcPilot.Core.Catalog;
using PcPilot.Core.Platform;
using PcPilot.Core.Security;

namespace PcPilot.Modules.Users;

/// <summary>
/// Conversion des plages horaires entre la grille locale (lundi → dimanche, heure locale) et le format SAM :
/// 168 bits (21 octets) en heure UTC, bit 0 de l'octet 0 = dimanche 0 h–1 h UTC.
/// </summary>
internal static partial class LogonHoursMap
{
    public const int Hours = 168;

    [GeneratedRegex("^[0-9A-Fa-f]{42}$")] private static partial Regex HexRx();

    /// <summary>Décalage actuel de l'heure locale par rapport à UTC, arrondi à l'heure inférieure.</summary>
    public static int CurrentOffsetHours() => (int)Math.Floor(TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalHours);

    public static bool OffsetHasMinutes() => TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).Minutes != 0;

    /// <summary>Index local : jour (0 = dimanche, comme DayOfWeek) × 24 + heure.</summary>
    public static int LocalIndex(DayOfWeek day, int hour) => (int)day * 24 + hour;

    /// <summary>Grille locale → bitmap UTC. <paramref name="offsetHours"/> = heure locale − UTC.</summary>
    public static byte[] ToUtcBitmap(bool[] local, int offsetHours)
    {
        var bytes = new byte[21];
        for (var l = 0; l < Hours; l++)
        {
            if (!local[l]) continue;
            var u = Mod(l - offsetHours);
            bytes[u / 8] |= (byte)(1 << (u % 8));
        }
        return bytes;
    }

    /// <summary>Bitmap UTC → grille locale (null = aucune restriction : tout est autorisé).</summary>
    public static bool[] FromUtcBitmap(byte[]? bytes, int offsetHours)
    {
        var local = new bool[Hours];
        for (var l = 0; l < Hours; l++)
        {
            if (bytes is null) { local[l] = true; continue; }
            var u = Mod(l - offsetHours);
            local[l] = (bytes[u / 8] & (1 << (u % 8))) != 0;
        }
        return local;
    }

    public static string ToHex(byte[] bytes) => Convert.ToHexString(bytes);

    public static byte[] ParseHex(string value)
    {
        if (!HexRx().IsMatch(value)) throw new ValidationException("Plages horaires invalides (42 caractères hexadécimaux attendus).");
        return Convert.FromHexString(value);
    }

    private static int Mod(int v) => ((v % Hours) + Hours) % Hours;
}

/// <summary>Enregistre les heures d'ouverture de session autorisées d'un compte standard (NetUserSetInfo niveau 1020).</summary>
public sealed class SetLogonHoursAction : IActionHandler
{
    public string Id => "users.logonhours.set";
    public string Title => "Plages horaires de connexion";
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        AccountParams.Sid(p);
        LogonHoursMap.ParseHex(Validate.Required(p, "hours", 42));
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var bitmap = LogonHoursMap.ParseHex(Validate.Required(p, "hours", 42));
        var (target, _) = LocalAccounts.Resolve(AccountParams.Sid(p), ctx.UserSid);
        LocalAccounts.RefuseSessionAccount(target, ctx.UserSid, "restreindre les horaires de");
        if (target.IsAdmin) throw new ValidationException("Les plages horaires sont réservées aux comptes standard : un administrateur pourrait les retirer lui-même.");
        if (target.IsBuiltIn) throw new ValidationException("Les comptes intégrés de Windows ne sont pas concernés.");

        NetApi.SetLogonHours(target.Name, bitmap);
        var allowed = bitmap.Sum(b => System.Numerics.BitOperations.PopCount(b));
        Log.Info("Users", $"plages horaires {target.Name} : {allowed} h/semaine");
        var msg = allowed switch
        {
            168 => $"« {target.Name} » peut de nouveau ouvrir une session à toute heure.",
            0 => $"« {target.Name} » ne peut plus ouvrir de session à aucun moment.",
            _ => $"Plages horaires de « {target.Name} » enregistrées ({allowed} h par semaine).",
        };
        return Task.FromResult(ActionResult.Ok(msg));
    }
}
