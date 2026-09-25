# Security policy

Timonier changes Windows settings with administrator rights, so security reports are taken seriously.

## Reporting a vulnerability

Please **do not open a public issue**. Use GitHub's private vulnerability reporting instead:
**Security** tab → **Report a vulnerability** on <https://github.com/Thespiki/Timonier/security>.

Include the Timonier version, the Windows edition and build, the steps to reproduce and the impact you observed.
You should get an answer within a few days.

## Supported versions

Timonier is in preview (0.x). Only the latest release receives security fixes.

## Scope

In scope, for example:

- a way for an unelevated process or file to make the elevated broker run something outside its catalog, or with
  parameters that bypass validation;
- command, PowerShell, registry path or file path injection;
- secrets written in plain text (passwords, PIN) or leaked to logs;
- a setting that silently weakens Windows security.

The security model is described in [docs/DEVELOPER_GUIDE.md](docs/DEVELOPER_GUIDE.md#1-security-rules-non-negotiable)
and on the app's **Transparency** page.
