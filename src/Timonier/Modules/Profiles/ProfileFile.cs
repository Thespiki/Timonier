using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Timonier.Core.Catalog;
using Timonier.Core.Model;
using Timonier.Core.Security;
using Timonier.Modules.Apps;

namespace Timonier.Modules.Profiles;

/// <summary>Contenu d'un fichier de configuration exporté (aucune donnée personnelle : ni nom de PC, ni compte).</summary>
internal sealed class ProfileFileModel
{
    public string? Format { get; set; }
    public int Version { get; set; }
    public string? CreatedAt { get; set; }
    public List<string>? Profiles { get; set; }
    public Dictionary<string, string>? Tweaks { get; set; }
    public List<string>? Apps { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    MaxDepth = 8,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    AllowTrailingCommas = false)]
[JsonSerializable(typeof(ProfileFileModel))]
internal sealed partial class ProfilesJson : JsonSerializerContext;

/// <summary>Export et import des plans. Un fichier importé est une donnée NON fiable : il ne fait que préremplir le plan à vérifier.</summary>
internal static partial class ProfileFile
{
    public const string FormatId = "timonier.profile";
    public const int CurrentVersion = 1;
    public const long MaxBytes = 256 * 1024;
    private const int MaxTweaks = 600;
    private const int MaxApps = 120;
    private const int MaxProfiles = 32;

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._\-]{0,99}$")] private static partial Regex TweakIdRx();
    [GeneratedRegex(@"^[a-z0-9][a-z0-9_\-]{0,39}$")] private static partial Regex OptionRx();

    public static string Serialize(IEnumerable<string> profiles, IEnumerable<KeyValuePair<string, string>> tweaks, IEnumerable<string> apps)
    {
        var model = new ProfileFileModel
        {
            Format = FormatId,
            Version = CurrentVersion,
            CreatedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture),
            Profiles = [.. profiles],
            Tweaks = tweaks.ToDictionary(k => k.Key, k => k.Value, StringComparer.Ordinal),
            Apps = [.. apps],
        };
        return JsonSerializer.Serialize(model, ProfilesJson.Default.ProfileFileModel);
    }

    /// <summary>Lit et valide un fichier (hors du thread d'interface). Lève <see cref="ValidationException"/> si le fichier est refusé.</summary>
    public static ImportedPlan Read(string path, ModuleRegistry registry)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new ValidationException(L("File not found."));
        if (info.Length > MaxBytes) throw new ValidationException(L("File too large (256 KB maximum): this isn't a Timonier configuration."));

        ProfileFileModel? model;
        try
        {
            var bytes = File.ReadAllBytes(path);
            var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿');
            model = JsonSerializer.Deserialize(text, ProfilesJson.Default.ProfileFileModel);
        }
        catch (DecoderFallbackException) { throw new ValidationException(L("The file isn't valid UTF-8 text.")); }
        catch (JsonException ex) { throw new ValidationException(L("Unreadable file or unexpected format: {0}", ex.Message)); }

        if (model is null || model.Format != FormatId) throw new ValidationException(L("This file isn't a configuration exported by Timonier."));
        if (model.Version != CurrentVersion)
            throw new ValidationException(L("Unsupported file version ({0}). This version of Timonier reads version {1}.", model.Version, CurrentVersion));
        if (model.Profiles?.Count > MaxProfiles || model.Tweaks?.Count > MaxTweaks || model.Apps?.Count > MaxApps)
            throw new ValidationException(L("The file contains too many items to be a valid configuration."));

        var plan = new ImportedPlan { FileName = Path.GetFileName(path) };

        foreach (var id in (model.Profiles ?? []).Distinct(StringComparer.Ordinal))
        {
            if (ProfileCatalog.Find(id ?? "") is not null) plan.Profiles.Add(id!);
        }

        var unknown = new List<string>();
        var skippedActions = 0;
        foreach (var (id, option) in model.Tweaks ?? [])
        {
            if (id is null || option is null || !TweakIdRx().IsMatch(id) || !OptionRx().IsMatch(option)) { unknown.Add(Short(id)); continue; }
            var tweak = registry.GetTweak(id);
            if (tweak is null || tweak.GetOption(option) is null) { unknown.Add(id); continue; }
            if (tweak.Kind == TweakKind.Action) { skippedActions++; continue; }
            plan.Tweaks[id] = option;
        }
        if (unknown.Count > 0)
            plan.Notices.Add(LP(unknown.Count, "{0} setting unknown to this version of Timonier or with an invalid option was ignored: {1}{2}.",
                "{0} settings unknown to this version of Timonier or with an invalid option were ignored: {1}{2}.",
                string.Join(", ", unknown.Take(6)), unknown.Count > 6 ? "…" : ""));
        if (skippedActions > 0)
            plan.Notices.Add(LP(skippedActions, "{0} one-time action ignored: a profile only runs verifiable settings.",
                "{0} one-time actions ignored: a profile only runs verifiable settings."));

        var invalidApps = new List<string>();
        var outside = new List<string>();
        foreach (var raw in (model.Apps ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var id = Validate.WingetId((raw ?? "").Trim());
                if (!AppsCatalog.Contains(id)) outside.Add(id);
                plan.Apps.Add(id);
            }
            catch (ValidationException) { invalidApps.Add(Short(raw)); }
        }
        if (invalidApps.Count > 0)
            plan.Notices.Add(LP(invalidApps.Count, "{0} invalid app ID ignored: {1}.",
                "{0} invalid app IDs ignored: {1}.", string.Join(", ", invalidApps.Take(4))));
        if (outside.Count > 0)
            plan.Notices.Add(LP(outside.Count, "{0} app outside Timonier's verified catalog ({1}{2}): it stays unchecked and, if you check it, Windows will ask you for an additional administrator confirmation.",
                "{0} apps outside Timonier's verified catalog ({1}{2}): they stay unchecked and, if you check them, Windows will ask you for an additional administrator confirmation.",
                string.Join(", ", outside.Take(4)), outside.Count > 4 ? "…" : ""));
        return plan;
    }

    private static string Short(string? value)
    {
        // Ni caractères de contrôle ni caractères de mise en forme invisibles (inversion bidirectionnelle, etc.) dans les avis affichés.
        var v = new string((value ?? "").Where(c => !char.IsControl(c) &&
            char.GetUnicodeCategory(c) is not (System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.Surrogate)).ToArray());
        return v.Length > 40 ? v[..40] + "…" : v.Length == 0 ? L("(empty)") : v;
    }
}
