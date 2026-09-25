using Timonier.Broker;
using Timonier.Core.Platform;
using Timonier.UI.Shell;

namespace Timonier;

public static class Program
{
    public const string ActivationEventName = @"Local\Timonier.Activate";
    private const string MutexName = @"Local\Timonier.SingleInstance";

    [STAThread]
    public static int Main(string[] args)
    {
        // Mode broker : processus élevé sans interface, lancé par l'UI via UAC.
        if (args.Length > 0 && args[0] == "--broker")
            return BrokerServer.Run(args[1..]);

        // Auto-test du canal broker (Debug uniquement, sans élévation, n'applique rien).
        if (args.Length == 1 && args[0] == "--selftest-broker")
            return BrokerSelfTest.Run();

#if DEBUG
        // Mode capture (développement / documentation, builds Debug uniquement) :
        // --capture fichier.png [idPage] [light|dark] [paramètre] [défilement] [langue]
        // Rend la fenêtre hors écran dans un PNG puis quitte. N'applique aucun réglage.
        if (args.Length >= 2 && args[0] == "--capture")
        {
            var capture = new App
            {
                Capture = new CaptureRequest(args[1], args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null,
                    args.Length > 4 ? args[4] : null,
                    args.Length > 5 && double.TryParse(args[5], System.Globalization.CultureInfo.InvariantCulture, out var scroll) ? scroll : null,
                    args.Length > 6 && Core.Localization.Languages.Find(args[6]) is { } lang ? lang.Code : null),
            };
            capture.InitializeComponent();
            return capture.Run();
        }
#endif

        using var mutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            // Une instance tourne déjà : on lui demande de se montrer, puis on quitte.
            try
            {
                using var activate = EventWaitHandle.OpenExisting(ActivationEventName);
                activate.Set();
            }
            catch (Exception ex) { Log.Warn("Program", "activation : " + ex.Message); }
            return 0;
        }

        var app = new App { StartInBackground = args.Contains("--background") };
        app.InitializeComponent();
        return app.Run();
    }
}
