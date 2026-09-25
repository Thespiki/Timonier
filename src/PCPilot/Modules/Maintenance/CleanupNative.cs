using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PcPilot.Modules.Maintenance;

/// <summary>
/// Suppression sûre d'un fichier ou d'un dossier vide, résistante aux liens (jonctions, liens symboliques) :
/// l'objet est ouvert SANS suivre les points d'analyse, son chemin réel est relu depuis le handle
/// (GetFinalPathNameByHandle) et doit se trouver sous la racine autorisée, puis la suppression est demandée
/// sur ce même handle. Un dossier parent remplacé par une jonction entre l'énumération et la suppression est
/// donc détecté (le chemin réel sort de la racine) : aucune suppression hors de la catégorie n'est possible.
/// </summary>
internal static unsafe partial class SafeDelete
{
    private const uint DELETE = 0x00010000;
    private const uint FILE_READ_ATTRIBUTES = 0x0080;
    private const uint SYNCHRONIZE = 0x00100000;
    private const uint FILE_SHARE_ALL = 0x7;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const int FileBasicInfo = 0;
    private const int FileDispositionInfo = 4;
    private const int FileDispositionInfoEx = 21;
    private const uint FILE_DISPOSITION_FLAG_DELETE = 0x1;
    private const uint FILE_DISPOSITION_FLAG_POSIX_SEMANTICS = 0x2;
    private const uint FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_BASIC_INFO
    {
        public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime;
        public uint FileAttributes;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string fileName, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static partial uint GetFinalPathNameByHandle(SafeFileHandle file, char* buffer, uint length, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, void* info, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(SafeFileHandle file, int infoClass, void* info, uint size);

    /// <summary>Résultat d'une tentative de suppression.</summary>
    public enum Outcome { Deleted, InUse, Refused, Missing }

    /// <summary>
    /// Chemin réel (forme \\?\C:\…) d'un dossier racine, ou null s'il est absent, inaccessible ou lui-même un lien.
    /// </summary>
    public static string? RootFinalPath(string root)
    {
        using var h = CreateFile(root, FILE_READ_ATTRIBUTES | SYNCHRONIZE, FILE_SHARE_ALL, 0, OPEN_EXISTING,
            FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS, 0);
        if (h.IsInvalid) return null;
        if (!TryAttributes(h, out var attr) || (attr & FILE_ATTRIBUTE_REPARSE_POINT) != 0 || (attr & FILE_ATTRIBUTE_DIRECTORY) == 0) return null;
        return FinalPath(h);
    }

    /// <summary>Supprime un fichier (ou un dossier vide si <paramref name="directory"/>) situé sous <paramref name="rootFinal"/>.</summary>
    public static Outcome Delete(string path, string rootFinal, bool directory)
    {
        var flags = FILE_FLAG_OPEN_REPARSE_POINT | (directory ? FILE_FLAG_BACKUP_SEMANTICS : 0);
        using var h = CreateFile(path, DELETE | FILE_READ_ATTRIBUTES | SYNCHRONIZE, FILE_SHARE_ALL, 0, OPEN_EXISTING, flags, 0);
        if (h.IsInvalid)
        {
            var err = Marshal.GetLastPInvokeError();
            return err is 2 or 3 ? Outcome.Missing : Outcome.InUse; // 32 = partage refusé, 5 = accès refusé…
        }
        if (!TryAttributes(h, out var attr)) return Outcome.Refused;
        if ((attr & FILE_ATTRIBUTE_REPARSE_POINT) != 0) return Outcome.Refused;
        if (((attr & FILE_ATTRIBUTE_DIRECTORY) != 0) != directory) return Outcome.Refused;

        var final = FinalPath(h);
        if (final is null || !final.StartsWith(rootFinal + "\\", StringComparison.OrdinalIgnoreCase)) return Outcome.Refused;

        uint exFlags = FILE_DISPOSITION_FLAG_DELETE | FILE_DISPOSITION_FLAG_POSIX_SEMANTICS | FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE;
        if (SetFileInformationByHandle(h, FileDispositionInfoEx, &exFlags, sizeof(uint))) return Outcome.Deleted;
        var exError = Marshal.GetLastPInvokeError();
        if (exError is 145) return Outcome.InUse; // dossier non vide
        // Systèmes de fichiers sans FileDispositionInfoEx (FAT, anciennes versions) : suppression classique.
        byte deleteFile = 1;
        if (SetFileInformationByHandle(h, FileDispositionInfo, &deleteFile, 1)) return Outcome.Deleted;
        return Outcome.InUse;
    }

    private static bool TryAttributes(SafeFileHandle h, out uint attributes)
    {
        FILE_BASIC_INFO info;
        if (GetFileInformationByHandleEx(h, FileBasicInfo, &info, (uint)sizeof(FILE_BASIC_INFO)))
        {
            attributes = info.FileAttributes;
            return true;
        }
        attributes = 0;
        return false;
    }

    private static string? FinalPath(SafeFileHandle h)
    {
        const int size = 1024;
        var buffer = stackalloc char[size];
        var n = GetFinalPathNameByHandle(h, buffer, size, 0 /* FILE_NAME_NORMALIZED | VOLUME_NAME_DOS */);
        if (n == 0 || n >= size) return null;
        return new string(buffer, 0, (int)n).TrimEnd('\\');
    }
}

/// <summary>Corbeille de Windows (API du shell, compte de l'utilisateur courant, tous les lecteurs).</summary>
internal static partial class RecycleBin
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);

    [LibraryImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBin(nint hwnd, string? rootPath, uint flags);

    private const uint SHERB_NOCONFIRMATION = 0x1, SHERB_NOPROGRESSUI = 0x2, SHERB_NOSOUND = 0x4;

    /// <summary>Taille et nombre d'éléments de la corbeille (null si la requête échoue).</summary>
    public static (long Bytes, long Items)? Query()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        var hr = SHQueryRecycleBin(null, ref info);
        return hr == 0 ? (info.i64Size, info.i64NumItems) : null;
    }

    /// <summary>Vide la corbeille de tous les lecteurs (sans boîte de dialogue ni son). Exécuté sur un thread STA.</summary>
    public static bool Empty()
    {
        var hr = RunSta(() => SHEmptyRecycleBin(0, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND));
        // E_UNEXPECTED (0x8000FFFF) : corbeille déjà vide.
        return hr == 0 || hr == unchecked((int)0x8000FFFF);
    }

    private static T RunSta<T>(Func<T> func)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
        }) { IsBackground = true, Name = "PCPilot-RecycleBin" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromMinutes(10));
        if (error is not null) throw error;
        return result;
    }
}
