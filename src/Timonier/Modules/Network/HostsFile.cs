using System.Net;
using System.Text;
using Timonier.Core.Security;

namespace Timonier.Modules.Network;

/// <summary>Ligne active du fichier hosts (hors commentaires).</summary>
internal sealed record HostsEntry(string Address, string Host, bool Managed)
{
    /// <summary>Blocage (0.0.0.0, 127.x, ::) plutôt que redirection vers une autre machine.</summary>
    public bool IsBlock => Address is "0.0.0.0" or "::" or "::1" or "::0" || Address.StartsWith("127.", StringComparison.Ordinal);
}

internal sealed record HostsSnapshot(List<HostsEntry> Entries, bool Exists, DateTime? BackupTime, string? Error)
{
    public IEnumerable<HostsEntry> Managed => Entries.Where(e => e.Managed);
    public IEnumerable<HostsEntry> Others => Entries.Where(e => !e.Managed);
}

/// <summary>
/// Lecture et modification du fichier hosts. Timonier ne touche QU'À sa section délimitée par des marqueurs :
/// les autres lignes sont conservées octet pour octet (décodage Latin-1, sans perte), ainsi que les fins de ligne.
/// Les écritures (actions admin) sauvegardent d'abord le fichier dans « hosts.timonier.bak ».
/// </summary>
internal static class HostsFile
{
    public const string BeginMarker = "# --- Timonier (début) ---";
    public const string EndMarker = "# --- Timonier (fin) ---";
    public const int MaxManagedEntries = 500;

    /// <summary>
    /// Taille maximale lue (les grandes listes de blocage publiques font quelques Mo) : au-delà, le fichier n'est ni
    /// analysé ni modifié, pour ne pas saturer la mémoire d'un PC modeste.
    /// </summary>
    public const long MaxFileBytes = 32L * 1024 * 1024;

    // Les marqueurs sont écrits en UTF-8 ; lus en Latin-1 ils apparaissent sous cette forme.
    private static readonly string BeginMarkerLatin1 = Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(BeginMarker));
    private static readonly string EndMarkerLatin1 = Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(EndMarker));

    /// <summary>
    /// Domaines indispensables à la sécurité de Windows (mises à jour, Defender, SmartScreen) : Timonier refuse de
    /// les bloquer, car ce serait affaiblir la protection du PC.
    /// </summary>
    private static readonly string[] ProtectedSuffixes =
    [
        "windowsupdate.com", "windowsupdate.microsoft.com", "update.microsoft.com", "delivery.mp.microsoft.com",
        "wd.microsoft.com", "wdcp.microsoft.com", "wdcpalt.microsoft.com", "smartscreen.microsoft.com",
        "smartscreen-prod.microsoft.com", "definitionupdates.microsoft.com", "security.microsoft.com",
        // Réputation SmartScreen des URL et des applications, listes de révocation des certificats.
        "urs.microsoft.com", "checkappexec.microsoft.com", "crl.microsoft.com", "mscrl.microsoft.com",
        "oneocsp.microsoft.com", "ocsp.msocsp.com",
    ];

    public static string FilePath => Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");
    public static string BackupPath => Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts.timonier.bak");

    /// <summary>Valide un nom de site à bloquer (format strict, domaine non numérique, domaines de sécurité refusés).</summary>
    public static string ValidateBlockHost(string value)
    {
        var raw = value.Trim();
        // Tolère une URL collée : on ne garde que le nom d'hôte.
        if (raw.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(raw, UriKind.Absolute, out var uri)) raw = uri.Host;
        // Nom de domaine accentué (IDN) : converti en punycode, seule forme reconnue par le fichier hosts.
        if (raw.Any(c => c > 127))
        {
            try { raw = new System.Globalization.IdnMapping().GetAscii(raw.TrimEnd('.')); }
            catch (ArgumentException) { /* laissé tel quel : refusé ci-dessous avec un message clair */ }
        }
        var host = Validate.HostName(raw);
        if (!host.Contains('.')) throw new ValidationException(L("Enter a full domain name (e.g. example.com)."));
        if (IPAddress.TryParse(host, out _) || host.Split('.')[^1].All(char.IsAsciiDigit))
            throw new ValidationException(L("Enter a domain name, not an IP address."));
        if (host is "localhost" or "localhost.localdomain" || host.EndsWith(".local", StringComparison.Ordinal))
            throw new ValidationException(L("This name is reserved for the local network."));
        if (ProtectedSuffixes.Any(s => host == s || host.EndsWith("." + s, StringComparison.Ordinal)))
            throw new ValidationException(L("This domain is used for Windows updates or protection: Timonier won't block it."));
        return host;
    }

    public static HostsSnapshot Read()
    {
        DateTime? backup = File.Exists(BackupPath) ? File.GetLastWriteTime(BackupPath) : null;
        try
        {
            if (!File.Exists(FilePath)) return new HostsSnapshot([], false, backup, null);
            var text = Encoding.Latin1.GetString(ReadAllBytesShared(FilePath));
            return new HostsSnapshot(Parse(text), true, backup, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new HostsSnapshot([], true, backup, ex.Message);
        }
    }

    private static byte[] ReadAllBytesShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length > MaxFileBytes)
            throw new IOException(L("the hosts file is larger than {0} MB: Timonier doesn't analyze or modify it.", MaxFileBytes / (1024 * 1024)));
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }

    internal static List<HostsEntry> Parse(string text)
    {
        var result = new List<HostsEntry>();
        var inSection = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (IsBegin(line)) { inSection = true; continue; }
            if (IsEnd(line)) { inSection = false; continue; }
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            // Première colonne : adresse ; les suivantes : noms d'hôte (jusqu'à 9 par ligne).
            if (!IPAddress.TryParse(parts[0].TrimStart('﻿', 'ï', '»', '¿'), out var ip)) continue;
            foreach (var host in parts.Skip(1)) result.Add(new HostsEntry(ip.ToString(), host.ToLowerInvariant(), inSection));
        }
        return result;
    }

    private static bool IsBegin(string line) => line == BeginMarkerLatin1 || line == BeginMarker;
    private static bool IsEnd(string line) => line == EndMarkerLatin1 || line == EndMarker;

    /// <summary>
    /// Réécrit la section gérée avec <paramref name="hosts"/> (ordre conservé). Une liste vide supprime la section.
    /// Le reste du fichier n'est pas modifié. Sauvegarde préalable dans hosts.timonier.bak.
    /// </summary>
    public static void WriteManaged(IReadOnlyList<string> hosts)
    {
        if (hosts.Count > MaxManagedEntries) throw new ValidationException(L("Limit of {0} blocked sites reached.", MaxManagedEntries));
        foreach (var h in hosts) ValidateBlockHost(h); // défense en profondeur : rien d'autre ne peut entrer dans le fichier

        var path = FilePath;
        var exists = File.Exists(path);
        if (exists && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
            throw new InvalidOperationException(L("The hosts file is write-protected (“Read-only” attribute, often set by security software): Timonier doesn't remove this protection. Remove it yourself if you want to manage blocks here."));
        var bytes = exists ? ReadAllBytesShared(path) : [];
        // Fichier enregistré en UTF-16 (Bloc-notes « Unicode ») : des lignes ajoutées octet par octet le corrompraient.
        if (bytes.Length >= 2 && (bytes[0], bytes[1]) is (0xFF, 0xFE) or (0xFE, 0xFF))
            throw new InvalidOperationException(L("The hosts file is saved in UTF-16, a format Timonier can't safely modify: change canceled. Save it again as ANSI or UTF-8, then try again."));
        var text = Encoding.Latin1.GetString(bytes);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) || !exists ? "\r\n" : "\n";

        // Découpe en lignes en conservant chaque ligne telle quelle (avec son éventuel \r).
        var lines = text.Length == 0 ? [] : text.Split('\n').ToList();
        var endsWithNewline = text.EndsWith('\n');
        if (endsWithNewline) lines.RemoveAt(lines.Count - 1);

        var begin = lines.FindIndex(l => IsBegin(l.TrimEnd('\r').Trim()));
        var end = begin >= 0 ? lines.FindIndex(begin + 1, l => IsEnd(l.TrimEnd('\r').Trim())) : -1;
        if (begin >= 0 && end < 0) throw new InvalidOperationException(L("Timonier section of the hosts file is incomplete (end marker missing): change canceled for safety."));

        var section = new List<string>();
        if (hosts.Count > 0)
        {
            var cr = newline == "\r\n" ? "\r" : "";
            section.Add(BeginMarkerLatin1 + cr);
            section.AddRange(hosts.Select(h => "0.0.0.0 " + h + cr));
            section.Add(EndMarkerLatin1 + cr);
        }

        if (begin >= 0)
        {
            lines.RemoveRange(begin, end - begin + 1);
            lines.InsertRange(begin, section);
        }
        else if (section.Count > 0)
        {
            // La dernière ligne existante sera terminée avec la même fin de ligne que le reste du fichier.
            if (lines.Count > 0 && !endsWithNewline && newline == "\r\n" && !lines[^1].EndsWith('\r')) lines[^1] += "\r";
            lines.AddRange(section);
            endsWithNewline = true;
        }

        var output = string.Join("\n", lines) + (endsWithNewline && lines.Count > 0 ? "\n" : "");
        var outBytes = Encoding.Latin1.GetBytes(output);

        if (exists) File.Copy(path, BackupPath, overwrite: true);
        // Écriture en place : conserve les ACL et attributs du fichier d'origine.
        using var fs = new FileStream(path, exists ? FileMode.Truncate : FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        fs.Write(outBytes);
        fs.Flush(true);
    }
}
