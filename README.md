# WinRestoreKit

Back up, copy and restore Windows settings locally.

## What it does

WinRestoreKit exports registry keys as `.reg` files and copies folders and files into a timestamped backup folder. Backups stay offline and local, with no cloud account required.

## Key features

- Back up and restore selected modules for Windows settings, apps, devices, Wi-Fi credentials, and developer tooling.
- Review a restore wizard that states what will be overwritten and what it cannot undo before consenting.
- Create an automatic pre-restore snapshot of the settings about to be overwritten.
- Browse backups and undo points in a History timeline.
- Store a machine-readable `backup_manifest.json` beside each backup log for backup metadata and status.

## Requirements

- Windows 11, 64-bit. Windows 10 should work, but is untested.
- Run as administrator. Registry export and import shell out to `regedit.exe`, which requires elevation.

No .NET installation is required. The runtime is bundled, which is why the download is around 69 MB rather than about 1 MB. It is still a single `WinRestoreKit.exe` file.

## Download

Download [WinRestoreKit.exe](https://github.com/nicolasestrem/WinRestoreKit/releases/latest) from the latest release.

## Using existing Appcopier backups

WinRestoreKit uses the same backup format, and existing Appcopier backups remain compatible. To reuse an existing backup collection, either place `WinRestoreKit.exe` beside the existing `app\` directory, or copy the existing `app\` directory beside `WinRestoreKit.exe`.

Copy the backup collection before trying this. Do not experiment on the only copy of a backup folder. Older backups can still show an `Appcopier` header line inside `backup_log.txt`; this is expected and harmless.

## Building from source

Build and test the solution:

```powershell
dotnet build src\WinRestoreKit.sln
dotnet test src\WinRestoreKit.sln
```

Publish the self-contained single-file release artifact. This block uses `cmd` line
continuations (`^`), matching `.claude/skills/release/SKILL.md` verbatim so the two cannot
drift; in PowerShell, put it on one line instead.

```bat
dotnet publish src\WinRestoreKit\WinRestoreKit.csproj -c Release -r win-x64 --self-contained true ^
     -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
     -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

## Project history and attribution

WinRestoreKit began as a fork of [Appcopier by Builtbybel](https://github.com/builtbybel/Appcopier). It has since been substantially rebuilt with a new engine, restore-safety model, test suite and interface. The original project and its copyright remain acknowledged under the MIT licence.

## Licence

WinRestoreKit is available under the MIT licence, retaining the original copyright alongside the new one. See [LICENSE](LICENSE) and [NOTICE.md](NOTICE.md).
