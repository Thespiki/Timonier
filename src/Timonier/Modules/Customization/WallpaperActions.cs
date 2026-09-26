using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Timonier.Core.Catalog;
using Timonier.Core.Engine;
using Timonier.Core.Model;
using Timonier.Core.Platform;
using Timonier.Core.Security;

namespace Timonier.Modules.Customization;

/// <summary>Positions du fond d'écran (valeurs écrites par Paramètres dans HKCU\Control Panel\Desktop).</summary>
internal sealed record WallpaperPosition(string Key, string Label, string WallpaperStyle, string TileWallpaper)
{
    public static readonly WallpaperPosition[] All =
    [
        new("fill", L("Fill"), "10", "0"),
        new("fit", L("Fit"), "6", "0"),
        new("stretch", L("Stretch"), "2", "0"),
        new("center", L("Center"), "0", "0"),
        new("tile", L("Tile"), "0", "1"),
        new("span", L("Span (multiple displays)"), "22", "0"),
    ];

    public static WallpaperPosition? Get(string key) => All.FirstOrDefault(p => p.Key == key);

    /// <summary>Position actuelle d'après le registre (null si combinaison inconnue).</summary>
    public static WallpaperPosition? FromRegistry(string? style, string? tile) =>
        tile?.Trim() == "1" ? Get("tile") : All.FirstOrDefault(p => p.Key != "tile" && p.WallpaperStyle == style?.Trim());
}

/// <summary>Constantes et appels Win32 propres au module.</summary>
internal static partial class CustomizationNative
{
    public const uint SPI_SETDESKWALLPAPER = 0x0014;
    public const int COLOR_DESKTOP = 1;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetSysColors(int cElements, int[] lpaElements, int[] lpaRgbValues);

    /// <summary>Applique le fond d'écran (chemin vide = aucun) et l'enregistre dans le profil de l'utilisateur.</summary>
    public static void SetDesktopWallpaper(string path)
    {
        if (!Native.SystemParametersInfoString(SPI_SETDESKWALLPAPER, 0, path, Native.SPIF_UPDATEINIFILE | Native.SPIF_SENDCHANGE))
            throw Native.LastError("SystemParametersInfo(SPI_SETDESKWALLPAPER)");
    }
}

/// <summary>Règles de validation communes aux actions du module.</summary>
internal static partial class CustomizationValidate
{
    /// <summary>Formats acceptés comme fond d'écran par Windows (sans codec supplémentaire).</summary>
    public static readonly string[] WallpaperExtensions = [".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".dib", ".gif", ".tif", ".tiff"];

    /// <summary>Formats acceptés pour l'écran de verrouillage.</summary>
    public static readonly string[] LockScreenExtensions = [".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp"];

    public const long MaxImageBytes = 100L * 1024 * 1024;

    [GeneratedRegex("^[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColorRx();

    public static void OnlyKeys(IReadOnlyDictionary<string, string> p, params string[] allowed)
    {
        foreach (var key in p.Keys)
            if (!allowed.Contains(key, StringComparer.Ordinal)) throw new ValidationException(L("Unexpected parameter: {0}", key));
    }

    /// <summary>Image locale existante, d'un format autorisé et de taille raisonnable.</summary>
    public static string ImageFile(IReadOnlyDictionary<string, string> p, string key, string[] extensions)
    {
        var path = Validate.ExistingLocalFile(Validate.Required(p, key, 1024), extensions);
        var info = new FileInfo(path);
        if (info.Length == 0) throw new ValidationException(L("The image file is empty."));
        if (info.Length > MaxImageBytes) throw new ValidationException(L("Image too large (100 MB maximum)."));
        return path;
    }

    public static (byte R, byte G, byte B) HexColor(IReadOnlyDictionary<string, string> p, string key)
    {
        var v = Validate.Required(p, key, 6);
        if (!HexColorRx().IsMatch(v)) throw new ValidationException(L("Invalid color: expected format RRGGBB (e.g. 1E3A5F)."));
        var rgb = int.Parse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}

/// <summary>
/// Change le fond d'écran de l'utilisateur (image + position). Sans élévation : registre HKCU journalisé
/// (valeurs précédentes conservées) puis <c>SystemParametersInfo(SPI_SETDESKWALLPAPER)</c> pour un effet immédiat.
/// Données renvoyées : <c>previousPath</c>, <c>previousPosition</c>, <c>previousColor</c> (pour « Restaurer »).
/// </summary>
public sealed class SetWallpaperAction : IActionHandler
{
    public const string ActionId = "custom.wallpaper.set";
    public string Id => ActionId;
    public string Title => L("Change the wallpaper");
    public bool RequiresAdmin => false;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        CustomizationValidate.OnlyKeys(p, "path", "position");
        CustomizationValidate.ImageFile(p, "path", CustomizationValidate.WallpaperExtensions);
        Validate.OneOf(p, "position", [.. WallpaperPosition.All.Select(x => x.Key)]);
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        if (ctx.Elevated) return Task.FromResult(SessionOnly());
        var path = CustomizationValidate.ImageFile(p, "path", CustomizationValidate.WallpaperExtensions);
        var position = WallpaperPosition.Get(Validate.OneOf(p, "position", [.. WallpaperPosition.All.Select(x => x.Key)]))!;
        ctx.Progress?.Report(L("Applying the wallpaper…"));

        var previous = WallpaperState.Read(ctx.UserSid);
        var entry = ctx.ApplyJournaled(ActionId, L("Wallpaper"), $"{Path.GetFileName(path)} ({position.Label.ToLowerInvariant()})",
        [
            Reg.CuString(CustomizationTweaks.DesktopKey, "WallpaperStyle", position.WallpaperStyle),
            Reg.CuString(CustomizationTweaks.DesktopKey, "TileWallpaper", position.TileWallpaper),
            Reg.CuString(CustomizationTweaks.DesktopKey, "WallPaper", path),
        ]);

        try
        {
            CustomizationNative.SetDesktopWallpaper(path);
        }
        catch
        {
            OperationExecutor.Undo(entry.Undo, ctx.Exec);
            MarkUndone(entry, ctx, L("Couldn't apply: previous values restored."));
            throw;
        }

        AddNote(entry, ctx);
        return Task.FromResult(new ActionResult(true, L("Wallpaper applied: {0}.", Path.GetFileName(path)))
        {
            JournalId = entry.Id,
            Data = previous.ToData(),
        });
    }

    /// <summary>Le fond d'écran appartient à la session interactive : jamais exécuté par le processus élevé.</summary>
    internal static ActionResult SessionOnly() =>
        ActionResult.Fail(L("This action applies to the user's session and must not run elevated."));

    internal static void AddNote(JournalEntry entry, ActionContext ctx)
    {
        if (ctx.Elevated) return; // action sans élévation : toujours dans le journal utilisateur
        entry.Note = L("Undoing it from History restores the registry values: the old wallpaper comes back the next time you sign in. To switch back right away, use “Restore previous wallpaper” in Personalization.");
        JournalWriter.UserStore.Update(entry);
    }

    internal static void MarkUndone(JournalEntry entry, ActionContext ctx, string note)
    {
        if (ctx.Elevated) return;
        entry.Undone = true;
        entry.UndoneAt = DateTimeOffset.Now;
        entry.Note = note;
        JournalWriter.UserStore.Update(entry);
    }
}

/// <summary>Remplace le fond d'écran par une couleur unie (HKCU\Control Panel\Colors\Background + SetSysColors).</summary>
public sealed class SetSolidColorAction : IActionHandler
{
    public const string ActionId = "custom.wallpaper.color";
    public string Id => ActionId;
    public string Title => L("Solid color wallpaper");
    public bool RequiresAdmin => false;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        CustomizationValidate.OnlyKeys(p, "color");
        CustomizationValidate.HexColor(p, "color");
    }

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        if (ctx.Elevated) return Task.FromResult(SetWallpaperAction.SessionOnly());
        var (r, g, b) = CustomizationValidate.HexColor(p, "color");
        var hex = $"{r:X2}{g:X2}{b:X2}";
        ctx.Progress?.Report(L("Applying the color…"));

        var previous = WallpaperState.Read(ctx.UserSid);
        var entry = ctx.ApplyJournaled(ActionId, L("Wallpaper"), L("Solid color #{0}", hex),
        [
            Reg.CuString(@"Control Panel\Colors", "Background", $"{r} {g} {b}"),
            Reg.CuString(CustomizationTweaks.DesktopKey, "WallPaper", ""),
        ]);

        try
        {
            // Couleur du bureau pour la session en cours (le registre ci-dessus la conserve pour les suivantes).
            if (!CustomizationNative.SetSysColors(1, [CustomizationNative.COLOR_DESKTOP], [r | (g << 8) | (b << 16)]))
                throw Native.LastError("SetSysColors");
            CustomizationNative.SetDesktopWallpaper("");
        }
        catch
        {
            OperationExecutor.Undo(entry.Undo, ctx.Exec);
            SetWallpaperAction.MarkUndone(entry, ctx, L("Couldn't apply: previous values restored."));
            throw;
        }

        SetWallpaperAction.AddNote(entry, ctx);
        return Task.FromResult(new ActionResult(true, L("Wallpaper: solid color #{0}.", hex))
        {
            JournalId = entry.Id,
            Data = previous.ToData(),
        });
    }
}

/// <summary>
/// Image de l'écran de verrouillage de l'utilisateur, via l'API WinRT <c>LockScreen.SetImageFileAsync</c>
/// (sans élévation). Non annulable automatiquement : Windows ne donne pas accès à l'image précédente.
/// </summary>
public sealed class SetLockScreenImageAction : IActionHandler
{
    public const string ActionId = "custom.lockscreen.set";
    public string Id => ActionId;
    public string Title => L("Change the lock screen picture");
    public bool RequiresAdmin => false;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        CustomizationValidate.OnlyKeys(p, "path");
        CustomizationValidate.ImageFile(p, "path", CustomizationValidate.LockScreenExtensions);
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        if (ctx.Elevated) return SetWallpaperAction.SessionOnly();
        var path = CustomizationValidate.ImageFile(p, "path", CustomizationValidate.LockScreenExtensions);
        ctx.Progress?.Report(L("Applying the picture…"));
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            await Windows.System.UserProfile.LockScreen.SetImageFileAsync(file);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn("Customization", "écran de verrouillage : " + ex.Message);
            return ActionResult.Fail(L("Windows refused to change the lock screen picture (organization policy or unsupported format). Use Settings › Personalization › Lock screen."));
        }
        return ActionResult.Ok(L("Lock screen: {0}.", Path.GetFileName(path)));
    }
}

/// <summary>État actuel du fond d'écran (lecture seule du registre utilisateur).</summary>
internal sealed record WallpaperState(string FilePath, WallpaperPosition? Position, string? BackgroundRgb)
{
    public bool IsSolidColor => FilePath.Length == 0;

    public static WallpaperState Read(string? userSid = null)
    {
        var path = RegistryAccess.Read(RegHive.CurrentUser, CustomizationTweaks.DesktopKey, "WallPaper", userSid) as string ?? "";
        var style = RegistryAccess.Read(RegHive.CurrentUser, CustomizationTweaks.DesktopKey, "WallpaperStyle", userSid) as string;
        var tile = RegistryAccess.Read(RegHive.CurrentUser, CustomizationTweaks.DesktopKey, "TileWallpaper", userSid) as string;
        var background = RegistryAccess.Read(RegHive.CurrentUser, @"Control Panel\Colors", "Background", userSid) as string;
        return new WallpaperState(path.Trim(), WallpaperPosition.FromRegistry(style, tile), background);
    }

    /// <summary>Couleur du bureau au format RRVVBB (d'après « R V B » du registre), ou null.</summary>
    public string? BackgroundHex
    {
        get
        {
            var parts = BackgroundRgb?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts is not { Length: 3 }) return null;
            var bytes = new byte[3];
            for (var i = 0; i < 3; i++)
                if (!byte.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out bytes[i])) return null;
            return $"{bytes[0]:X2}{bytes[1]:X2}{bytes[2]:X2}";
        }
    }

    public Dictionary<string, string> ToData()
    {
        var d = new Dictionary<string, string> { ["previousPath"] = FilePath };
        if (Position is not null) d["previousPosition"] = Position.Key;
        if (BackgroundHex is { } hex) d["previousColor"] = hex;
        return d;
    }
}
