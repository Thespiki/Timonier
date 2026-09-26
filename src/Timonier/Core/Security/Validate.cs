using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Timonier.Core.Security;

public sealed class ValidationException(string message) : Exception(message);

/// <summary>
/// Validation stricte des paramètres d'actions. Principe : liste blanche de formats, longueur bornée,
/// aucun caractère de contrôle. Tout ce qui n'est pas explicitement valide est refusé.
/// </summary>
public static partial class Validate
{
    public const int MaxParameters = 32;
    public const int MaxKeyLength = 40;
    public const int MaxValueLength = 8192;

    // Ancre de fin \z (et non $, qui accepte un saut de ligne final) ; classes ASCII explicites (pas de \d ni \w Unicode).
    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_.]{0,39}\z")] private static partial Regex KeyRx();
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+\-]{0,127}\z")] private static partial Regex WingetIdRx();
    [GeneratedRegex(@"^[A-Za-z0-9._\-]+![A-Za-z0-9._\-]+\z")] private static partial Regex AumidRx();
    [GeneratedRegex(@"^[A-Za-z0-9 ._\-]{1,20}\z")] private static partial Regex UserNameRx();
    [GeneratedRegex(@"^S-1-[0-9]{1,2}(-[0-9]{1,10}){1,14}\z")] private static partial Regex SidRx();
    [GeneratedRegex(@"^[A-Za-z0-9\\&_.#{}\- ]{3,400}\z")] private static partial Regex DeviceIdRx();
    [GeneratedRegex(@"^(?=.{1,253}\z)(?!-)[A-Za-z0-9\-]{1,63}(?<!-)(\.(?!-)[A-Za-z0-9\-]{1,63}(?<!-))*\z")] private static partial Regex HostNameRx();

    /// <summary>Contrôles communs à tout sac de paramètres (taille, noms, caractères de contrôle).</summary>
    public static void ParameterBag(IReadOnlyDictionary<string, string> p)
    {
        if (p.Count > MaxParameters) throw new ValidationException(L("Trop de paramètres."));
        foreach (var (k, v) in p)
        {
            if (!KeyRx().IsMatch(k)) throw new ValidationException(L("Nom de paramètre invalide : {0}", k));
            if (v is null || v.Length > MaxValueLength) throw new ValidationException(L("Paramètre « {0} » trop long.", k));
            if (v.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
                throw new ValidationException(L("Paramètre « {0} » : caractères de contrôle interdits.", k));
        }
    }

    public static string Required(IReadOnlyDictionary<string, string> p, string key, int maxLength = 260)
    {
        if (!p.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v)) throw new ValidationException(L("Paramètre manquant : {0}", key));
        if (v.Length > maxLength) throw new ValidationException(L("Paramètre « {0} » trop long (max {1}).", key, maxLength));
        return v.Trim();
    }

    public static string? Optional(IReadOnlyDictionary<string, string> p, string key, int maxLength = 260)
    {
        if (!p.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v)) return null;
        if (v.Length > maxLength) throw new ValidationException(L("Paramètre « {0} » trop long (max {1}).", key, maxLength));
        return v.Trim();
    }

    public static string OneOf(IReadOnlyDictionary<string, string> p, string key, params string[] allowed)
    {
        var v = Required(p, key, 128);
        return allowed.FirstOrDefault(a => string.Equals(a, v, StringComparison.OrdinalIgnoreCase))
               ?? throw new ValidationException(L("Valeur non autorisée pour « {0} ».", key));
    }

    public static int Int(IReadOnlyDictionary<string, string> p, string key, int min, int max)
    {
        var v = Required(p, key, 12);
        if (!int.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var i) || i < min || i > max)
            throw new ValidationException(L("« {0} » doit être un entier entre {1} et {2}.", key, min, max));
        return i;
    }

    public static bool Bool(IReadOnlyDictionary<string, string> p, string key, bool defaultValue = false)
    {
        if (!p.TryGetValue(key, out var v) || v.Length == 0) return defaultValue;
        return v switch
        {
            "true" or "1" or "True" => true,
            "false" or "0" or "False" => false,
            _ => throw new ValidationException(L("« {0} » doit valoir true ou false.", key)),
        };
    }

    public static Guid Guid(IReadOnlyDictionary<string, string> p, string key) =>
        System.Guid.TryParse(Required(p, key, 64), out var g) ? g : throw new ValidationException(L("« {0} » n'est pas un GUID valide.", key));

    public static IPAddress IpAddress(string value, bool allowV6 = true)
    {
        if (!IPAddress.TryParse(value.Trim(), out var ip) || (!allowV6 && ip.AddressFamily != AddressFamily.InterNetwork))
            throw new ValidationException(L("Adresse IP invalide : {0}", value));
        // Refuse les formes ambiguës acceptées par TryParse (ex. "1" ou "1.2") : on exige la forme canonique IPv4.
        if (ip.AddressFamily == AddressFamily.InterNetwork && ip.ToString() != value.Trim())
            throw new ValidationException(L("Adresse IPv4 non canonique : {0}", value));
        return ip;
    }

    public static string HostName(string value)
    {
        var v = value.Trim().TrimEnd('.');
        if (!HostNameRx().IsMatch(v)) throw new ValidationException(L("Nom d'hôte invalide : {0}", value));
        return v.ToLowerInvariant();
    }

    public static string WingetId(string value) =>
        WingetIdRx().IsMatch(value) && value.Contains('.') ? value : throw new ValidationException(L("Identifiant winget invalide : {0}", value));

    public static string Aumid(string value) =>
        AumidRx().IsMatch(value) && value.Length <= 256 ? value : throw new ValidationException(L("Identifiant d'application (AUMID) invalide : {0}", value));

    /// <summary>Nom de compte local Windows : 1–20 caractères, sans caractères réservés.</summary>
    public static string LocalUserName(string value)
    {
        var v = value.Trim();
        if (!UserNameRx().IsMatch(v) || v.EndsWith('.') || v.All(c => c is '.' or ' '))
            throw new ValidationException(L("Nom d'utilisateur invalide (1 à 20 caractères : lettres, chiffres, espace, . _ -)."));
        string[] reserved = ["administrator", "administrateur", "guest", "invité", "system", "defaultaccount", "wdagutilityaccount"];
        if (reserved.Contains(v.ToLowerInvariant())) throw new ValidationException(L("Ce nom est réservé par Windows."));
        return v;
    }

    public static string Sid(string value) =>
        SidRx().IsMatch(value) ? value : throw new ValidationException(L("SID invalide."));

    public static string DeviceInstanceId(string value) =>
        DeviceIdRx().IsMatch(value) && !value.Contains("..") ? value : throw new ValidationException(L("Identifiant de périphérique invalide."));

    /// <summary>
    /// Fichier local existant, chemin absolu, pas de chemin réseau (UNC), pas de flux de données alternatif.
    /// <paramref name="allowedExtensions"/> : ex. [".exe"].
    /// </summary>
    public static string ExistingLocalFile(string value, params string[] allowedExtensions)
    {
        var v = value.Trim().Trim('"');
        if (v.Length < 4 || v.Length > 1024 || v.StartsWith(@"\\") || !Path.IsPathFullyQualified(v))
            throw new ValidationException(L("Chemin de fichier local absolu requis."));
        if (v.IndexOf(':', 2) >= 0) throw new ValidationException(L("Chemin refusé (flux alternatif)."));
        var full = Path.GetFullPath(v);
        if (!File.Exists(full)) throw new ValidationException(L("Fichier introuvable : {0}", full));
        if (allowedExtensions.Length > 0 && !allowedExtensions.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase))
            throw new ValidationException(L("Type de fichier non autorisé : {0}", Path.GetExtension(full)));
        return full;
    }

    public static string ExistingLocalDirectory(string value)
    {
        var v = value.Trim().Trim('"');
        if (v.Length < 3 || v.Length > 1024 || v.StartsWith(@"\\") || !Path.IsPathFullyQualified(v) || v.IndexOf(':', 2) >= 0)
            throw new ValidationException(L("Chemin de dossier local absolu requis."));
        var full = Path.GetFullPath(v);
        if (!Directory.Exists(full)) throw new ValidationException(L("Dossier introuvable : {0}", full));
        return full;
    }
}

/// <summary>Code PIN / mot de passe local de Timonier (PBKDF2-SHA256, sel aléatoire, comparaison à temps constant).</summary>
public static class PinHasher
{
    private const int Iterations = 210_000;

    public static string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"v1:{Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string pin, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        // Le hachage vient d'un fichier modifiable (settings.json) : format strict, jamais d'exception ni de calcul démesuré.
        var parts = stored.Split(':');
        if (parts.Length != 4 || parts[0] != "v1"
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var it)
            || it is < 100_000 or > 5_000_000)
            return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException) { return false; }
        if (salt.Length < 16 || expected.Length != 32) return false;
        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, it, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
