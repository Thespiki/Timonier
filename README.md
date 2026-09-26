<p align="center">
  <img src="src/Timonier/Assets/Timonier.png" width="96" alt="Timonier logo">
</p>

<h1 align="center">Timonier</h1>

<p align="center">
  <b>Take the helm of your Windows PC.</b><br>
  A local, transparent control center for Windows 10 and 11: privacy, performance, security, devices, users,
  kiosk mode, guided access, apps, maintenance — in one fast, careful app.
</p>

<p align="center">
  <a href="https://github.com/Thespiki/Timonier/releases">Download</a> ·
  <a href="docs/DEVELOPER_GUIDE.md">Developer guide</a> ·
  <a href="docs/I18N.md">Translations</a> ·
  <a href="SECURITY.md">Security</a>
</p>

> **Preview (0.x).** Timonier changes real Windows settings. Every change is recorded and can be undone, and the
> setup wizard creates a restore point first, but please try it on a PC you can afford to troubleshoot and report
> anything unexpected.

## Why Timonier

- **Everything in one place.** More than 230 documented settings and 65 actions, organized like Windows itself, with
  a search that understands typos, synonyms and intents ("turn off the camera" shows the switch directly).
- **Adapted to your PC.** Timonier detects your Windows edition and build, hardware (CPU, RAM, GPU, disk type, battery,
  touch, Bluetooth, camera…) and whether the PC is managed by an organization. It only offers what works here and
  explains why something is unavailable.
- **Honest.** No placebo "optimizations". Each setting says what it really does, what it may break and which Windows
  versions honor it. The *Transparency* page lists every operation Timonier can perform and what it will never do.
- **Reversible.** Every change goes to a journal and can be undone in one click (a few actions that Windows cannot undo
  are clearly flagged).
- **Safe by design.** The app runs without administrator rights. When a change needs them, a separate elevated process
  starts after a UAC prompt, only accepts operations from Timonier's own catalog, re-validates every parameter and
  closes itself after a few idle minutes. No shell commands are built from text, no scripts are generated.
- **Local and light.** No account, no telemetry, no network access by Timonier itself. It starts quickly, uses no
  background service and only stays in the tray while a feature needs it (guided access, for example).

## Features

| Area | What you can do |
| --- | --- |
| **Home** | PC identity, live CPU / memory / disk / battery tiles, health checks with one-click fixes, recommendations for this hardware, quick actions |
| **Privacy** | Telemetry and diagnostics, advertising and suggestions, search and AI features (Copilot, Recall), activity history, per-app permissions, privacy score |
| **Personalization** | Light/dark themes, accent, wallpaper and lock screen, taskbar, Start menu, File Explorer, desktop and windows, sign-in screen, mouse and keyboard |
| **Performance** | Hardware profile and advice, power plans and power modes, sleep timeouts, visual effects, gaming options, background apps, services |
| **Network** | Adapters overview, DNS per adapter (including family filters and DNS over HTTPS), hosts file blocking, network tools, saved Wi-Fi networks |
| **Security** | Security dashboard and score (antivirus, firewall, encryption, Secure Boot, TPM, UAC…), hardening settings, block an app from the Internet |
| **Devices** | Device inventory with problem explanations, disable/enable devices safely, block USB storage, CD/DVD, phones, camera, microphone, radios, battery health, displays |
| **Users** | Local accounts, logon hours for children, per-account restrictions, sign-in screen options |
| **Kiosk mode** | Wizard to turn a PC into a single-app kiosk (Store app, Microsoft Edge, or classic app), automatic sign-in with the password stored as an LSA secret, clean removal |
| **Guided access** | Lock the PC on one app until a PIN is entered, like on a phone (blocks shortcuts, keeps the app in front, time limit) |
| **Setup profiles** | Five-step wizard for a new PC: combine profiles (gaming, office, family, developer, maximum privacy, low-end PC, laptop, security, kiosk), review the plan, restore point, apps, export/import |
| **Apps** | Install from a curated winget catalog, uninstall programs, remove preinstalled Store apps, update everything |
| **Startup & services** | Startup apps (compatible with Task Manager), services and scheduled tasks, with protected system items |
| **Maintenance** | Disk cleanup, SFC/DISM/chkdsk repairs, restore points, Windows Update pause and active hours, error log digest |
| **Windows tools** | 80+ classic tools and 140+ Settings pages, searchable |
| **History & transparency** | Undo any change, export the history, see every operation Timonier can perform |

## Download and install

Download the latest release from the [Releases page](https://github.com/Thespiki/Timonier/releases):

- **Timonier-Setup-x.y.z-x64.exe** — installer (recommended). It installs Timonier for all users in Program Files,
  which the elevated process requires for security.
- **Timonier-x.y.z-win-x64-portable.zip** — portable version, no installation.

Check the downloaded file against `SHA256SUMS.txt`. The executables are not code-signed yet, so Windows SmartScreen may
show a warning: choose *More info* → *Run anyway* only if the checksum matches.

**Requirements:** Windows 10 version 2004 or later, or Windows 11, 64-bit (x64; ARM64 PCs run it through emulation).
Some features depend on the edition (Pro, Enterprise, Education) and are shown as unavailable otherwise.

## Languages

Timonier follows your Windows display language and can be switched in its settings. French is the original language;
the other translations were produced with AI assistance and are waiting for native speakers — corrections are very
welcome, see [docs/I18N.md](docs/I18N.md).

## Build from source

```powershell
git clone https://github.com/Thespiki/Timonier.git
cd Timonier
dotnet build src/Timonier/Timonier.csproj -c Release
.\tools\publish.ps1          # self-contained build, portable zip and installer (needs Inno Setup 6)
```

You need the .NET 10 SDK. See the [developer guide](docs/DEVELOPER_GUIDE.md) for the architecture and the rules every
contribution follows.

## Contributing

Bug reports, fixes, new settings and translations are welcome: read [CONTRIBUTING.md](CONTRIBUTING.md).
Please report security issues privately as described in [SECURITY.md](SECURITY.md).

## License

Timonier is free software, licensed under the [GNU General Public License v3.0](LICENSE).

Windows, Microsoft Edge and other product names are trademarks of their respective owners. Timonier is an independent
project, not affiliated with Microsoft.
