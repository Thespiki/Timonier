# PC Pilot — Guide de développement des modules

PC Pilot est un centre de contrôle local pour Windows 10/11 (C# / .NET 10 / WPF). Interface **en français**.
Tout tourne en local, sans télémétrie, avec une empreinte minimale (le PC de test est un Pentium N6000, 8 Go).

## 0. Règles absolues

### Sécurité de la machine de développement
- **N'applique JAMAIS de modification système sur ce PC** : pas de `reg add/delete`, `sc config`, `netsh … set`,
  `Set-ItemProperty` sur HKLM/HKCU, `schtasks /change`, désinstallation d'app, etc.
  Les commandes **en lecture seule** sont autorisées pour vérifier tes hypothèses
  (`reg query`, `Get-ItemProperty`, `Get-Service`, `Get-ScheduledTask`, `Get-CimInstance`…).
- Ne lance jamais le broker (`--broker`) et ne déclenche jamais d'invite UAC.
- Pour tester l'interface, utilise uniquement le mode capture (voir §7), qui n'applique rien.

### Périmètre des fichiers
- Tu ne modifies **que** ton dossier `src/PCPilot/Modules/<TonModule>/`. Aucun autre fichier (Core, Broker, UI, csproj).
- Pas de nouveau paquet NuGet. Disponibles : CommunityToolkit.Mvvm, System.Management (WMI),
  System.ServiceProcess.ServiceController, System.DirectoryServices.AccountManagement, System.Diagnostics.EventLog,
  WinForms (uniquement si indispensable), et les API WinRT (`Windows.*`, TFM `net10.0-windows10.0.22621.0`).
- S'il manque quelque chose dans le cœur, contourne localement (P/Invoke dans ton dossier…) et **signale-le** dans ton rapport final.

### Sécurité du code (anti-injection)
1. **Jamais de shell, jamais de concaténation de commande.** Utilise `ProcessRunner.RunAsync(SystemTool.X, [args…])`
   (ArgumentList, chemins absolus en liste blanche) ou mieux, une API Win32/WMI/WinRT/COM.
2. **PowerShell** : uniquement via `PowerShellRunner.RunAsync(scriptConstant, parametres)`. Le script est une constante ;
   les données passent par `$env:PCP_NOM` (dictionnaire `parametres` : clé `NOM` → variable `PCP_NOM`). Jamais d'interpolation.
3. **Tout paramètre d'action** est validé dans `ValidateParameters(...)` avec `PcPilot.Core.Security.Validate` (liste blanche de formats,
   longueurs bornées). Le broker revalide systématiquement : ne fais jamais confiance à l'interface.
4. **Aucun chemin/identifiant arbitraire** ne doit atteindre une opération admin sans validation : ex. un identifiant
   de périphérique doit exister dans l'énumération actuelle, un nom de service doit être dans ta liste autorisée, etc.
5. Les actions sensibles (créer un compte admin, installer un logiciel hors catalogue, ouverture de session automatique,
   désactiver un composant système…) : `RequiresElevatedConfirmation => true` (confirmation affichée par le processus élevé).
6. Aucun secret en clair sur disque ni dans les logs. Aucune connexion réseau initiée par PC Pilot lui-même
   (winget peut en faire, c'est son rôle et c'est dit à l'utilisateur).
7. **Ne jamais affaiblir la sécurité** : pas de réglage pour désactiver Defender, le pare-feu, l'UAC, SmartScreen,
   Secure Boot, les mises à jour de sécurité de façon permanente, etc. On peut *informer* de leur état et proposer de les *activer*.

## 1. Architecture en bref

```
src/PCPilot/
  Core/Model        TweakDefinition (réglage déclaratif), Operation (RegSet, RegDeleteValue, RegDeleteKey,
                    ServiceStartOp, ScheduledTaskOp, RunToolOp, BroadcastSettingChangeOp), Requirement/Requires
  Core/Engine       TweakEngine (appliquer/annuler), OperationExecutor, StateDetector, Journal (annulation)
  Core/Catalog      IModule, ModuleRegistry, IActionHandler, PageInfo, CategoryInfo, SearchEntry, IHealthCheck, QuickAction
  Core/Platform     SystemProfile (+Service), ProcessRunner/SystemTool, PowerShellRunner, RegistryAccess, ServiceConfig,
                    TaskSchedulerHelper, WmiQuery, Format, Native, Log, AppPaths
  Core/Security     Validate (+ValidationException), PinHasher
  Core/Search       SearchEngine (floue, synonymes), Synonyms.AddGroup(...)
  Core/Settings     AppSettings / SettingsStore (ModuleData pour tes préférences)
  Broker/           processus élevé à la demande (tu n'y touches pas)
  UI/               Shell (MainWindow), Controls (TweakCard, TweakListView, PageHeader, PageScaffold), Theme, Services
  Modules/<X>/      ← TON CODE
```

- L'interface tourne **sans élévation**. Un réglage/une action qui exige l'admin est envoyé automatiquement au **broker**
  (processus élevé lancé via UAC à la demande) : tu n'as rien de spécial à faire, sauf déclarer correctement `RequiresAdmin`.
- `IModule.Register` est appelé **aussi dans le broker** (pas d'interface graphique) : il ne doit **que** déclarer
  (réglages, actions, pages via fabriques lambda, entrées de recherche…). Aucun accès à `AppHost`, aucune création d'UI,
  aucune E/S lente (WMI, réseau, fichiers volumineux) dans `Register`.
- Les `IActionHandler.ExecuteAsync` s'exécutent dans le broker si `RequiresAdmin` : **aucune référence à AppHost/UI** dedans.

## 2. Déclarer un module

```csharp
namespace PcPilot.Modules.Privacy;

public sealed class PrivacyModule : IModule   // public, constructeur sans paramètre : découvert automatiquement
{
    public const string Category = "privacy";
    public void Register(ModuleRegistry r)
    {
        r.AddCategory(new CategoryInfo(Category, "Confidentialité", "", "Description courte de la catégorie."));
        r.AddTweaks(PrivacyTweaks.All());              // réglages déclaratifs
        r.AddAction(new MyActionHandler());             // actions paramétrées
        r.AddPage(new PageInfo("privacy", "Confidentialité", "", NavSection.Settings, 10, () => new PrivacyPage())
        {
            CategoryId = Category,                      // la recherche d'un réglage de cette catégorie ouvre cette page
            Description = "…", Keywords = ["vie privée", "télémétrie"],
        });
        r.AddHealthCheck(HealthCheck.Sync("privacy.score", "Confidentialité", "", "privacy", () => …));
        r.AddQuickAction(new QuickAction("privacy.quick", "Titre", "", "Description", async () => { … }));
        r.AddSearchEntry(new SearchEntry { Id = "privacy.permissions", Title = "Autorisations des applications", PageId = "privacy", … });
        Synonyms.AddGroup("mot", "synonyme", "expression multi mots");   // si utile
    }
}
```
Sans page dédiée, une catégorie obtient automatiquement une page générique listant ses réglages.

## 3. Réglages déclaratifs (le cœur du produit)

```csharp
const string Adv = @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo";
const string AdvPolicy = @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo";

Tweak.Toggle("privacy.ads.advertisingid", "Identifiant de publicité",
        "Permet aux applications de vous suivre pour afficher des publicités personnalisées.")
    .In("privacy", "Publicité")                         // catégorie, groupe (sous-titre dans la page)
    .Keywords("pub", "ads", "tracking", "ciblage")        // mots pour la recherche (FR + EN)
    .WhenOn(Reg.CuDword(Adv, "Enabled", 1), Reg.LmDel(AdvPolicy, "DisabledByGroupPolicy"))
    .WhenOff(Reg.CuDword(Adv, "Enabled", 0), Reg.LmDword(AdvPolicy, "DisabledByGroupPolicy", 1))
    .WindowsDefault(TweakDefinition.On)                   // état d'origine de Windows (utilisé si rien ne correspond)
    .Recommend(TweakDefinition.Off)                       // recommandation de PC Pilot
    .Build();
```

**Conventions (importantes) :**
- **Toggle : `On` = la fonctionnalité Windows nommée par le titre est active.** Le titre nomme la fonctionnalité
  (« Identifiant de publicité », « Stockage USB », « Télémétrie des tâches planifiées ») et non l'action (« Désactiver X »).
  Les libellés par défaut sont « Activé »/« Désactivé » ; `.Labels("Autorisé", "Bloqué")` pour les adapter.
- Le côté « Windows par défaut » supprime généralement la valeur de stratégie (`Reg.LmDel`/`Reg.CuDel`) plutôt que
  d'écrire une valeur : c'est plus propre et c'est ce que la détection reconnaît comme état d'origine.
- `Choice` : `.Option("cle", "Libellé", ops…)` ×N (≥ 2). `Action` : `.Run(ops…)` (bouton ; `Label` = « Exécuter »).
- `RequiresAdmin` est **calculé** à partir des opérations (HKLM, HKU\.DEFAULT, HKCU\Software\Policies, services, tâches
  système, `RunToolOp(admin:true)`). `.AlwaysAdmin()` pour forcer.
- `.Requires(Requires.Windows11)`, `Requires.ProOrHigher`, `Requires.EnterpriseOrEducation`, `Requires.Battery`,
  `Requires.Bluetooth`, `Requires.Wifi`, `Requires.Camera`, `Requires.NvidiaGpu`, `Requires.When(p => …, "raison FR")`,
  combinables avec `.And(...)`. La raison est affichée telle quelle : sois clair.
- `.RecommendWhen(p => p.Tier == PerformanceTier.Low ? "off" : null)` : recommandation **adaptée au matériel**
  (null = pas de recommandation). `p` est un `SystemProfile` (édition, build, portable, batterie, GPU, disque système
  HDD/SSD, RAM, gamme de performance, tactile, VM, domaine/MDM…).
- `.Effect(ApplyEffect.RestartExplorer | SignOut | Reboot)` : effet nécessaire pour que ce soit pris en compte.
- `.Risk(RiskLevel.Moderate)` si ça peut casser une fonctionnalité (confirmation affichée) ; `Advanced` = masqué hors mode avancé.
- `.Warning("…")` : conséquence concrète à connaître (affichée avant application).
- `.Tags("gaming", "lowend", "privacy-max", "family", "kiosk", "security", "battery", "office", "dev")` : utilisés par les profils d'installation.
- `.Detect(() => "on"/"off"/clé/null)` : détection personnalisée si les opérations ne suffisent pas (lecture seule, rapide).
- Opérations dispo : `Reg.CuDword/LmDword/DefDword/CuString/LmString/Cu(kind)/Lm(kind)/CuDel/LmDel/CuDelKey/LmDelKey`,
  `Sys.Service(nom, ServiceStartKind.X)`, `Sys.DisableTask(@"\Microsoft\Windows\…")`, `Sys.EnableTask(...)`,
  `Sys.Tool(SystemTool.PowerCfg, admin, "explication FR", args…)` (non annulable), `Sys.Broadcast()`.
- Chaque réglage est **annulable** automatiquement (journal) sauf `RunToolOp`. Un service ou une tâche absents sont ignorés
  proprement (l'état est considéré conforme) : tu peux lister des éléments qui n'existent pas sur toutes les versions.
- **Exactitude** : chaque chemin/valeur de registre doit être réel et documenté (Microsoft Learn / ADMX). Vérifie en lecture
  seule sur ce PC quand c'est possible. Décris précisément l'effet réel, sans exagérer.

## 4. Actions paramétrées

```csharp
public sealed class SetDnsAction : IActionHandler
{
    public string Id => "network.dns.set";
    public string Title => "Changer les serveurs DNS";
    public bool RequiresAdmin => true;
    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        Validate.IpAddress(Validate.Required(p, "primary", 45));
        if (Validate.Optional(p, "secondary", 45) is { } s) Validate.IpAddress(s);
        Validate.Int(p, "ifIndex", 0, 100000);
    }
    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);   // toujours revalider ici (défense en profondeur)
        ctx.Progress?.Report("…");
        // API / WMI / ProcessRunner(ArgumentList) — jamais de chaîne de commande
        return ActionResult.Ok("DNS modifiés.");
    }
}
```
- Appel depuis l'interface : `var outcome = await AppHost.Engine.RunActionAsync("network.dns.set", dict, progress);`
  puis `AppHost.Toasts.ShowOutcome(outcome);` (le moteur passe par le broker si besoin, une seule invite UAC par session).
- `ctx.ApplyJournaled(sourceId, titre, libellé, operations)` exécute des `Operation` et crée une entrée **annulable**
  dans le journal : à privilégier pour tout ce qui s'exprime en registre/services/tâches.
- Données de retour : `ActionResult.Ok(message, new Dictionary<string,string>{…})` → `outcome.Data`.
- Lecture seule sans admin (listes, états) : fais-le directement dans ta page/ton service (pas besoin d'action).
- `ctx.UserSid` : SID de l'utilisateur de l'interface (dans le broker, HKCU est automatiquement remappé sur ce SID
  par `RegistryAccess.OpenRoot(RegHive.CurrentUser, ctx.UserSid)`).

## 5. Interface

- Pages = `UserControl` (XAML ou code). Structure standard :
  ```xml
  <UserControl x:Class="PcPilot.Modules.Privacy.PrivacyPage"
               xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
               xmlns:controls="clr-namespace:PcPilot.UI.Controls">
    <ScrollViewer Style="{StaticResource Pp.PageScroll}">
      <StackPanel Style="{StaticResource Pp.PageStack}">
        <controls:PageHeader Title="Confidentialité" Subtitle="…" Glyph="&#xE72E;" />
        <Border Style="{StaticResource Pp.Card}"> … </Border>
        <ContentControl x:Name="TweaksHost" />   <!-- en code : TweaksHost.Content = new TweakListView("privacy"); -->
      </StackPanel>
    </ScrollViewer>
  </UserControl>
  ```
  En code : `var stack = PageScaffold.Create(this, "Titre", "Sous-titre", "");` puis `stack.Children.Add(...)`.
  Aides : `PageScaffold.Section(titre)`, `.Card(element)`, `.InfoBar(texte, glyph, "Pp.InfoBar[.Success|.Warning|.Danger]")`, `.KeyValue(k, v)`.
- **Liste de réglages** : `new TweakListView("categorie")` ou `new TweakListView(tweaks)` (cartes, états, badges,
  « Ce que ça change », recommandations, application groupée). Si ta page l'intègre, implémente `INavigationAware` et
  transmets `"tweak:<id>"` à `list.Highlight(id)` (la recherche l'utilise).
- **Styles** (StaticResource) : `Pp.PageScroll`, `Pp.PageStack`, `Pp.PageTitle`, `Pp.PageSubtitle`, `Pp.SectionTitle`, `Pp.CardTitle`,
  `Pp.Body`, `Pp.Caption`, `Pp.Metric`, `Pp.Icon` (TextBlock, police d'icônes), `Pp.Card`, `Pp.CardInteractive`, `Pp.Badge`, `Pp.BadgeText`,
  `Pp.InfoBar`, `Pp.InfoBar.Success/Warning/Danger`, `Pp.Button`, `Pp.AccentButton`, `Pp.DangerButton`, `Pp.SubtleButton`, `Pp.LinkButton`,
  `Pp.ToggleSwitch` (sur CheckBox), `Pp.SearchBox` (TextBox, Tag = placeholder), `Pp.ResultItem`.
- **Couleurs** (toujours `DynamicResource`, thème clair/sombre à chaud) : `Pp.WindowBackground`, `Pp.ContentBackground`,
  `Pp.CardBackground`, `Pp.CardHover`, `Pp.CardBorder`, `Pp.CardSecondary`, `Pp.Divider`, `Pp.TextPrimary`, `Pp.TextSecondary`,
  `Pp.TextTertiary`, `Pp.TextOnAccent`, `Pp.ControlFill`, `Pp.ControlStroke`, `Pp.SubtleHover`, `Pp.Accent`, `Pp.AccentText`,
  `Pp.AccentSubtle`, `Pp.Success(+Background)`, `Pp.Warning(+Background)`, `Pp.Danger(+Background)`, `Pp.Info(+Background)`,
  `Pp.Neutral(+Background)`. **Aucune couleur codée en dur.**
- Convertisseurs globaux : `BoolToVisibility`, `InverseBoolToVisibility`, `NullToVisibility`, `StringToVisibility`, `InverseBool`.
- Contrôles standard (TextBox, ComboBox, ListView, DataGrid, ProgressBar, Slider, PasswordBox…) : style Fluent automatique.
- Icônes : police Segoe Fluent Icons (`Style="{StaticResource Pp.Icon}" Text="&#xE72E;"`). Glyphes utiles :
  E72E cadenas, E7EF bouclier admin, E713 réglages, E771 personnaliser, E7F8 PC portable, E9D9 performances, E83F batterie,
  E701 Wi-Fi, E702 Bluetooth, E714 caméra, E720 micro, E88E USB, E772 périphériques, E774 globe/réseau, E968 réseau,
  E77B utilisateur, E716 groupe, EA86 kiosque/app, E7C4 accès guidé/verrou, E90F outils, E74D supprimer, E896 télécharger,
  E895 synchro, E777 redémarrage, E7BA avertissement, E946 info, E73E coche, E711 annuler/fermer, E7E8 énergie, EA8F santé,
  E8B7 dossier, E81C historique, E74C catalogue/apps, E7B8 console, E9F5 mémoire, E950 puce CPU, E7F4 écran, E8D7 tâche,
  EDA2 localisation, E8F1 bibliothèque, E80F accueil, E721 recherche, E946 info, E8A7 lancer, E768 lecture.
- Services UI (thread UI uniquement) : `AppHost.Dialogs.ConfirmAsync/AlertAsync/PromptAsync/ShowAsync`,
  `AppHost.Toasts.Show(msg, ToastKind)`, `AppHost.Toasts.ShowOutcome(outcome)`, `AppHost.Navigator.Navigate(pageId, param)`,
  `AppHost.Profile` (SystemProfile), `AppHost.Registry`, `AppHost.Engine`, `AppHost.Background.Acquire("raison")` (garde
  l'app en arrière-plan tant que le jeton n'est pas libéré), `AppHost.Settings.ModuleData["module.cle"]` + `SettingsStore.Save()`.
- **Performance** : rien de lourd sur le thread UI (WMI, fichiers, registre massif → `Task.Run` puis retour UI via `await`).
  Chargement paresseux à la première visite. Minuteurs de rafraîchissement **uniquement quand la page est visible**
  (démarrer sur `Loaded`, arrêter sur `Unloaded`). Pas de polling en arrière-plan.
- Textes : français, clairs, honnêtes. Explique les limites (« Windows peut réactiver ce service lors d'une mise à jour majeure »).

## 6. Identifiants et navigation (réservés — ne pas empiéter)

| Module (dossier) | Page id | Section / ordre | Catégorie(s) | Préfixe des ids |
|---|---|---|---|---|
| Dashboard | `dashboard` | Overview / 0 | — | `dashboard.` |
| Privacy | `privacy` | Settings / 10 | `privacy` | `privacy.` |
| Customization | `customization` | Settings / 20 | `customization` | `custom.` |
| Performance | `performance` | Settings / 30 | `performance` | `perf.` |
| Network | `network` | Settings / 40 | `network` | `network.` |
| Security | `security` | Control / 10 | `security` | `security.` |
| Devices | `devices` | Control / 20 | `devices` | `devices.` |
| Users | `users` | Control / 30 | `users` | `users.` |
| Kiosk | `kiosk` | Control / 40 | `kiosk` | `kiosk.` |
| GuidedAccess | `guided` | Control / 50 | — | `guided.` |
| Profiles | `profiles` | Tools / 5 | — | `profiles.` |
| Apps | `apps` | Tools / 10 | `apps` | `apps.` |
| Startup | `startup` | Tools / 20 | `startup` | `startup.` |
| Maintenance | `maintenance` | Tools / 30 | `maintenance` | `maintenance.` |
| WindowsTools | `wintools` | Tools / 40 | — | `wintools.` |
| AppPages | `journal`, `transparency`, `settings` | App / 10, 20, 30 | — | `app.` |

Ne duplique pas un réglage d'un autre module (voir la colonne « périmètre » donnée dans ta mission).

## 6 bis. Modules terminés : les exemples de référence

`Modules/Customization` (59 réglages, page riche avec aperçus et barre de navigation par groupe) et `Modules/Privacy`
(79 réglages, score, filtre « à revoir ») sont terminés : inspire-toi de leur niveau de finition et de leurs patterns
(détections personnalisées, actions journalisées, entrées de recherche, synonymes). Ne duplique aucun de leurs réglages.

Nouveautés du cœur : `TweakListView.GroupNames` et `TweakListView.ScrollToGroup(nom)` (barre de navigation par groupe).
**Moindre privilège** : le broker refuse désormais tout réglage/action dont `RequiresAdmin` est faux — une action non-admin
s'exécute toujours dans le processus de l'interface. Déclare donc `RequiresAdmin => true` uniquement quand c'est nécessaire,
et ne compte jamais sur une exécution élevée pour une action déclarée non-admin.

## 7. Cycle de travail

```powershell
. .\tools\dev.ps1                 # charge Build / Capture (dotnet + git sur le PATH)
Build                             # compile ; affiche erreurs/avertissements
Capture -Page "privacy" -Theme light -Out "$env:TEMP\pcp-<module>\privacy-light.png"
Capture -NoBuild -Page "privacy" -Theme dark -Out "$env:TEMP\pcp-<module>\privacy-dark.png"
```
Le mode capture rend la fenêtre hors écran dans un PNG (≈ 5 s) sans rien appliquer : **ouvre l'image (outil Read) et
corrige la mise en page** jusqu'à ce qu'elle soit propre dans les deux thèmes. Consulte aussi `%LOCALAPPDATA%\PCPilot\logs\pcpilot.log`
(lignes `[ERROR]`). `Registry.Errors` (doublons, catégories inconnues) apparaît dans le log de démarrage si tu ajoutes un `Log.Warn`.

Termine par : build sans erreur ni nouvel avertissement, captures vérifiées, puis
`git add -A src/PCPilot/Modules/<TonModule>` et `git commit -m "<Module>: …"`.
