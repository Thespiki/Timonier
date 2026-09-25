using System.Buffers.Binary;

namespace Timonier.Broker;

/// <summary>
/// Requête envoyée au broker. Ne contient QUE des identifiants (réglage/action/entrée de journal) et des
/// paramètres texte validés côté broker : jamais d'opération, de chemin de registre ou de commande.
/// </summary>
public sealed class BrokerRequest
{
    public int Id { get; set; }
    /// <summary>hello | apply | action | undo | cancel | exit</summary>
    public string Op { get; set; } = "";
    public string? TweakId { get; set; }
    public string? Option { get; set; }
    public string? ActionId { get; set; }
    public Dictionary<string, string>? Params { get; set; }
    public Guid? EntryId { get; set; }
    public int? TargetId { get; set; }
}

public sealed class BrokerResponse
{
    public int Id { get; set; }
    /// <summary>false = message de progression intermédiaire ; true = réponse finale.</summary>
    public bool Final { get; set; } = true;
    public bool Ok { get; set; }
    public string? Message { get; set; }
    public string? Progress { get; set; }
    public Guid? JournalId { get; set; }
    public int Effect { get; set; }
    public Dictionary<string, string>? Data { get; set; }
    public bool Cancelled { get; set; }
    public int? ServerVersion { get; set; }
}

/// <summary>Trames : longueur (int32 little-endian) + JSON UTF-8. Taille bornée pour éviter l'épuisement mémoire.</summary>
public static class BrokerFraming
{
    public const int ProtocolVersion = 1;
    public const int MaxFrameBytes = 1024 * 1024;

    public static async Task WriteAsync(Stream stream, byte[] payload, CancellationToken ct)
    {
        if (payload.Length > MaxFrameBytes) throw new InvalidDataException("Trame trop volumineuse.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Lit une trame complète ; null si le canal est fermé proprement.</summary>
    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false)) return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaxFrameBytes) throw new InvalidDataException("Longueur de trame invalide.");
        var payload = new byte[length];
        if (!await ReadExactAsync(stream, payload, ct).ConfigureAwait(false)) throw new EndOfStreamException();
        return payload;
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0) return read == 0 ? false : throw new EndOfStreamException();
            read += n;
        }
        return true;
    }
}
