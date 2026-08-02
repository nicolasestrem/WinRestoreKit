---
name: release
description: Walk the WinRestoreKit release flow - version bump in AssemblyInfo.cs (format is load-bearing for the in-app update checker), Release build, branch/commit, tag, and GitHub release.
disable-model-invocation: true
---

# Release

Arguments: the new version, e.g. `/release 0.31.0`. If omitted, ask for it.

## Why this flow is delicate

Every installed copy of WinRestoreKit checks for updates by downloading
`https://raw.githubusercontent.com/nicolasestrem/WinRestoreKit/main/src/WinRestoreKit/Properties/AssemblyInfo.cs`
and string-parsing the `AssemblyFileVersion` line (`DataHelper.Data.CheckForUpdates`). It compares the parsed value against the running app's own `AssemblyFileVersion`, read by reflection and formatted as **three parts** (`Version.ToString(3)` in `Program.GetCurrentVersionTostring`). Both sides therefore resolve to the same attribute and cannot drift. Consequences:


- `AssemblyVersion` and `AssemblyFileVersion` must stay **three-part** (`"0.31.0"`, never `"0.31.0.0"`) or every deployed copy will see a phantom "update available" forever.
- The line format `[assembly: AssemblyFileVersion("x.y.z")]` must not change (the parser does raw `IndexOf('(')` / `LastIndexOf(')')` substring math).
- The update check reads from **main** - the version bump only becomes "live" to users when it lands on main, so the bump must merge together with (not before) the release being available under GitHub releases.

## Steps

1. **Preflight**: working tree clean, on up-to-date `main`. Create a release branch (e.g. `release/0.31.0`) - never commit to main directly.
2. **Bump version** in `src/WinRestoreKit/Properties/AssemblyInfo.cs`: update both `AssemblyVersion` and `AssemblyFileVersion` to the same three-part value.
3. **Update CHANGELOG.md**: move Unreleased entries under the new version heading with today's date.
4. **Build and publish Release**, pasting the verbatim output of both:
   ```
   dotnet build src\WinRestoreKit.sln -c Release
   dotnet test src\WinRestoreKit.sln -c Release
   ```
   Do not proceed on a failed build or a failing test.

   **Then publish the release artifact.** WinRestoreKit ships as a **self-contained single file**, so users
   download one `.exe` and run it with no .NET install - the same experience 0.30.0 had on .NET Framework.
   Use exactly these flags:
   ```
   dotnet publish src\WinRestoreKit\WinRestoreKit.csproj -c Release -r win-x64 --self-contained true ^
     -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
     -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
   ```
   All four `-p:` flags matter. Without `IncludeNativeLibrariesForSelfExtract` the publish emits five loose
   native DLLs next to the exe (the app then fails if they are not shipped with it), and without
   `EnableCompressionInSingleFile` the artifact is ~156 MB instead of ~69 MB.

   Verify before shipping: `publish\` must contain **exactly one file**, `WinRestoreKit.exe`, around 69 MB.
   If anything else is in there, do not ship it - re-check the flags.

   Do **not** add `-p:PublishTrimmed=true` to shrink it. WinForms is not trim-safe: the designers and
   `ComponentResourceManager` resolve types by reflection, so trimming removes code that is only reached
   at runtime and the failures appear as missing resources or blank forms, not build errors.
5. **Commit** the version bump + changelog on the release branch, push, and open a PR to main. **Stop here for review - do not merge without explicit approval.**
6. **After the PR is approved and merged** (by the user or with their explicit OK): tag `main` (`git tag <version>` matching the repo's existing tag style - check `git tag --list` first) and push the tag.
7. **GitHub release**: create a release for the tag with `gh release create`, attaching the single
   `publish\WinRestoreKit.exe` from step 4. Note in the release notes that the download is much larger than
   0.30.0 (~69 MB vs ~1 MB) because the .NET runtime is now bundled into the executable, and that this is
   what keeps it a no-install download. Never attach the plain `bin\Release\net8.0-windows\WinRestoreKit.exe` -
   that build is framework-dependent and cannot start on its own.
8. **Post-check**: confirm the raw AssemblyInfo.cs URL on main now serves the new version, and that the release shows as latest - that is exactly what deployed apps will see.
