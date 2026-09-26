using System.Text.RegularExpressions;
using Timonier.Core.Catalog;
using Timonier.Core.Platform;
using Timonier.Core.Security;

namespace Timonier.Modules.Users;

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
        if (!HexRx().IsMatch(value)) throw new ValidationException(L("Invalid time slots (42 hexadecimal characters expected)."));
        return Convert.FromHexString(value);
    }

    private static int Mod(int v) => ((v % Hours) + Hours) % Hours;
}

/// <summary>Enregistre les heures d'ouverture de session autorisées d'un compte standard (NetUserSetInfo niveau 1020).</summary>
public sealed class SetLogonHoursAction : IActionHandler
{
    public string Id => "users.logonhours.set";
    public string Title => L("Sign-in hours");
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
        LocalAccounts.RefuseSessionAccount(target, ctx.UserSid, L("For security, Timonier refuses to restrict the hours of the account you're signed in with."));
        if (target.IsAdmin) throw new ValidationException(L("Time slots are reserved for standard accounts: an administrator could remove them themselves."));
        if (target.IsBuiltIn) throw new ValidationException(L("Windows built-in accounts aren't affected."));

        NetApi.SetLogonHours(target.Name, bitmap);
        var allowed = bitmap.Sum(b => System.Numerics.BitOperations.PopCount(b));
        Log.Info("Users", $"plages horaires {target.Name} : {allowed} h/semaine");
        var msg = allowed switch
        {
            168 => L("“{0}” can sign in at any time again.", target.Name),
            0 => L("“{0}” can no longer sign in at any time.", target.Name),
            _ => LP(allowed, "Time slots for “{1}” saved ({0} hour per week).", "Time slots for “{1}” saved ({0} hours per week).", target.Name),
        };
        return Task.FromResult(ActionResult.Ok(msg));
    }
}
