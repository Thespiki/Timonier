using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace PcPilot.Core.Platform;

/// <summary>Appels Win32 partagés. Les modules peuvent ajouter leurs propres P/Invoke dans leur dossier.</summary>
public static partial class Native
{
    public const int SM_MAXIMUMTOUCHES = 95;
    public const int SM_CLEANBOOT = 67;
    public const int HWND_BROADCAST = 0xFFFF;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFirmwareType(out int firmwareType);

    public static bool IsUefiFirmware() => GetFirmwareType(out var t) && t == 2;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint SendMessageTimeout(nint hWnd, int msg, nint wParam, string lParam, uint flags, uint timeout, out nint result);

    /// <summary>Notifie toutes les fenêtres d'un changement de paramètre (thème, environnement, stratégies…).</summary>
    public static void BroadcastSettingChange(string area)
    {
        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, area, SMTO_ABORTIFHUNG, 3000, out _);
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint hWnd, string text, string caption, uint type);

    private const uint MB_YESNO = 0x4, MB_ICONWARNING = 0x30, MB_TOPMOST = 0x40000, MB_SETFOREGROUND = 0x10000, MB_DEFBUTTON2 = 0x100;
    private const int IDYES = 6;

    /// <summary>
    /// Boîte de confirmation affichée PAR LE BROKER (processus élevé). Grâce à l'isolation UIPI, un processus
    /// non élevé ne peut pas cliquer dessus à la place de l'utilisateur : c'est la protection des actions sensibles.
    /// </summary>
    public static bool ConfirmFromElevatedProcess(string title, string message) =>
        MessageBox(0, message, title, MB_YESNO | MB_ICONWARNING | MB_TOPMOST | MB_SETFOREGROUND | MB_DEFBUTTON2) == IDYES;

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfo(uint action, uint uiParam, nint pvParam, uint winIni);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfoString(uint action, uint uiParam, string pvParam, uint winIni);

    public const uint SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;

    // ---------------------------------------------------------------- Jeton / élévation

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint token, int infoClass, out int info, int length, out int returnLength);

    private const int TokenElevationType = 18;
    private const int TokenElevationTypeLimited = 3;

    /// <summary>Vrai si le jeton courant est un jeton filtré par l'UAC (l'utilisateur est admin mais non élevé).</summary>
    public static bool IsTokenElevationTypeLimited()
    {
        using var id = WindowsIdentity.GetCurrent();
        return GetTokenInformation(id.Token, TokenElevationType, out var type, sizeof(int), out _) && type == TokenElevationTypeLimited;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    private const uint TOKEN_QUERY = 0x0008;

    /// <summary>SID de l'utilisateur propriétaire d'un processus (utilisé par le broker pour identifier son client).</summary>
    public static SecurityIdentifier? GetProcessUserSid(System.Diagnostics.Process process)
    {
        if (!OpenProcessToken(process.Handle, TOKEN_QUERY, out var token)) return null;
        try
        {
            using var identity = new WindowsIdentity(token);
            return identity.User;
        }
        finally { CloseHandle(token); }
    }

    // ---------------------------------------------------------------- Gestionnaire de services

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint OpenService(nint scManager, string serviceName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "ChangeServiceConfigW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ChangeServiceConfig(nint service, uint serviceType, uint startType, uint errorControl,
        string? binaryPathName, string? loadOrderGroup, nint tagId, string? dependencies, string? serviceStartName,
        string? password, string? displayName);

    [LibraryImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ChangeServiceConfig2(nint service, uint infoLevel, ref int info);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseServiceHandle(nint handle);

    public const uint SC_MANAGER_CONNECT = 0x0001;
    public const uint SERVICE_CHANGE_CONFIG = 0x0002;
    public const uint SERVICE_QUERY_CONFIG = 0x0001;
    public const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    public const uint SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;

    public static Win32Exception LastError(string context) =>
        new(Marshal.GetLastWin32Error(), context + " : " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
}
