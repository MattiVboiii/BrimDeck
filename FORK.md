# BrimDeck fork (MattiVboiii)

This repository is a personal fork of [MoFeng2223/BrimDeck](https://github.com/MoFeng2223/BrimDeck) (Apache 2.0). Upstream stays the source of truth for the product; this fork carries Matti-specific build settings and a place for local experiments.

## Branch layout

| Branch | Role |
|--------|------|
| `main` | Fast-forward mirror of upstream `main`. Do not land fork-only commits here. |
| `matti` | Integration branch for this fork. Merge `main` into `matti` when upstream moves. |
| `feat/*`, `fix/*` | Short-lived work branches. Open pull requests into `matti`, not into `main`. |

The scheduled `upstream-sync` workflow updates `main` from upstream and opens or refreshes a single PR `main` → `matti` when `matti` is behind. It never auto-merges.

## Low-conflict rules

1. Prefer settings and configuration over deleting or rewriting upstream code (for example disable NetEase/KuGou libcef patching or per-provider credential reads via settings rather than removing the upstream paths).
2. Add new code as new files under `src/BrimDeck/Matti/` when possible.
3. Custom quota providers: use the existing Jint script provider when you can instead of editing large Core usage files.
4. Merge upstream (`main` into `matti`) often; keep fork diffs small.
5. Changes that belong upstream should go to MoFeng2223/BrimDeck as pull requests when they are generally useful.

## Fork-only build settings

`Directory.Build.props` at the repository root (not on upstream `main`) overrides:

- `BrimDeckRepositoryUrl` → `https://github.com/MattiVboiii/BrimDeck` so in-app updates, release manifests, and enhanced-sensor downloads use this fork’s GitHub releases (hash/size verification in `BrimDeck.Core` is unchanged).
- `BrimDeckInstallerAppId` → a distinct AppId so a fork install does not register as the upstream uninstall entry or receive upstream update packages.
- `BrimDeckDataDirSubfolder` → `BrimDeck-Matti` so user data is not stored beside an upstream install’s `%LocalAppData%\BrimDeck`.

Defaults for a vanilla upstream tree live in `src/BrimDeck/ForkRepositoryMetadata.targets`. The app reads repository and AppId from assembly metadata (`GitHubUpdates.cs`).

Publishing (`scripts/Publish-Release.ps1`) passes the same values into `installer/BrimDeck.iss` via Inno Setup defines.

## Upstream files this fork intentionally touches

Keep this list short when you add more fork behavior:

| File | Why |
|------|-----|
| `src/BrimDeck/Updates/GitHubUpdates.cs` | Read repository URL and installer AppId from assembly metadata. |
| `src/BrimDeck/BrimDeck.csproj` | Import `ForkRepositoryMetadata.targets`. |
| `installer/BrimDeck.iss` | Optional Inno defines for AppId, publisher URLs, and data folder (upstream defaults unchanged). |
| `scripts/Publish-Release.ps1` | Pass fork MSBuild properties into the installer build. |
| `.github/workflows/release.yml` | Guard release publishing so only `MoFeng2223/BrimDeck` runs it. |

New fork-only files: `Directory.Build.props`, `src/BrimDeck/ForkRepositoryMetadata.targets`, `src/BrimDeck/Matti/`, `.github/workflows/upstream-sync.yml`, `scripts/Get-RepositoryBuildProperties.ps1`, and this `FORK.md`.

Settings UI still links to upstream GitHub in a few places (`SettingsWindow.cs`); that is intentional so feedback and community links stay on the upstream project.

## Build and run (Windows)

Requirements match upstream CI: **.NET SDK 10.0.x** (see `.github/workflows/ci.yml`), **Windows 10 2004+** (build target `net10.0-windows10.0.19041.0`), x64.

From a repository root PowerShell prompt:

```powershell
dotnet build BrimDeck.sln -c Release
dotnet run --project src/BrimDeck/BrimDeck.csproj -c Release
```

Tests (as in CI, on a Windows machine):

```powershell
dotnet run --project tests/BrimDeck.Tests -c Release
dotnet run --project tests/BrimDeck.MediaTests -c Release
dotnet run --project tests/BrimDeck.WindowsTests -c Release -- --hosted
```

Installer and release artifacts (Windows, Inno Setup fetched by `scripts/Get-InnoSetup.ps1`):

```powershell
./scripts/Publish-Release.ps1 -Version <major.minor.patch>
```

## License

Apache 2.0 applies as in upstream `LICENSE` and `NOTICE`. Fork-specific source files are modifications and additions under the same license; retain copyright notices and state changes where required.