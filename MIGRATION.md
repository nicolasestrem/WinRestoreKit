# Migration from Appcopier

## Repository and executable

`nicolasestrem/Appcopier`, itself a fork of [builtbybel/Appcopier](https://github.com/builtbybel/Appcopier), became `nicolasestrem/WinRestoreKit`. WinRestoreKit is a standalone repository with `isFork: false` and carries the full 63-commit history.

The executable name changed from `Appcopier.exe` to `WinRestoreKit.exe`.

## Existing backups

WinRestoreKit keeps the Appcopier backup format unchanged. The `app\` data root, `backup_manifest.json`, every manifest key including `manifest_version`, module identifiers and type names, snapshot and backup folder naming including the ` (pre-restore)` suffix and the `yyyy-MM-dd - HH.mm` pattern, and `.reg` file naming are unchanged.

An existing Appcopier backup collection is read by WinRestoreKit as-is. Place `WinRestoreKit.exe` beside the existing `app\` directory, or copy that directory beside the executable. Copy the collection before trying this, rather than experimenting on the only backup.

The backup and restore behaviour, consent and snapshot workflow, on-disk layout, MIT licence and UI design did not change. The visible difference is that backup and restore logs written from now on use a `WinRestoreKit` header line instead of an `Appcopier` one. That header is only written and displayed, never parsed, so older logs continue to read exactly as before.

## Versioning and updates

WinRestoreKit is a new application and starts at version `0.0.1`. It does not continue Appcopier's `0.30.0` version line. Tags `0.12` and `0.30.0` are retained in the WinRestoreKit repository as Appcopier history.

Existing Appcopier binaries check Builtbybel-hosted endpoints and do not automatically discover WinRestoreKit. Migrating requires downloading WinRestoreKit once by hand.

## Archived repository

Old pull requests, issues and releases remain at [nicolasestrem/Appcopier](https://github.com/nicolasestrem/Appcopier). The repository is archived rather than deleted so those discussions, the release history and the fork lineage remain reachable, and existing links do not break.

## Attribution

[Builtbybel/Appcopier](https://github.com/builtbybel/Appcopier) remains acknowledged as the original project. The retained 2023 Builtbybel copyright and the MIT licence are recorded in [NOTICE.md](NOTICE.md) and [LICENSE](LICENSE). This attribution does not imply endorsement by the original author.
