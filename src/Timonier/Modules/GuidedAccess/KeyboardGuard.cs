using Timonier.Core.Platform;

namespace Timonier.Modules.GuidedAccess;

/// <summary>
/// Crochet clavier bas niveau (WH_KEYBOARD_LL) sur un thread dédié avec sa propre boucle de messages : le rappel reste
/// rapide même si l'interface est occupée (Windows retire un crochet trop lent).
/// Il ne fait que <b>laisser passer ou avaler</b> les combinaisons système bloquées et repérer le geste de sortie
/// (Échap ×3 en 1,5 s ou Ctrl+Alt+Maj+P). Aucune frappe n'est enregistrée, conservée ni transmise.
/// </summary>
internal static unsafe class KeyboardGuard
{
    private static nint _hook;
    private static Thread? _thread;
    private static uint _threadId;

    // Configuration lue par le rappel (écrite avant l'installation, puis en lecture seule).
    private static volatile bool _blockShortcuts;
    private static volatile bool _blockAltF4;
    private static volatile bool _blockMedia;
    private static Action? _exitGesture;

    // Geste « Échap ×3 » : uniquement les instants des trois derniers appuis sur Échap (aucune autre touche n'est suivie).
    private static long _esc1, _esc2;

    public static bool IsInstalled => _hook != 0;

    /// <summary>Installe le crochet. <paramref name="exitGesture"/> est appelé sur le thread du crochet : il doit seulement poster vers l'interface.</summary>
    public static void Install(GuidedOptions options, Action exitGesture)
    {
        if (_thread is not null) return;
        _blockShortcuts = options.BlockShortcuts;
        _blockAltF4 = options.BlockAltF4;
        _blockMedia = !options.AllowMediaKeys;
        _exitGesture = exitGesture;
        _esc1 = _esc2 = 0;

        using var ready = new ManualResetEventSlim(false);
        Exception? failure = null;
        _thread = new Thread(() =>
        {
            _threadId = GuidedNative.GetCurrentThreadId();
            _hook = GuidedNative.SetWindowsHookEx(GuidedNative.WH_KEYBOARD_LL, &HookProc, GuidedNative.GetModuleHandle(null), 0);
            if (_hook == 0) failure = new System.ComponentModel.Win32Exception();
            ready.Set();
            if (_hook == 0) return;
            try
            {
                while (GuidedNative.GetMessage(out _, 0, 0, 0) > 0) { /* le crochet est appelé pendant GetMessage */ }
            }
            finally
            {
                GuidedNative.UnhookWindowsHookEx(_hook);
                _hook = 0;
            }
        })
        { IsBackground = true, Name = "Timonier.GuidedAccess.Keyboard" };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
        if (failure is not null || _hook == 0)
        {
            Uninstall();
            throw new InvalidOperationException("Impossible d'installer le filtre clavier.", failure);
        }
    }

    public static void Uninstall()
    {
        var thread = _thread;
        _thread = null;
        _exitGesture = null;
        if (thread is null) return;
        if (_threadId != 0) GuidedNative.PostThreadMessage(_threadId, GuidedNative.WM_QUIT, 0, 0);
        if (!thread.Join(TimeSpan.FromSeconds(2)))
        {
            Log.Warn("GuidedAccess", "le thread du filtre clavier ne s'est pas arrêté à temps");
            if (_hook != 0) { GuidedNative.UnhookWindowsHookEx(_hook); _hook = 0; }
        }
        _threadId = 0;
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static nint HookProc(int code, nuint wParam, nint lParam)
    {
        if (code == GuidedNative.HC_ACTION)
        {
            var k = (GuidedNative.KBDLLHOOKSTRUCT*)lParam;
            if (ShouldSwallow(k->vkCode, k->flags, (uint)wParam)) return 1;
        }
        return GuidedNative.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static bool ShouldSwallow(uint vk, uint flags, uint message)
    {
        var down = message is GuidedNative.WM_KEYDOWN or GuidedNative.WM_SYSKEYDOWN;
        var alt = (flags & GuidedNative.LLKHF_ALTDOWN) != 0;

        // Geste de sortie (détecté à l'appui uniquement).
        if (down && vk == GuidedNative.VK_ESCAPE && !alt && !IsDown(GuidedNative.VK_CONTROL))
        {
            var now = Environment.TickCount64;
            if (_esc1 != 0 && now - _esc1 <= 1500)
            {
                _esc1 = _esc2 = 0;
                Signal();
                return true;
            }
            _esc1 = _esc2;
            _esc2 = now;
            if (_esc1 != 0 && now - _esc1 > 1500) _esc1 = 0;
            return false;
        }
        if (vk == GuidedNative.VK_P && alt && IsDown(GuidedNative.VK_CONTROL) && IsDown(GuidedNative.VK_SHIFT))
        {
            if (down) Signal();
            return true;
        }

        if (_blockShortcuts)
        {
            switch ((int)vk)
            {
                case GuidedNative.VK_LWIN or GuidedNative.VK_RWIN:
                    return true;                                                   // menu Démarrer et raccourcis Win+…
                case GuidedNative.VK_TAB when alt:
                    return true;                                                   // Alt+Tab, Alt+Maj+Tab
                case GuidedNative.VK_ESCAPE when alt || IsDown(GuidedNative.VK_CONTROL):
                    return true;                                                   // Alt+Échap, Ctrl+Échap, Ctrl+Maj+Échap
                case GuidedNative.VK_SPACE when alt:
                    return true;                                                   // menu système de la fenêtre
                case GuidedNative.VK_BROWSER_SEARCH or GuidedNative.VK_BROWSER_HOME:
                case >= GuidedNative.VK_LAUNCH_MAIL and <= GuidedNative.VK_LAUNCH_APP2:
                    return true;                                                   // touches qui lancent une application
            }
        }
        if (_blockAltF4 && vk == GuidedNative.VK_F4 && alt) return true;
        if (_blockMedia && vk is >= GuidedNative.VK_VOLUME_MUTE and <= GuidedNative.VK_MEDIA_PLAY_PAUSE) return true;
        return false;
    }

    private static bool IsDown(int vk) => (GuidedNative.GetAsyncKeyState(vk) & 0x8000) != 0;

    private static void Signal()
    {
        try { _exitGesture?.Invoke(); } catch { /* jamais d'exception dans un crochet */ }
    }
}
