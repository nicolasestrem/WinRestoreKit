---
name: new-backup-module
description: Scaffold a new WinRestoreKit backup module - creates the Conf/ class, the restore-safety declarations, and the ModuleCatalog registration that is easy to miss. Use when adding support for backing up a new Windows setting, app, or device area.
---

# New Backup Module

Adding a backup module requires **two synchronized edits**, plus declarations the restore path depends on. The SDK-style project globs `**/*.cs`, so the file is compiled automatically - but registration in the module catalog is not, and missing it means the module silently never appears.

Read `CLAUDE.md`'s "Reporting outcomes" and "Restore safety" sections before writing any of this. The rules there are not style preferences; each was written after the corresponding mistake shipped.

## Inputs to determine first

1. **Module name** - prefix letter encodes the category and drives the filename:
   | Prefix | Category | Tree node name in UI |
   |--------|----------|----------------------|
   | `A` | Apps | "Apps" |
   | `C` | Credentials | "Credentials" |
   | `D` | Devices | "Devices" |
   | `E` | Developer tooling | "Developer" |
   | `G` | Gaming | "Gaming" |
   | `W` | Windows settings | "Settings" |

   There is no `B`/Browser row anymore: the browser modules were retired in Phase 3a, and the roadmap says not to add new ones.
2. **What gets backed up** - registry key(s), a folder, or a command's output. Pick the matching base below; hand-roll from `BackupBase` only when none fits, and say why in the class remarks.
3. **Whether an absent target is normal.** A touchpad key on a desktop is normal; a key the module exists to capture is not. This flag is the difference between a reassuring "skipped" and a real problem being hidden, in one direction, and crying wolf in the other.

## Step 1a - Single registry key? Inherit `RegistryModule`

The most common case (9 of the 29 shipped modules). It supplies `Backup`, `Restore`, `IsInstalled` and the restore declaration from your data. Its files are named `{Title}.reg` - a compatibility promise with existing backups.

```csharp
using WinRestoreKit;

namespace Conf
{
    public class WExample : RegistryModule
    {
        public WExample()
        {
            Title = "Example";
            Info = "This will back up ...";
        }

        protected override string Key => @"HKEY_CURRENT_USER\Software\...";

        // True when the key is legitimately missing on some healthy machines.
        protected override bool AbsenceIsNormal => false;
    }
}
```

## Step 1b - Several registry keys? Inherit `MultiKeyRegistryModule`

One `.reg` file **per key**, named from the key via `RegFileNameFor` - never `{Title}.reg` in a loop, which is the WThemes landmine `BackupFileNamingTests` exists to catch. Add keys in the constructor; the base reads `Keys` at access time.

```csharp
namespace Conf
{
    public class WExample : MultiKeyRegistryModule
    {
        public WExample()
        {
            Title = "Example";
            Info = "This will back up ...";

            Keys.Add(@"HKEY_CURRENT_USER\Software\...");
            Keys.Add(@"HKEY_LOCAL_MACHINE\SOFTWARE\...");
        }

        // Per key - IsInstalled() answering true says nothing about the other keys.
        protected override bool AbsenceIsNormal(string key) => false;
    }
}
```

## Step 1c - One folder? Inherit `FolderModule`

Backs the folder up under `{Title}` in the backup and restores it back. `AbsenceIsNormal` defaults to `true` (a missing profile folder usually means the feature was never used); override it for a folder whose absence is a fault.

```csharp
using DataHelper;

namespace Conf
{
    public class EExample : FolderModule
    {
        public EExample() : base(Data.LocalAppData + "\\Vendor\\App")
        {
            Title = "Example";
            Info = "This will back up ...";
        }
    }
}
```

The base has deliberately **no close-before-backup logic**. A module whose backup must close the owning app first is a different shape: hand-roll from `BackupBase` so the requirement is visible, and read `Conf/FolderModule.cs`'s remarks first.

## Step 1ci - Named files inside a folder? Inherit `FileModule`

Copies the files you list into `{Title}\` in the backup and puts them back. Use this - **not** `FolderModule` - whenever the folder holds anything you must not capture: it copies what `Files` lists and never enumerates a directory, which is how `ESsh` excludes private keys structurally rather than by a filter someone has to keep correct.

```csharp
using DataHelper;

namespace Conf
{
    public class EExample : FileModule
    {
        public EExample()
        {
            Title = "Example";
            Info = "This will back up ...";

            Files.Add(Data.UserProfile + "\\.config\\app.json");
        }

        // Per file, and abstract on purpose: the shipped consumers genuinely disagree.
        protected override bool AbsenceIsNormal(string file) => true;
    }
}
```

Two rules, both the file-side form of ones you already know:

- **N files must produce N distinct names.** The default name is the file's *base name*. If two of your files share one - three Windows Terminal installs all call theirs `settings.json` - you **must** override `BackupFileNameFor`, or the second copy silently overwrites the first while both steps report success. Match on the path, never on list position.
- **Never derive the name from the full path.** Paths are composed from `Data.*` roots and carry the backing-up account's user name, so a path-derived name stops resolving under any other account.

## Step 1d - A command's export? Use `Utils.RunToolAsync` + `Utils.ValidateExportArtifact`

There is intentionally no `CommandModule` base - the shipped command modules proved too different for one (see the Phase 3a spec). Hand-roll from `BackupBase`, override the **async** pair, and use the shared seams:

```csharp
public override async Task<ModuleResult> BackupAsync(string path)
{
    string file = Path.Combine(path, ExportFileName);   // keyless artifact: name it with a const on THIS class

    ProcessOutcome outcome = await Utils.RunToolAsync("sometool", new[] { "export" }, stdoutFile: file);

    // Walk the full ladder: null / !Started / TimedOut / Error != null / ExitCode != 0 - see
    // WNetworkConf.Verify for the shape - then check the artifact. An exit code is not evidence.
    ...
    return ModuleResult.Aggregate(new[] { Utils.ValidateExportArtifact(file, Title, "sometool", "exported ...") });
}
```

`RestoreTargets` for a command module is `RestoreTarget.Command("...")` - a plain-language description over 30 characters, because it is the only thing the user can judge the command by (`RestoreDeclarationTests` enforces this). Restore-side reasons say **applied**, never *verified*.

winget specifically goes through `Utils.RunWingetAsync`, not `RunToolAsync` - it can show its own console window and carries a ten-minute budget.

## Declarations every module needs

**A module that overwrites a live app's files must declare its process:**

```csharp
public override IReadOnlyList<RestoreCloseRequirement> ProcessesToCloseBeforeRestore
    => new[] { new RestoreCloseRequirement("Code", "Visual Studio Code", needsConsent: true) };
```

The process name is what `Process.GetProcessesByName` takes - no `.exe`. Require consent for anything whose closing destroys work the user can see; pass `false` only for a process Windows brings straight back on its own. **Never** write into a profile whose owner is running without one of these. This also applies to apps that *rewrite their own settings files while running* (Windows Terminal, VS Code): an unclosed app can overwrite the restored file minutes later while the row reads applied.

Leave `RestoreMakesChanges` alone unless the module's restore genuinely writes nothing. Setting it false exempts the module from the pre-restore snapshot, which means a restore that cannot be undone. Likewise, **anything a restore writes must be read by the module's own `Backup`** - the snapshot is an ordinary backup, so a restore path that writes elsewhere is invisible to it while the gate still reports the restore undoable.

Conventions the bases imply:
- `Restore` must consume exactly what `Backup` produced - same filename, same key.
- Keyless artifacts (a `.json`, a `.pow`) are named by a `const` on the class that writes them (`AppStoreApps.ExportFileName` is the pattern); `.reg` names come from `RegFileNameFor`.
- **Never show a dialog from module code on the restore path.** Modules run on thread-pool threads. Restore consent is gathered by `BackupRestoreOrchestrator` before dispatch. There is no backup-time prompt mechanism anymore either (`AllowPrompts` was removed in 3a); if a module ever truly needs one, it takes the permission as a call parameter, never as instance state.

The file goes in **`src/WinRestoreKit.Core/Conf/`** - the engine library, not the app project. Since Phase 4 PR 2 the module tests enumerate `typeof(BackupBase).Assembly`, which resolves to `WinRestoreKit.Core.dll`; a module created in the app project is invisible to every one of those sweeps, and `RestoreDeclarationTests.NoModuleIsLeftBehindInTheAppAssembly` is what will tell you so.

`WinRestoreKit.Core` deliberately does not reference WinForms. If your module seems to need a dialog, it does not: see `AppStoreApps.RestoreDialog` for the registered-seam pattern, and remember that module code must never show a dialog from a thread-pool thread anyway.

Do **not** add a `<Compile Include>` entry to any csproj - the SDK projects glob `**/*.cs`.

## Step 2 - Register in the module catalog

In `src/WinRestoreKit.Core/Conf/ModuleCatalog.cs`, method `CreateAll()`, add a `ModuleRegistration` next to its category siblings, in tree order:

```csharp
new ModuleRegistration(new WExample(), "Settings"),
```

The category string must exactly match the tree node name from the table above (a typo silently creates a new top-level category; a genuinely new category is created by spelling it consistently on every module that belongs to it). `BackupPageView.InitializeConfigurations` loops over `ModuleCatalog.CreateAll()`, so this single edit is what puts the module in the tree.

**Must this module join a preset?** `src/WinRestoreKit/Views/BackupPresets.cs` holds two curated name lists: `DeveloperMachine` and `MinimalPrivacySafeExclusions`. A developer tooling module usually belongs in `DeveloperMachine`; a module that carries identifying data (Windows Update client identity, env vars, Wi-Fi keys) belongs in `MinimalPrivacySafeExclusions` so "Minimal privacy-safe" leaves it out. Add the module's CLR type name to the matching list (and to `BackupPresetsTests`'s pinned literal) if so.

## Step 3 - Update the hand-kept test rosters

The declaration tests enumerate modules by reflection, but a few assertions are hand-kept and **will fail until updated** - that is their job:

- `RestoreDeclarationTests`: the total module count, the RegistryModule-subclass count, and the close-requirements roster if your module declares one. `ModuleCatalogTests` pins the same 29 - update both counts together when you add a module.
- `ModuleShapeTests.EveryRegisteredModule_HasATitle`: append your module to the array.
- `BackupFileNamingTests`: nothing to edit for a new module, but your keys must not collide with any existing `.reg` filename - the global uniqueness sweep catches it.

## Verify

```
dotnet build src\WinRestoreKit.sln
dotnet test src\WinRestoreKit.sln
```

`RestoreDeclarationTests` enumerates every registered module and fails if yours does not declare what its restore touches, so a forgotten declaration is caught here rather than in front of a user mid-restore.

Then confirm manually where possible: the module appears under the right tree node, backup produces the expected file in `app\<timestamp>\`, the restore confirmation lists your declared targets, and restore consumes what backup wrote. Meaningful manual testing needs an elevated session - registry work shells out to `regedit.exe`.
