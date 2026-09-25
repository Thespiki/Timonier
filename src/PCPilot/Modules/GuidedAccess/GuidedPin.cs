using PcPilot.Core.Security;
using PcPilot.Core.Settings;

namespace PcPilot.Modules.GuidedAccess;

/// <summary>
/// Code de sortie de l'accès guidé : haché (PBKDF2, <see cref="PinHasher"/>) dans les préférences, jamais en clair.
/// Les vérifications sont limitées en fréquence après plusieurs erreurs.
/// </summary>
internal static class GuidedPin
{
    public const int MinLength = 4;
    public const int MaxLength = 12;

    private static int _failures;
    private static DateTime _lockedUntil = DateTime.MinValue;

    public static bool IsSet => !string.IsNullOrEmpty(SettingsStore.Current.GuidedAccessPinHash);

    /// <summary>Message d'erreur de format, ou null si le code est acceptable.</summary>
    public static string? FormatError(string pin)
    {
        if (pin.Length < MinLength) return $"Le code doit comporter au moins {MinLength} chiffres.";
        if (pin.Length > MaxLength) return $"Le code ne peut pas dépasser {MaxLength} chiffres.";
        foreach (var c in pin)
            if (c is < '0' or > '9') return "Utilisez uniquement des chiffres.";
        if (pin.Distinct().Count() == 1) return "Évitez un code composé d'un seul chiffre répété.";
        return null;
    }

    /// <summary>Temps d'attente restant avant un nouvel essai (zéro si aucun blocage).</summary>
    public static TimeSpan LockRemaining
    {
        get
        {
            var left = _lockedUntil - DateTime.UtcNow;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>Vérifie le code hors du thread UI (PBKDF2 est volontairement lent). Applique la limitation des essais.</summary>
    public static Task<bool> VerifyAsync(string pin) => Task.Run(() => Verify(pin));

    /// <summary>Vérification synchrone (≈ 0,2 à 0,5 s sur un PC modeste) avec limitation des essais.</summary>
    public static bool Verify(string pin)
    {
        if (LockRemaining > TimeSpan.Zero) return false;
        var stored = SettingsStore.Current.GuidedAccessPinHash;
        bool ok;
        try { ok = pin.Length is >= MinLength and <= MaxLength && PinHasher.Verify(pin, stored); }
        catch { ok = false; }
        if (ok)
        {
            _failures = 0;
            _lockedUntil = DateTime.MinValue;
            return true;
        }
        _failures++;
        if (_failures >= 3)
        {
            // 30 s après 3 erreurs, puis doublement jusqu'à 5 minutes.
            var seconds = Math.Min(300, 30 * (1 << Math.Min(4, _failures - 3)));
            _lockedUntil = DateTime.UtcNow.AddSeconds(seconds);
        }
        return false;
    }

    public static int Failures => _failures;

    public static async Task SetAsync(string pin)
    {
        if (FormatError(pin) is { } error) throw new ValidationException(error);
        var hash = await Task.Run(() => PinHasher.Hash(pin));
        SettingsStore.Update(s => s.GuidedAccessPinHash = hash);
    }

    public static void Clear() => SettingsStore.Update(s => s.GuidedAccessPinHash = null);
}
