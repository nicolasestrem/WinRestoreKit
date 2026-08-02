---
name: release
description: Walk the WinRestoreKit release flow - version bump in AssemblyInfo.cs (format is load-bearing for the in-app update checker), Release build, branch/commit, tag, and GitHub release.
disable-model-invocation: true
---

# Release

Arguments: the new version, e.g. `/release 0.31.0`. If omitted, ask for it.

## Why this flow is delicate

Every installed copy of WinRestoreKit checks for updates through
`UpdateCheck.CheckForUpdatesAsync`, which reads the newest published version from two
sources in order (`UpdateCheck.ReadLatestVersionAsync`):

1. **Primary**, the GitHub Releases API,
   `https://api.github.com/repos/nicolasestrem/WinRestoreKit/releases/latest`, taking the
   release tag.
2. **Fallback**, taken on ANY primary failure at all, including the rate-limit 403 that
   shared IPs hit: it downloads
   `https://raw.githubusercontent.com/nicolasestrem/WinRestoreKit/main/src/WinRestoreKit/Properties/AssemblyInfo.cs`
   as raw text and string-parses the `AssemblyFileVersion` line out of it
   (`Data.ParseLatestVersion`).

Both sources therefore have to be correct at release time, not just the tag.

Whichever source answered, the result goes through `Program.NormalizeVersion` and is
compared with `==` against the running app's own `AssemblyFileVersion`, read by reflection
and formatted as **three parts** (`Version.ToString(3)` in
`Program.GetCurrentVersionTostring`).

A difference between the remote value and the local one is the NORMAL, intended state: the
local side is whatever binary the user installed, the remote side is what `main` says now,
and an update is exactly that gap. The question is never "are they equal" but "is a
difference real". On that, the two sources are NOT equally safe:

- **Fallback versus local is always meaningful.** Both sides read the same attribute,
  `AssemblyFileVersion`, one from `main` as raw text and one from the installed binary by
  reflection. A difference is therefore a genuine version difference and never an artefact
  of comparing two different fields.
- **Primary versus local can be meaningless, and nothing in the build stops it.** The tag
  is a separate hand-entered value: `ParseLatestReleaseTag` returns `tag_name` with a
  leading `v` or `V` stripped, and that is all. It never reads `AssemblyFileVersion`. So a
  difference here may be a real new version, or may just be a mistyped tag. Tag `0.31.1`
  over an artifact whose `AssemblyFileVersion` is `0.31.0` offers every user of that
  release a permanent phantom update, because they will never reach the advertised
  version; a tag behind the file tells users on the old build that they are current.

So the release requirement is explicit: **for one release, the artifact's own
`AssemblyFileVersion`, the release tag, and the `AssemblyInfo.cs` on `main` must all
normalize to the same string.** Consequences:

- `AssemblyVersion` and `AssemblyFileVersion` must stay **three-part** (`"0.31.0"`, never `"0.31.0.0"`) or every deployed copy will see a phantom "update available" forever.
- The tag must be three-part too. `NormalizeVersion` returns the raw input unchanged when fewer than three components parse, so a tag of `0.12` stays `"0.12"` and never equals `"0.12.0"`. A bare `v` prefix is tolerated and pinned by test; nothing else is.
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
6. **After the PR is approved and merged** (by the user or with their explicit OK): tag `main` and push the tag.

   The tag MUST be the exact three-part value you put in `AssemblyFileVersion` in step 2,
   optionally with a leading `v`. Nothing else is safe. Do not copy the shape of the
   existing `0.12` tag: two-part tags do not normalize to three parts, so `0.12` would
   never compare equal to `0.12.0` and every user on that release would be offered a
   phantom update forever. Check it before pushing:

   ```
   git tag --list
   grep AssemblyFileVersion src\WinRestoreKit\Properties\AssemblyInfo.cs
   ```

   The tag you push and the value in that file must be the same string.
7. **GitHub release**: create a release for the tag with `gh release create`, attaching the single
   `publish\WinRestoreKit.exe` from step 4. Note in the release notes that the download is much larger than
   0.30.0 (~69 MB vs ~1 MB) because the .NET runtime is now bundled into the executable, and that this is
   what keeps it a no-install download. Never attach the plain `bin\Release\net8.0-windows\WinRestoreKit.exe` -
   that build is framework-dependent and cannot start on its own.
8. **Post-check**: confirm the raw AssemblyInfo.cs URL on main now serves the new version, and that the release shows as latest - that is exactly what deployed apps will see.
