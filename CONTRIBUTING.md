# Contributing to Timonier

Thanks for your interest! Bug reports, fixes, new settings and translations are all welcome.

## Before you start

- Read the [developer guide](docs/DEVELOPER_GUIDE.md), especially the **security rules**: no shell or command strings,
  strict parameter validation, never weaken Windows security.
- Every registry path and value must be real and documented (Microsoft Learn, ADMX). No placebo tweaks.
- Test anything that changes the system on a **virtual machine** or a spare PC.

## Pull requests

- One topic per pull request, with a clear description of what changes for the user.
- The build must pass (`dotnet build src/Timonier/Timonier.csproj`) with no new warning.
- User-facing texts go through `L(...)` (see [docs/I18N.md](docs/I18N.md)); run
  `dotnet run --project tools/I18n -- extract` and commit the updated catalog.
- Commit messages and pull request descriptions are written in English.

## Translations

Translation files are in `src/Timonier/Localization/<language>.json`. The first translations were produced with AI
assistance and are waiting for native speakers: fixing a wrong or clumsy text is a great first contribution. Run
`dotnet run --project tools/I18n -- check` before opening the pull request. See [docs/I18N.md](docs/I18N.md).

## Security issues

Please report vulnerabilities privately, as explained in [SECURITY.md](SECURITY.md).

## License

By contributing, you agree that your contributions are licensed under the [GNU GPL v3](LICENSE).
