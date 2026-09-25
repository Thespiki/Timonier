namespace Timonier.Core.Search;

/// <summary>
/// Groupes de synonymes FR/EN (concepts). Un mot ou une expression d'un groupe retrouve tous les autres.
/// Les modules peuvent enrichir le dictionnaire via <see cref="AddGroup"/> au démarrage (avant la construction de l'index).
/// </summary>
public static class Synonyms
{
    public static readonly HashSet<string> OffWords =
    [
        "desactiver", "desactive", "desactivez", "desactivation", "couper", "coupe", "eteindre", "eteins", "bloquer", "bloque",
        "stopper", "arreter", "enlever", "retirer", "masquer", "cacher", "empecher", "interdire",
        "virer", "off", "disable", "block", "hide", "remove", "stop",
    ];

    public static readonly HashSet<string> OnWords =
    [
        "activer", "active", "activez", "activation", "allumer", "allume", "autoriser", "autorise", "afficher", "affiche",
        "montrer", "reactiver", "debloquer", "remettre", "on", "enable", "show", "allow", "unblock",
    ];

    public static readonly HashSet<string> OpenWords = ["ouvrir", "ouvre", "lancer", "lance", "aller", "voir", "open", "launch", "go"];

    public static bool IsIntentWord(string token) => OffWords.Contains(token) || OnWords.Contains(token) || OpenWords.Contains(token);

    /// <summary>
    /// Mots d'intention de la langue active (section « search » du fichier de traduction : "on", "off", "open").
    /// Les listes français/anglais ci-dessus restent toujours actives.
    /// </summary>
    public static void AddLocalizedIntentWords(IReadOnlyDictionary<string, string[]> words)
    {
        foreach (var (key, set) in new[] { ("on", OnWords), ("off", OffWords), ("open", OpenWords) })
            if (words.TryGetValue(key, out var list))
                foreach (var w in list)
                    if (TextNormalizer.Normalize(w) is { Length: > 0 } n && !n.Contains(' ')) set.Add(n);
    }

    private static readonly List<string[]> Groups =
    [
        ["wifi", "sans fil", "wlan", "wireless", "reseau sans fil"],
        ["bluetooth", "bt", "appairage", "pairing"],
        ["ecran", "affichage", "display", "moniteur", "monitor", "resolution", "hdr"],
        ["luminosite", "brightness", "lumiere bleue", "eclairage nocturne", "night light"],
        ["son", "audio", "volume", "haut parleur", "speaker", "sound", "casque"],
        ["micro", "microphone", "mic", "micros"],
        ["camera", "webcam", "cam", "cameras"],
        ["telemetrie", "telemetry", "diagnostic", "donnees de diagnostic", "tracking", "pistage", "suivi", "espionnage", "espion", "collecte", "mouchard"],
        ["confidentialite", "privacy", "vie privee", "donnees personnelles", "rgpd"],
        ["pub", "publicite", "ads", "advertising", "annonce", "reclame", "sponsorise", "suggestion", "recommandation", "promotion"],
        ["veille", "sleep", "mise en veille", "hibernation", "hiberner", "suspendre"],
        ["batterie", "battery", "autonomie", "economie energie", "economiseur", "charge"],
        ["alimentation", "energie", "power", "plan alimentation", "mode alimentation", "power plan", "performances maximales"],
        ["demarrage", "startup", "boot", "lancement automatique", "ouverture session", "autostart", "au demarrage"],
        ["performance", "rapide", "vitesse", "lent", "lenteur", "accelerer", "optimiser", "optimisation", "speed", "fluidite", "rame", "ralenti"],
        ["jeu", "game", "gaming", "gamer", "fps", "joueur"],
        ["theme", "mode sombre", "dark mode", "sombre", "dark", "mode clair", "light mode", "couleur", "accent", "apparence"],
        ["barre tache", "taskbar", "barre du bas"],
        ["menu demarrer", "start menu", "demarrer", "start"],
        ["explorateur", "explorer", "explorateur fichier", "gestionnaire fichier", "file explorer"],
        ["extension", "extension fichier", "type fichier", "file extension"],
        ["fichier cache", "fichier masque", "hidden file", "fichier invisible"],
        ["mise jour", "maj", "update", "windows update", "actualisation", "patch"],
        ["antivirus", "defender", "microsoft defender", "malware", "virus", "logiciel malveillant", "ransomware", "rancongiciel"],
        ["securite", "security", "protection", "durcissement", "hardening", "securiser"],
        ["pare feu", "parefeu", "firewall"],
        ["internet", "reseau", "network", "connexion", "ethernet", "net", "wifi"],
        ["dns", "serveur dns", "resolveur", "doh", "dns over https"],
        ["clavier", "keyboard", "touche", "raccourci", "shortcut", "raccourci clavier"],
        ["souris", "mouse", "pointeur", "curseur", "cursor", "pave tactile", "touchpad"],
        ["tactile", "touch", "ecran tactile", "stylet", "pen"],
        ["imprimante", "printer", "impression", "imprimer", "spouleur"],
        ["usb", "cle usb", "stockage amovible", "disque externe", "clef usb", "support amovible"],
        ["peripherique", "device", "materiel", "hardware", "composant", "gestionnaire peripherique"],
        ["utilisateur", "compte", "account", "user", "session", "profil utilisateur"],
        ["enfant", "parental", "controle parental", "famille", "kids", "ado", "adolescent"],
        ["kiosque", "kiosk", "borne", "affichage dynamique", "assigned access", "acces affecte"],
        ["acces guide", "guided access", "verrouillage application", "mode focus", "une seule application", "verrouiller application"],
        ["mot passe", "password", "pin", "code", "code pin"],
        ["nettoyer", "nettoyage", "clean", "cleanup", "menage", "fichier temporaire", "temp", "liberer espace", "espace disque", "corbeille"],
        ["disque", "stockage", "storage", "ssd", "hdd", "disque dur", "partition"],
        ["application", "apps", "app", "logiciel", "programme", "software", "appli"],
        ["installer", "installation", "install", "telecharger logiciel", "winget"],
        ["desinstaller", "uninstall", "desinstallation", "supprimer application"],
        ["bloatware", "preinstalle", "inutile", "crapware", "applis inutiles", "debloat", "debloquer windows"],
        ["notification", "alerte", "toast", "centre notification", "ne pas deranger"],
        ["copilot", "ia", "ai", "intelligence artificielle", "recall", "rappel", "click to do"],
        ["cortana", "recherche windows", "bing", "recherche web", "search"],
        ["widget", "actualite", "news", "meteo", "weather"],
        ["onedrive", "cloud", "synchronisation", "sync", "sauvegarde cloud"],
        ["edge", "navigateur", "browser", "chrome", "firefox"],
        ["xbox", "game bar", "barre jeu", "game dvr", "enregistrement jeu"],
        ["restauration", "point restauration", "restore", "restore point", "retour arriere"],
        ["sauvegarde", "backup", "historique fichier", "copie securite"],
        ["reparer", "reparation", "repair", "sfc", "dism", "depanner", "depannage", "corriger", "probleme", "bug"],
        ["service", "services windows", "svchost"],
        ["tache planifiee", "planificateur", "scheduled task", "tache"],
        ["pilote", "driver", "mise jour pilote"],
        ["heure", "horloge", "date", "clock", "fuseau horaire", "synchronisation heure"],
        ["langue", "language", "region", "format regional"],
        ["accessibilite", "accessibility", "loupe", "narrateur", "contraste eleve", "touche remanente", "sticky keys"],
        ["localisation", "location", "gps", "position", "geolocalisation"],
        ["hosts", "fichier hosts", "blocage site", "bloquer site", "liste noire"],
        ["vpn", "proxy"],
        ["bureau distance", "remote desktop", "rdp", "assistance distance", "prise main distance"],
        ["chiffrement", "bitlocker", "encryption", "chiffrer", "cryptage"],
        ["tpm", "secure boot", "demarrage securise", "uefi", "bios"],
        ["gestionnaire tache", "task manager", "taskmgr", "processus"],
        ["registre", "registry", "regedit"],
        ["invite commande", "cmd", "terminal", "powershell", "console"],
        ["panneau configuration", "control panel", "panneau de config"],
        ["parametre", "reglage", "settings", "option", "configuration", "preference"],
        ["fond ecran", "wallpaper", "arriere plan", "image fond", "background"],
        ["ecran verrouillage", "lock screen", "ecran connexion", "verrouillage"],
        ["menu contextuel", "clic droit", "context menu", "menu clic droit"],
        ["animation", "effet visuel", "transparence", "visual effect", "effet transparence"],
        ["redemarrer", "reboot", "restart", "relancer"],
        ["arreter pc", "shutdown", "eteindre pc", "arret"],
        ["temperature", "chauffe", "surchauffe", "ventilateur", "fan", "thermique"],
        ["memoire", "ram", "memoire vive"],
        ["processeur", "cpu", "coeur", "core"],
        ["carte graphique", "gpu", "graphique", "nvidia", "amd radeon", "intel graphics"],
        ["telephone", "phone link", "mobile", "smartphone", "lien telephone"],
        ["presse papier", "clipboard", "copier coller"],
        ["capture ecran", "screenshot", "outil capture", "impr ecran"],
        ["hibernation", "demarrage rapide", "fast startup", "veille prolongee"],
        ["journal", "historique", "log", "annuler", "undo", "retour"],
        ["profil", "preset", "configuration type", "installation initiale", "nouveau pc", "setup", "premiere installation"],
    ];

    private static Dictionary<string, int[]>? _wordIndex;
    private static List<(string Phrase, int Concept)>? _phrases;
    private static readonly Lock Gate = new();

    /// <summary>Ajoute un groupe de synonymes (avant la première recherche).</summary>
    public static void AddGroup(params string[] terms)
    {
        lock (Gate)
        {
            Groups.Add(terms);
            _wordIndex = null;
            _phrases = null;
        }
    }

    private static void EnsureIndex()
    {
        if (_wordIndex is not null && _phrases is not null) return;
        lock (Gate)
        {
            if (_wordIndex is not null && _phrases is not null) return;
            var words = new Dictionary<string, HashSet<int>>();
            var phrases = new List<(string, int)>();
            for (var i = 0; i < Groups.Count; i++)
            {
                foreach (var term in Groups[i])
                {
                    var canonical = string.Join(' ', TextNormalizer.Tokens(term));
                    if (canonical.Length == 0) continue;
                    if (canonical.Contains(' ')) phrases.Add((canonical, i));
                    else
                    {
                        if (!words.TryGetValue(canonical, out var set)) words[canonical] = set = [];
                        set.Add(i);
                    }
                }
            }
            _phrases = [.. phrases.OrderByDescending(p => p.Item1.Length)];
            _wordIndex = words.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        }
    }

    public static int[] ConceptsOfWord(string token)
    {
        EnsureIndex();
        return _wordIndex!.TryGetValue(token, out var c) ? c : [];
    }

    /// <summary>Expressions multi-mots présentes dans une suite de jetons normalisés (séparés par des espaces).</summary>
    public static IEnumerable<(string Phrase, int Concept)> PhrasesIn(string canonicalTokens)
    {
        EnsureIndex();
        var padded = " " + canonicalTokens + " ";
        foreach (var (phrase, concept) in _phrases!)
            if (padded.Contains(" " + phrase + " ", StringComparison.Ordinal))
                yield return (phrase, concept);
    }

    /// <summary>Tous les concepts présents dans un texte (mots et expressions).</summary>
    public static IEnumerable<int> ConceptsIn(string text)
    {
        var tokens = TextNormalizer.Tokens(text);
        var set = new HashSet<int>();
        foreach (var t in tokens)
            foreach (var c in ConceptsOfWord(t)) set.Add(c);
        foreach (var (_, c) in PhrasesIn(string.Join(' ', tokens))) set.Add(c);
        return set;
    }
}
