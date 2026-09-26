using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Timonier.Core.Catalog;
using Timonier.Core.Model;
using Timonier.Core.Security;

namespace Timonier.Modules.Profiles;

/// <summary>
/// Renomme le PC (nom d'hôte DNS et nom NetBIOS) via SetComputerNameEx. Prise en compte au prochain redémarrage.
/// Paramètre « name » : 1 à 15 caractères (lettres, chiffres, trait d'union), pas uniquement des chiffres.
/// </summary>
public sealed partial class RenameComputerAction : IActionHandler
{
    public const string ActionId = "profiles.computer.rename";

    public string Id => ActionId;
    public string Title => L("Renommer ce PC");
    public bool RequiresAdmin => true;
    public bool RequiresElevatedConfirmation => true;

    [GeneratedRegex(@"^[A-Za-z0-9-]{1,15}$")] private static partial Regex NameRx();

    /// <summary>Message d'erreur (ou null si le nom est valide) : utilisé aussi par la saisie de l'interface.</summary>
    public static string? Check(string? name)
    {
        var v = (name ?? "").Trim();
        if (v.Length == 0) return L("Indiquez un nom.");
        if (!NameRx().IsMatch(v)) return L("De 1 à 15 caractères : lettres sans accent, chiffres et trait d'union uniquement.");
        if (v.All(char.IsAsciiDigit)) return L("Le nom ne peut pas être composé uniquement de chiffres.");
        if (v.StartsWith('-') || v.EndsWith('-')) return L("Le nom ne peut ni commencer ni se terminer par un trait d'union.");
        return null;
    }

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        var name = Validate.Required(p, "name", 15);
        // Strict côté broker : le nom transmis doit être exactement celui qui a été vérifié (aucun espace autour).
        if (!NameRx().IsMatch(name)) throw new ValidationException(L("De 1 à 15 caractères : lettres sans accent, chiffres et trait d'union uniquement."));
        if (Check(name) is { } error) throw new ValidationException(error);
    }

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> parameters) =>
        L("Timonier va renommer cet ordinateur en « {0} ».\n\nLe nouveau nom sera pris en compte au prochain redémarrage. Les appareils qui accèdent à ce PC par son nom (partages, imprimantes partagées) devront peut-être être reconfigurés. Ce changement n'est pas inscrit au Journal : pour revenir en arrière, renommez de nouveau le PC.", Validate.Required(parameters, "name", 15));

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var name = Validate.Required(p, "name", 15);
        // Sur un PC membre d'un domaine, SetComputerNameEx ne renomme pas le compte d'ordinateur dans l'annuaire :
        // la relation d'approbation serait rompue au redémarrage. On refuse et on oriente vers les outils du domaine.
        if (IsDomainMember())
            return Task.FromResult(ActionResult.Fail(
                L("Ce PC est membre d'un domaine : il doit être renommé avec un compte du domaine (Paramètres > Système > Informations système), sinon il ne pourrait plus se connecter au domaine. Demandez à votre service informatique.")));
        ctx.Progress?.Report(L("Changement du nom de l'ordinateur…"));
        if (!SetComputerNameEx(ComputerNamePhysicalDnsHostname, name))
        {
            var error = new Win32Exception(Marshal.GetLastPInvokeError());
            return Task.FromResult(ActionResult.Fail(L("Windows a refusé le nouveau nom : {0}", error.Message)));
        }
        return Task.FromResult(ActionResult.Ok(L("Le PC s'appellera « {0} » après le redémarrage. Pour revenir en arrière, renommez-le de nouveau.", name))
            with { Effect = ApplyEffect.Reboot });
    }

    private const int ComputerNamePhysicalDnsHostname = 5;
    private const int NetSetupDomainName = 3;

    /// <summary>Lecture seule : le PC est-il joint à un domaine Active Directory ? (En cas de doute, on considère que oui.)</summary>
    private static bool IsDomainMember()
    {
        var status = NetGetJoinInformation(null, out var buffer, out var joinStatus);
        if (buffer != IntPtr.Zero) NetApiBufferFree(buffer);
        return status != 0 || joinStatus == NetSetupDomainName;
    }

    [LibraryImport("netapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int NetGetJoinInformation(string? server, out IntPtr nameBuffer, out int joinStatus);

    [LibraryImport("netapi32.dll")]
    private static partial int NetApiBufferFree(IntPtr buffer);

    [LibraryImport("kernel32.dll", EntryPoint = "SetComputerNameExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetComputerNameEx(int nameType, string name);
}
