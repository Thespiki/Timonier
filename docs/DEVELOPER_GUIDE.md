# Timonier developer guide

Timonier is a local control center for Windows 10 and 11 written in C# / .NET 10 / WPF. Everything runs on the PC,
with no telemetry and a small footprint (the reference test machine is a Pentium N6000 laptop with 8 GB of RAM).

This guide explains the architecture, the security rules every contribution must follow, and how to add settings,
actions and pages. Translations are covered in [I18N.md](I18N.md).

## 1. Security rules (non-negotiable)

Timonier changes system settings with administrator rights. Its design assumes the UI process can be attacked
(it runs unelevated, reads user-writable files and displays text), so the elevated side trusts nothing it receives.

1. **No shell, no command strings.** Start programs with `ProcessRunner.RunAsync(SystemTool.X, [args…])` or
   `ProcessRunner.Launch(...)`: allowlisted absolute paths and `ArgumentList`. Prefer Win32, WMI, WinRT or COM APIs.
2. **PowerShell** only through `PowerShellRunner.RunAsync(constantScript, parameters)`. The script is a constant; data is
   passed as environment variables (`parameters["NAME"]` becomes `$env:TMN_NAME`). Never interpolate data into a script.
3. **Every action parameter is validated** in `ValidateParameters` with `Timonier.Core.Security.Validate` (formats,
   bounded lengths) and validated again in `ExecuteAsync`. The broker never trusts the UI.
4. **No arbitrary path or identifier reaches an admin operation**: a device id must exist in a fresh enumeration done in
   the broker, a service name must be in the allowed list, a file must exist and have the expected extension, etc.
5. **Sensitive actions** (creating an administrator, installing software outside the verified catalog, automatic
   sign-in, disabling a system component…) require a confirmation shown by the elevated process:
   `RequiresElevatedConfirmation => true`, or `RequiresElevatedConfirmationFor(parameters)` when it depends on them.
6. **No secret** in plain text on disk or in logs. **No network connection** initiated by Timonier itself (winget does
   download software; users are told so).
7. **Never weaken Windows security**: no setting to disable Defender, the firewall, UAC, SmartScreen, Secure Boot or
   security updates permanently. Timonier can report their state and offer to turn them on.
8. **Least privilege**: the broker refuses every setting or action whose `RequiresAdmin` is false. Declare
   `RequiresAdmin => true` only when needed.

## 2. Architecture

```
src/Timonier/
  Core/Model         TweakDefinition (declarative setting), Operation (RegSet, RegDeleteValue, RegDeleteKey,
                     ServiceStartOp, ScheduledTaskOp, RunToolOp, BroadcastSettingChangeOp), Requirement / Requires
  Core/Engine        TweakEngine (apply / undo), OperationExecutor, StateDetector, Journal (undo records)
  Core/Catalog       IModule, ModuleRegistry, IActionHandler, PageInfo, CategoryInfo, SearchEntry, IHealthCheck, QuickAction
  Core/Platform      SystemProfile (+Service), ProcessRunner / SystemTool, PowerShellRunner, RegistryAccess, ServiceConfig,
                     TaskSchedulerHelper, WmiQuery, Format, Native, Log, AppPaths
  Core/Security      Validate (+ValidationException), PinHasher
  Core/Search        SearchEngine (fuzzy, synonyms, intents), Synonyms
  Core/Settings      AppSettings / SettingsStore
  Core/Localization  Loc (L, LC, LP), Languages, PluralRules, LExtension (XAML)
  Localization/      one JSON translation file per language
  Broker/            on-demand elevated process
  UI/                Shell (MainWindow), Controls (TweakCard, TweakListView, PageHeader, PageScaffold), Theme, Services
  Modules/<Name>/    features
```

- The UI runs **unelevated**. When a setting or an action requires administrator rights, the engine sends it to the
  **broker**: the same executable started with `--broker` through UAC (one prompt per session). The broker listens on a
  named pipe restricted to the user's SID (network access denied, first instance only), checks the client and server
  process ids and the parent executable path, only runs operations declared in the catalog, re-validates every
  parameter, records machine-wide changes in `HKLM\SOFTWARE\Timonier\Journal` (writable by administrators only), maps
  `HKCU` to `HKU\<client SID>`, and exits after a few idle minutes.
- `IModule.Register` also runs **inside the broker**: it must only declare things (settings, actions, pages through
  factory lambdas, search entries…). No `AppHost`, no UI creation, no slow I/O (WMI, network, big files).
- `IActionHandler.ExecuteAsync` runs in the broker when `RequiresAdmin` is true: never reference `AppHost` or the UI there.
- Settings, cache and logs live in `%LOCALAPPDATA%\Timonier`.

## 3. Declaring a module

```csharp
namespace Timonier.Modules.Privacy;

public sealed class PrivacyModule : IModule   // public, parameterless: discovered by reflection
{
    public const string Category = "privacy";

    public void Register(ModuleRegistry r)
    {
        r.AddCategory(new CategoryInfo(Category, L("Privacy"), "", L("Telemetry, ads and app permissions.")));
        r.AddTweaks(PrivacyTweaks.All());
        r.AddAction(new MyActionHandler());
        r.AddPage(new PageInfo("privacy", L("Privacy"), "", NavSection.Settings, 10, () => new PrivacyPage())
        {
            CategoryId = Category,   // searching a setting of this category opens this page
            Description = L("…"),
            Keywords = [L("privacy, telemetry, tracking")],
        });
        r.AddHealthCheck(HealthCheck.Sync("privacy.score", L("Privacy"), "", "privacy", () => …));
        r.AddQuickAction(new QuickAction("privacy.quick", L("Title"), "", L("Description"), async () => { … }));
    }
}
```

A category without its own page automatically gets a generic page listing its settings.

## 4. Declarative settings

```csharp
const string Adv = @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo";
const string AdvPolicy = @"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo";

Tweak.Toggle("privacy.ads.advertisingid", L("Advertising ID"),
        L("Lets apps track you to show personalized ads."))
    .In("privacy", L("Advertising"))                   // category, group
    .Keywords(L("ads, tracking, targeting"))
    .WhenOn(Reg.CuDword(Adv, "Enabled", 1), Reg.LmDel(AdvPolicy, "DisabledByGroupPolicy"))
    .WhenOff(Reg.CuDword(Adv, "Enabled", 0), Reg.LmDword(AdvPolicy, "DisabledByGroupPolicy", 1))
    .WindowsDefault(TweakDefinition.On)
    .Recommend(TweakDefinition.Off)
    .Build();
```

Conventions:

- **Toggle: `On` means the Windows feature named by the title is active.** The title names the feature
  ("Advertising ID", "USB storage"), not the action ("Disable X"). Labels default to On/Off; `.Labels(...)` adapts them.
- The "Windows default" side usually deletes the policy value (`Reg.LmDel` / `Reg.CuDel`) instead of writing one.
- `Choice`: `.Option("key", L("Label"), ops…)` two times or more. `Action`: `.Run(ops…)` (a button).
- `RequiresAdmin` is computed from the operations (HKLM, `HKU\.DEFAULT`, `HKCU\Software\Policies`, services, system
  tasks, `RunToolOp(admin: true)`); `.AlwaysAdmin()` forces it.
- `.Requires(Requires.Windows11)`, `Requires.ProOrHigher`, `Requires.EnterpriseOrEducation`, `Requires.Battery`,
  `Requires.Bluetooth`, `Requires.Wifi`, `Requires.Camera`, `Requires.NvidiaGpu`, `Requires.When(p => …, L("reason"))`,
  combined with `.And(...)`. The reason is shown to the user as is.
- `.RecommendWhen(p => p.Tier == PerformanceTier.Low ? "off" : null)`: hardware-aware recommendation (`null` = none).
  `p` is a `SystemProfile` (edition, build, laptop, battery, GPU, system disk type, RAM, tier, touch, VM, managed PC…).
- `.Effect(ApplyEffect.RestartExplorer | SignOut | Reboot)`, `.Risk(RiskLevel.Moderate)` (confirmation) or
  `RiskLevel.Advanced` (hidden outside advanced mode), `.Warning(L("…"))` (shown before applying).
- `.Tags("gaming", "lowend", "privacy-max", "family", "kiosk", "security", "battery", "office", "dev")`: used by the
  setup profiles.
- `.Detect(() => "on" / "off" / option key / null)`: custom, fast, read-only detection when operations are not enough.
- Every setting is undoable through the journal, except `RunToolOp`. A missing service or task counts as compliant, so
  a setting may list items that do not exist on every Windows version.
- **Accuracy**: every registry path and value must be real and documented (Microsoft Learn, ADMX). Describe the real
  effect without exaggeration. No placebo tweaks.

## 5. Parameterized actions

```csharp
public sealed class SetDnsAction : IActionHandler
{
    public string Id => "network.dns.set";
    public string Title => L("Change DNS servers");
    public bool RequiresAdmin => true;

    public void ValidateParameters(IReadOnlyDictionary<string, string> p)
    {
        Validate.IpAddress(Validate.Required(p, "primary", 45));
        Validate.Int(p, "ifIndex", 0, 100000);
    }

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx, IReadOnlyDictionary<string, string> p)
    {
        ValidateParameters(p);          // always validate again here
        ctx.Progress?.Report(L("…"));
        // API / WMI / ProcessRunner with ArgumentList — never a command string
        return ActionResult.Ok(L("DNS servers changed."));
    }
}
```

- From the UI: `var outcome = await AppHost.Engine.RunActionAsync("network.dns.set", parameters, progress);` then
  `AppHost.Toasts.ShowOutcome(outcome);`. The engine goes through the broker when needed.
- `ctx.ApplyJournaled(sourceId, title, label, operations)` runs operations and records an **undoable** journal entry:
  prefer it for anything expressible as registry, service or task operations.
- `ctx.UserSid` is the SID of the UI user; in the broker `RegistryAccess.OpenRoot(RegHive.CurrentUser, ctx.UserSid)`
  opens that user's hive.
- Read-only work that needs no administrator rights (lists, states) is done directly in the page or a service class.

## 6. User interface

- Pages are `UserControl`s. In code: `var stack = PageScaffold.Create(this, L("Title"), L("Subtitle"), "");`
  then `stack.Children.Add(...)`. Helpers: `PageScaffold.Section`, `.Card`, `.InfoBar`, `.KeyValue`.
- Settings list: `new TweakListView("category")` (cards, states, badges, recommendations, grouped apply). Pages that
  host one implement `INavigationAware` and pass `"tweak:<id>"` to `list.Highlight(id)` (used by the search).
- Styles (`StaticResource`): `Pp.PageScroll`, `Pp.PageStack`, `Pp.PageTitle`, `Pp.PageSubtitle`, `Pp.SectionTitle`,
  `Pp.CardTitle`, `Pp.Body`, `Pp.Caption`, `Pp.Metric`, `Pp.Icon`, `Pp.Card`, `Pp.CardInteractive`, `Pp.Badge`,
  `Pp.BadgeText`, `Pp.InfoBar(.Success|.Warning|.Danger)`, `Pp.Button`, `Pp.AccentButton`, `Pp.DangerButton`,
  `Pp.SubtleButton`, `Pp.LinkButton`, `Pp.ToggleSwitch`, `Pp.SearchBox`, `Pp.ResultItem`.
- Colors: always `DynamicResource` (`Pp.TextPrimary`, `Pp.CardBackground`, `Pp.Accent`, `Pp.Success`, `Pp.Warning`,
  `Pp.Danger`…) so light and dark themes switch live. No hard-coded colors.
- Icons: Segoe Fluent Icons (`Style="{StaticResource Pp.Icon}" Text="&#xE72E;"`).
- UI services (UI thread): `AppHost.Dialogs`, `AppHost.Toasts`, `AppHost.Navigator.Navigate(pageId, parameter)`,
  `AppHost.Profile`, `AppHost.Registry`, `AppHost.Engine`, `AppHost.Background.Acquire(L("reason"))`,
  `AppHost.Settings.ModuleData["module.key"]` + `SettingsStore.Save()`.
- Performance: nothing heavy on the UI thread (`Task.Run`, then `await` back); lazy loading on first visit; refresh
  timers only while the page is visible; no background polling.
- Texts: clear and honest, always through `L(...)` (see [I18N.md](I18N.md)); explain limits.

## 7. Identifiers and navigation

| Module | Page id | Section / order | Category | Id prefix |
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

Never duplicate a setting that another module already declares.

## 8. Development loop

Requirements: Windows 10 2004 or later, the .NET 10 SDK, Git. No administrator rights are needed to build.

```powershell
. .\tools\dev.ps1            # loads Build / Run / Smoke / Capture
Build                        # Debug build, prints errors and warnings
Smoke                        # starts the app for 8 s and checks the log for errors
Capture -Page privacy -Theme dark -Out "$env:TEMP\shots\privacy.png" [-Scroll 900] [-Param "tweak:<id>"] [-Lang de]
```

`Capture` (Debug builds only) renders the window off-screen into a PNG without applying anything: use it to check a
page in both themes and several languages. Logs are in `%LOCALAPPDATA%\Timonier\logs\timonier.log`.

The broker self-test (Debug builds only) checks the elevated channel without elevation and without applying anything:
`Timonier.exe --selftest-broker` (report in `%LOCALAPPDATA%\Timonier\logs\selftest-broker.txt`).

`tools\publish.ps1` builds the self-contained release, the portable zip and the Inno Setup installer.

**Test changes that modify the system on a virtual machine or a test PC**, never on a machine you depend on.
