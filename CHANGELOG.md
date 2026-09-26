# Changelog

All notable changes to Timonier are listed here. Versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased] — 0.1.0 (first preview)

### Added
- Home dashboard: PC identity, live tiles, health checks with fixes, hardware-based recommendations, quick actions.
- Privacy, Personalization, Performance and Network settings (230+ documented, reversible settings).
- Security dashboard and hardening, per-app Internet blocking with Windows Firewall.
- Devices: inventory with problem explanations, safe disable/enable, hardware blocking (USB storage, optical drives,
  phones, camera, microphone), radios, battery health, displays.
- Users: local accounts, logon hours, per-account restrictions; Kiosk mode wizard; Guided access.
- Setup profiles wizard, Apps (winget catalog, uninstall, preinstalled apps, updates), Startup & services,
  Maintenance (cleanup, repairs, restore points, Windows Update), Windows tools.
- History (undo any change), Transparency page, app settings with PIN lock.
- Smart search with typo tolerance, synonyms and intents.
- Localization system with right-to-left support and translations.
- Inno Setup installer, portable zip, GitHub Actions build.

### Security
- Unelevated UI and on-demand elevated broker restricted to the catalog, with parameter re-validation, protected
  named pipe, process identity checks, protected logs, hardened child-process environment and one operation at a time.
