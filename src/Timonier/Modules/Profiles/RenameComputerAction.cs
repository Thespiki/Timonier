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
    public string Title => L("Rename this PC");
    public bool RequiresAdmin => true;
    public bool RequiresElevatedConfirmation => true;

    [GeneratedRegex(@"^[A-Za-z0-9-]{1,15}$")] private static partial Regex NameRx();

    /// <summary>Message d'erreur (ou null si le nom est valide) : utilisé aussi par la saisie de l'interface.</summary>
    public static string? Check(string? name)
    {
        var v = (name ?? "").Trim();
        if (v.Length == 0) return L("Enter a name.");
        if (!NameRx().IsMatch(v)) return L("1 to 15 characters: letters without accents, numbers and hyphens only.");
        if (v.All(char.IsAsciiDigit)) return L("The name can't consist of numbers only.");
        if (v.StartsWith('-') || v.EndsWith('-')) return L("The name can't start or end with a hyphen.");
        return null;
    }

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        var name = Validate.Required(p, "name", 15);
        // Strict côté broker : le nom transmis doit être exactement celui qui a été vérifié (aucun espace autour).
        if (!NameRx().IsMatch(name)) throw new ValidationException(L("1 to 15 characters: letters without accents, numbers and hyphens only."));
        if (Check(name) is { } error) throw new ValidationException(error);
    }

    public string DescribeForConfirmation(IReadOnlyDictionary<string, string> parameters) =>
        L("Timonier will rename this computer to “{0}”.\n\nThe new name will take effect at the next restart. Devices that access this PC by its name (shares, shared printers) may need to be reconfigured. This change isn't recorded in History: to revert it, rename the PC again.", Validate.Required(parameters, "name", 15));

    public Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);
        var name = Validate.Required(p, "name", 15);
        // Sur un PC membre d'un domaine, SetComputerNameEx ne renomme pas le compte d'ordinateur dans l'annuaire :
        // la relation d'approbation serait rompue au redémarrage. On refuse et on oriente vers les outils du domaine.
        if (IsDomainMember())
            return Task.FromResult(ActionResult.Fail(
                L("This PC is a member of a domain: it must be renamed with a domain account (Settings > System > About), otherwise it could no longer connect to the domain. Ask your IT department.")));
        ctx.Progress?.Report(L("Changing the computer name…"));
        if (!SetComputerNameEx(ComputerNamePhysicalDnsHostname, name))
        {
            var error = new Win32Exception(Marshal.GetLastPInvokeError());
            return Task.FromResult(ActionResult.Fail(L("Windows rejected the new name: {0}", error.Message)));
        }
        return Task.FromResult(ActionResult.Ok(L("The PC will be named “{0}” after the restart. To revert, rename it again.", name))
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
