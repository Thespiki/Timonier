using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using Timonier.Core.Model;
using Timonier.Core.Platform;

namespace Timonier.Core.Engine;

/// <summary>Instantané d'une valeur de registre (sérialisable).</summary>
public sealed record RegValueSnapshot(RegistryValueKind Kind, long? Number, string? Text, string[]? Multi, byte[]? Binary)
{
    public static RegValueSnapshot? From(object? value, RegistryValueKind kind) => value switch
    {
        null => null,
        int i => new(kind, i, null, null, null),
        long l => new(kind, l, null, null, null),
        string s => new(kind, null, s, null, null),
        string[] m => new(kind, null, null, m, null),
        byte[] b => new(kind, null, null, null, b),
        _ => new(kind, null, value.ToString(), null, null),
    };

    public object ToValue() => Kind switch
    {
        RegistryValueKind.DWord => (int)(Number ?? 0),
        RegistryValueKind.QWord => Number ?? 0,
        RegistryValueKind.MultiString => Multi ?? [],
        RegistryValueKind.Binary or RegistryValueKind.None => Binary ?? [],
        _ => Text ?? "",
    };
}

public sealed record RegTreeSnapshot(Dictionary<string, RegValueSnapshot> Values, Dictionary<string, RegTreeSnapshot> SubKeys);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$t")]
[JsonDerivedType(typeof(RegValueUndo), "reg")]
[JsonDerivedType(typeof(RegKeyUndo), "regkey")]
[JsonDerivedType(typeof(ServiceUndo), "svc")]
[JsonDerivedType(typeof(TaskUndo), "task")]
[JsonDerivedType(typeof(NoUndo), "none")]
public abstract record UndoRecord;

public sealed record RegValueUndo(RegHive Hive, string Key, string Name, bool Existed, RegValueSnapshot? Previous) : UndoRecord;
public sealed record RegKeyUndo(RegHive Hive, string Key, bool Existed, RegTreeSnapshot? Tree) : UndoRecord;
public sealed record ServiceUndo(string Name, ServiceStartKind PreviousStart) : UndoRecord;
public sealed record TaskUndo(string Path, bool PreviousEnabled) : UndoRecord;
public sealed record NoUndo(string Reason) : UndoRecord;

/// <summary>Une modification appliquée par Timonier, avec de quoi l'annuler.</summary>
public sealed class JournalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
    /// <summary>Identifiant du réglage ou de l'action.</summary>
    public string SourceId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? FromOption { get; set; }
    public string? ToOption { get; set; }
    public string? ToLabel { get; set; }
    /// <summary>Vrai si appliqué par le broker admin (stocké dans HKLM, annulable uniquement via le broker).</summary>
    public bool Machine { get; set; }
    public string? UserSid { get; set; }
    public List<UndoRecord> Undo { get; set; } = [];
    public bool Undone { get; set; }
    public DateTimeOffset? UndoneAt { get; set; }
    public string? Note { get; set; }

    [JsonIgnore] public bool CanUndo => !Undone && Undo.Any(u => u is not NoUndo);
}

/// <summary>Journal utilisateur : %LOCALAPPDATA%\Timonier\journal.json (modifications sans élévation).</summary>
public sealed class UserJournalStore
{
    private const int MaxEntries = 1000;
    private readonly string _path = Path.Combine(AppPaths.LocalData, "journal.json");
    private readonly Lock _gate = new();
    private List<JournalEntry>? _cache;

    public IReadOnlyList<JournalEntry> All()
    {
        lock (_gate) return [.. Load()];
    }

    public void Add(JournalEntry e)
    {
        lock (_gate)
        {
            var list = Load();
            list.Add(e);
            if (list.Count > MaxEntries) list.RemoveRange(0, list.Count - MaxEntries);
            Save(list);
        }
    }

    public void Update(JournalEntry e)
    {
        lock (_gate)
        {
            var list = Load();
            var i = list.FindIndex(x => x.Id == e.Id);
            if (i >= 0) list[i] = e;
            Save(list);
        }
    }

    public void Clear()
    {
        lock (_gate) Save([]);
    }

    private List<JournalEntry> Load()
    {
        if (_cache is not null) return _cache;
        try
        {
            _cache = File.Exists(_path)
                ? JsonSerializer.Deserialize(File.ReadAllText(_path), CoreJson.Default.ListJournalEntry) ?? []
                : [];
        }
        catch (Exception ex)
        {
            Log.Error("Journal", "journal utilisateur illisible, recréé", ex);
            _cache = [];
        }
        return _cache;
    }

    private void Save(List<JournalEntry> list)
    {
        _cache = list;
        Directory.CreateDirectory(AppPaths.LocalData);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(list, CoreJson.Default.ListJournalEntry));
        File.Move(tmp, _path, overwrite: true);
    }
}

/// <summary>
/// Journal machine : HKLM\SOFTWARE\Timonier\Journal (une valeur par entrée). Seuls les administrateurs peuvent
/// y écrire (ACL par défaut de HKLM\SOFTWARE) : un programme non élevé ne peut donc pas y glisser de fausses
/// « données d'annulation » que le broker exécuterait ensuite avec les droits admin.
/// </summary>
public static class MachineJournalStore
{
    private const string JournalKey = AppPaths.MachineRegistryKey + @"\Journal";
    private const int MaxEntries = 500;

    /// <summary>Lecture (possible sans élévation).</summary>
    public static List<JournalEntry> ReadAll()
    {
        var list = new List<JournalEntry>();
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(JournalKey, false);
            if (k is null) return list;
            foreach (var name in k.GetValueNames())
            {
                try
                {
                    if (k.GetValue(name) is string json && JsonSerializer.Deserialize(json, CoreJson.Default.JournalEntry) is { } e)
                        list.Add(e);
                }
                catch { /* entrée corrompue ignorée */ }
            }
        }
        catch (Exception ex) { Log.Warn("Journal", "lecture HKLM : " + ex.Message); }
        return [.. list.OrderBy(e => e.At)];
    }

    public static JournalEntry? Get(Guid id)
    {
        using var k = Registry.LocalMachine.OpenSubKey(JournalKey, false);
        return k?.GetValue(id.ToString("N")) is string json ? JsonSerializer.Deserialize(json, CoreJson.Default.JournalEntry) : null;
    }

    /// <summary>Écriture : broker élevé uniquement.</summary>
    public static void Write(JournalEntry e)
    {
        using var k = Registry.LocalMachine.CreateSubKey(JournalKey, true);
        k.SetValue(e.Id.ToString("N"), JsonSerializer.Serialize(e, CoreJson.Default.JournalEntry), RegistryValueKind.String);
        var names = k.GetValueNames();
        if (names.Length > MaxEntries)
        {
            var ordered = ReadAll();
            foreach (var old in ordered.Take(names.Length - MaxEntries))
                k.DeleteValue(old.Id.ToString("N"), false);
        }
    }
}
