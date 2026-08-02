# Phase 3b — Module coverage: developer tooling

Design record for the second sub-phase of Phase 3. Written 2026-07-21, alongside the implementation on
`feat/phase3b-developer-tooling`. The plan behind it came from a multi-agent exploration and design
pass over the 3a bases; the two open scope questions it raised were decided by the user and are
recorded below rather than left implicit in the code.

> This document predates the rename of Appcopier to WinRestoreKit and is kept as a
> historical record. Product names, namespaces and paths below refer to the project as
> it was at the time of writing.

## Goal

Add the developer-facing state a power user would actually miss, on top of the module bases 3a
extracted. Five new modules in a new **Developer** tree category (filename prefix `E`), plus the two
pieces of machinery they need: a `FileModule` base for copying *named files*, and a `RestoreTarget.File`
kind so the confirmation dialog can say "file:" where it previously only knew about folders.

## What landed

### `RestoreTarget.File`

A fourth kind beside `RegistryKey`, `Folder` and `Command`. It exists because the two are different
promises to the user: `folder: C:\Users\me\.ssh` reads as "everything under here is replaced", and
these modules replace *named files* inside directories they otherwise leave alone. Overstating restore
scope in the text the user consents against is the same defect as understating it.

`RestorePlan.Render` is the only switch on the kind in the codebase; it gained one arm.
`RestorePlanTests.ConfirmationText_LabelsEachTargetKind` asserts a distinct label per kind, so a future
kind added without an arm falls through to the bare path and fails a test rather than shipping as an
unlabelled line.

### `Utils.CopyFile` and `CopyResult.ToFileStep`

There was no single-file copy primitive — only `CopyFolder`. `CopyFile` returns the same `CopyResult`
tally so both directions fold through one Skipped-vs-Failed ladder, and it does not throw: a failure
comes back as `FilesFailed = 1` with `FirstError` set.

Two decisions worth recording:

- **A source it could not *examine* is a failure, not an absence.** The `FileInfo` construction is
  wrapped separately from the copy, and that catch deliberately does **not** set `SourceMissing`.
  Absence maps to `Skipped` for four of the five modules, so folding a failed probe into absence is the
  "I could not tell" → "nothing was there" slide the Phase 2a rules exist to prevent.
- **It creates the destination directory.** Load-bearing rather than convenient: a machine being
  restored onto may never have run ssh, so `%USERPROFILE%\.ssh` does not exist, and failing there would
  report "could not be copied" for a restore that is simply the first one.

`ToFileStep` is a separate ladder from `ToStep` only because of its nouns — `ToStep`'s absent-and-not-
normal wording is "expected **folder** for X is missing", and telling a user their hosts *folder* is
missing sends them to look for something that was never sought. The wording lives next to the mapping
rather than being patched at five call sites.

### `FileModule`

The file-level sibling of `FolderModule`. Shape borrowed from `MultiKeyRegistryModule` (N items,
per-item names, fields read at access time); decisions borrowed from `FolderModule` (sealed async pair,
restore-side absence always `NothingBackedUp`).

- **It is a whitelist by construction.** It copies what `Files` lists and *never enumerates a
  directory*. That is how the private-key exclusion below is expressed — as a structural property
  rather than as an exclusion filter someone has to keep correct.
- **`AbsenceIsNormal(string file)` is abstract**, following `MultiKeyRegistryModule` rather than
  `FolderModule`'s virtual-default-`true`. The consumers genuinely disagree: a Terminal Preview settings
  file is absent on most machines, an absent `hosts` means a broken install. A default would be right
  for one and silently wrong for the other, on the flag whose two failure directions are cry-wolf and
  hidden-problem.
- **`BackupFileNameFor` defaults to the file's BASE NAME, deliberately not the full path.** The paths in
  `Files` are composed from `Data.*` roots at runtime, so a path-derived name would carry the backing-up
  account's user name into the artifact name and stop resolving under any other account — the WThemes
  wallpaper-pointer class of defect, except this one would break the restore rather than the result.
  A module whose files share a base name must override; `ETerminal` does.
- **Artifacts live under `{Title}\`** rather than loose at the backup root. That groups them, keeps a
  base name like `config` from colliding between two modules, and gives `HasBackupIn` one directory to
  probe.
- **`HasBackupIn` earns the real check only when the module closes a process** — the `FolderModule`
  rule, and here it is what stops the orchestrator closing Windows Terminal, and every shell running in
  it, for a restore that had nothing to copy.

### The five modules

| Module | Base | Closes | Absence |
| --- | --- | --- | --- |
| `ETerminal` | `FileModule` | WindowsTerminal (consented) | normal |
| `EVSCode` | `BackupBase` (hybrid) | Code (consented) | normal |
| `ESsh` | `FileModule` | — | normal |
| `EEnvironment` | `RegistryModule` | — | **not** normal |
| `EHosts` | `FileModule` | — | **not** normal |

**`ETerminal` covers all three installs** (Store, Preview, unpackaged) — user decision, 2026-07-21.
Covering only the Store build would hand Preview and scoop/choco users a green "Skipped" over a
settings file that was right there. All three files are called `settings.json`, which is exactly the
collision the naming seam exists for: without the override the second export would overwrite the first
while *both* steps reported success, and the restore would write one file to all three destinations.
The override matches on path rather than list position, because a positional name changes meaning the
moment a fourth location is added and would orphan artifacts in every existing backup.

**`EVSCode` is hand-rolled from `BackupBase`,** not a `FileModule`. Two of its three targets are files
and the third — `snippets` — is a directory of arbitrarily many user-named files. Teaching `FileModule`
about folders to serve this one consumer is the mistake 3a's critique caught with the dropped
`CommandModule`: a base that fits one of its consumers is a worse seam than two honest ones. `WThemes`
is the precedent for a heterogeneous module folding both kinds of sub-operation through one `Aggregate`.
Because it does not inherit the sealed pair, it repeats the restore-side absence rule explicitly, and
its own backup/restore behaviour is tested directly rather than through the base's tests.

**`ESsh` excludes private keys** — user decision, 2026-07-21. Appcopier writes backups as ordinary
unencrypted files beside the executable, which is the wrong home for key material: a copy of `id_rsa`
there is a credential in plaintext, surviving in every backup folder the user forgets to delete, with
the passphrase protection on the original bypassed. Keys are meant to be re-issued on a new machine,
not carried to it by a settings tool. `DeveloperModuleTests` pins the exact file list *and*, separately,
that no declared target names a key file — so a future edit that appends to `Files` has to defeat both,
and the test that fails states the reason.

Not restored: NTFS ACLs. Windows OpenSSH refuses to use a *private key* whose ACL is too permissive but
is tolerant about `config` and `known_hosts`, so the files this module actually carries are usable after
a plain copy. This would need saying if the exclusion above were ever reversed.

**`EHosts` is the one module here that writes outside the user's profile.** Machine-wide, read by every
program that resolves a name, so it carries a `WarningMessage` even though the mechanics are an ordinary
file copy. Writing it needs elevation; the app manifests `highestAvailable`, and an unelevated run
produces an honest `Failed` step out of the copy primitive rather than a special case in the module.
Deliberately no pre-flight elevation probe — it would report the same fact one step earlier while adding
a second place that has to agree with the first about what "can write" means.

Note that `ModuleTargetTests.Themes_WritesNothingMachineWide` is WThemes-specific on purpose and is not
a global sweep. This module would legitimately fail such a sweep, which is why that test names the
module it constrains.

**`EEnvironment` is a plain `RegistryModule`** despite shipping with the file-based set — the category
is about what the user is backing up, not which base class it needs. Two limitations are disclosed
rather than engineered around:

1. A restore is an additive **merge**, like every registry import in this app. A variable present on
   this machine but absent from the backup survives; only variables the backup names are overwritten.
   This is the Phase 2b fidelity stance, stated in `Info` because `PATH` is the value where a user is
   most likely to expect otherwise.
2. **No `WM_SETTINGCHANGE` broadcast.** Already-running shells and editors keep the variables they
   started with; new processes see the restored values. Broadcasting is deliberately not built here —
   it would be this app's first message sent to every top-level window, which is a different kind of
   operation from writing a key and belongs to its own review rather than to a coverage phase.

### Registration

There is no category enum: `ConfPageView.FindOrCreateNode` creates "Developer" from the first
`AddConfiguration` call, so consistent spelling is the whole mechanism. The block sits after
Credentials, which puts the node last in the tree.

## Snapshot coverage

The invariant from `CLAUDE.md` — *anything a restore writes must be inside the pre-restore snapshot*,
because the snapshot is taken by running the module's own `Backup`. It holds structurally for all five:
every module's restore writes exactly the paths its backup reads, with no legacy-filename fallback and
no write to a location backup does not visit. That is the asymmetry that pushed `WTelemetry`'s fallback
out of 2c, and nothing here reintroduces it.

## What the review changed

A four-agent review (safety, silent-failure, code, test-coverage) ran against the first commit. Three
findings were real defects rather than polish, and one of them is worth recording as a process note.

**`CopyFile` decided absence with `FileInfo.Exists`.** That is the probe `RestAppsForm.AppExport.Read`
already removed once, for the reason its comment gives: `Exists` folds "there is nothing here" together
with "I was not allowed to find out". I suspected this before the review, measured it, and **cleared it
wrongly** — my ACL denied `ListDirectory | ReadData`, which leaves Traverse and ReadAttributes intact,
so `Exists` still answered true and the probe looked correct. Two reviewers used
`icacls /inheritance:r /deny (RX)`, which denies those rights too, and `Exists` answers **false** for a
file that is sitting right there. With `AbsenceIsNormal => true`, that is a green "not present on this
system" over a file that exists and was never copied — and `icacls /inheritance:r` is the standard
remedy for OpenSSH's "permissions are too open", applied to the very folder `ESsh` reads. Now classified
from the exception the open raises. The test pins the discriminating ACL specifically, and was verified
to fail against the old implementation. **The lesson is in the code comment: the measurement that
exonerates a probe can be the one that did not deny enough.**

**`HasBackupIn` probed for the directory, not an artifact.** `CopyFile` creates the destination directory
before it knows the copy will succeed, and `CopyFolder` creates it before enumerating — so an entirely
failed backup, or a user with an empty `snippets` folder and no customised settings, leaves a `{Title}\`
that exists and holds nothing. A directory probe then buys a *consented process kill* for a restore that
copies nothing: every Terminal tab, every unsaved VS Code buffer, for a no-op knowable in advance. Both
`FileModule` and `EVSCode` now require a named artifact. `FolderModule` keeps the directory probe,
correctly: its restore copies the directory wholesale.

A correction to the first write-up of this fix, which justified it partly with a cross-machine scenario —
a Preview-only backup restored onto a Store-only machine. **That scenario does not hit the bug.**
`ETerminal` populates `Files` unconditionally in its constructor, so it always asks about all three
installs, and both the old directory probe and the new artifact probe answer true; the restore then
writes the preview file correctly. The empty-directory case is real and sufficient on its own. Recorded
because a fix justified by a scenario that does not occur invites someone to "simplify" it later after
finding the scenario impossible.

**The write was not atomic.** `FileMode.Create` truncates before the first byte, so a failure mid-copy
left the destination empty or half-written — and for `EHosts` that destination is the machine-wide
`hosts` file. Changed to write to a temp file and rename. **This was subsequently reverted** — see
"Reversed after review" below, which supersedes this paragraph and explains why.

Also from review: `EVSCode` gained a named `BackupNameFor` seam (two inline `Path.GetFileName` call sites
could drift apart — the WThemes shape), an internal setter on `SnippetsFolder` so its hand-rolled async
pair could finally be tested at all, and a full behavioural test file. `RestoreDeclarationTests`'
close-requirement theory was a single hand-written `[InlineData(APinnedApps)]` row that Phase 3b never
extended, leaving `ETerminal` with no direct coverage — the exact failure mode that file's own header
warns about, now a reflection sweep. A `FileModule` filename-distinctness sweep was added to match the
registry side's.

Three wording corrections, each a claim the code did not support: `ETerminal`'s warning said Terminal
"rewrites settings.json when it exits", but `Utils.CloseProcess` force-kills, so that exit never happens;
`RestoreTarget.File`'s comment justified itself by asserting the folder label means "everything under
here is replaced", when `CopyFolder` merges; and `EVSCode` disclosed neither that its two files are
replaced wholesale while snippets merge, nor that the snippets merge makes that half of the restore
un-undoable while `SnapshotGate` still reports it undoable.

One disclosure was added on a risk nobody had noticed: **`EEnvironment` exports every environment
variable, in plaintext**, which routinely includes `GITHUB_TOKEN` and `AWS_SECRET_ACCESS_KEY`. That is
word for word the hazard `ESsh` refuses to carry private keys over — two modules in one category taking
opposite stances. Defensible, because a private key is *always* a credential and excluding it loses
nothing, whereas filtering variables by name guesswork would drop real settings while still missing
secrets named differently. Disclosed rather than filtered, and the reasoning is in the class remarks.
(Superseded in part: an opt-in filtered sibling was added afterwards — see "Reversed after review". The
plain export and its disclosure are unchanged.)
`ESsh` also gained a warning: `known_hosts` is a man-in-the-middle defence, and overwriting it deserves
a line in the text the user consents against.

## Reversed after review

These two were raised as open questions at the end of the first round and decided by the user on
2026-07-21. Both reverse or extend a decision recorded above; the earlier text is left in place rather
than edited away, because the reasoning that produced it was not obviously wrong at the time and someone
will otherwise re-derive it.

### The atomic write is gone; `CopyFile` writes in place again

The temp-file-and-rename made the content swap atomic and, in doing so, replaced the **directory entry** —
so a destination that was a link stopped being one. Measured 2026-07-21 with a hard link: afterwards the
link held the new content while the file it was linked to still held the old, no longer the same file.

Laying the two failures side by side is what settled it:

| | torn file (atomicity prevents) | broken link (atomicity causes) |
|---|---|---|
| reported to the user | yes\* — the step goes `Failed` | **no — every row green** |
| undoable from the snapshot | yes — old contents are in it | **no** |

\* With one exception, caught in review and worth stating precisely because the trade rests on this
column. A torn file has three triggers: disk full, a source read error, and Appcopier being killed
mid-restore. The first two throw, are caught, and surface as a `Failed` step. A **kill reports nothing** —
`PerformRestoration` never returns, so no summary is shown and `restore_log.txt` is never written. The
snapshot half still holds there (it is taken before the restore begins), so the file is recoverable but
the user is not told it needs recovering. Two of three triggers loud, one silent-but-recoverable, against
a broken link that is silent-and-permanent. The trade runs the same way; it just should not be stated by
rounding the kill case up to "reported".

The snapshot is taken by running the module's own `Backup`, which captures file *contents*. It has no
representation of "this path was a link", so restoring it cannot put one back. Atomicity was therefore
buying protection from a **loud, recoverable** failure and paying for it with a **silent, permanent** one.
Both columns point the same way.

**The rejected middle option** was to detect a link and write through it while keeping the rename
otherwise. That needs a branch this environment cannot test — creating a symbolic link requires elevation
or Developer Mode — so the symlink arm would ship unexercised. An untested branch guarding a silent
failure is worse than not having the branch. Writing directly gets link-preservation *structurally*
instead, which is the same move `ESsh` makes by never enumerating `.ssh` rather than filtering it.

**Accepted cost, not an oversight:** `FileMode.Create` truncates before the first byte, so a copy that
dies mid-stream leaves the destination empty or half-written. That is the left column — reported, and the
snapshot holds the original. The files these modules carry are a few kilobytes, so the window is
microseconds. It is real, and it is the price.

**A reason string that the revert silently falsified.** `ToFileStep` worded every failure "could not be
copied", which reads as *the destination was left alone*. That was accurate while the temp-file swap
guaranteed it, and became false the moment `CopyFile` went back to writing in place — without the sentence
being touched, which is precisely the drift a reason string cannot signal about itself. `CopyResult` now
carries `DestinationTruncated`, set immediately after the `FileStream` constructor returns (a constructor
that throws has truncated nothing), and the two failures are worded apart: *left unchanged* versus *now
incomplete and should be restored again or repaired by hand*. The difference is whether the live
`hosts` file the user is looking at is intact.

Coverage is asymmetric and labelled as such: the flag's **false** case goes through the real `CopyFile`,
the **true** case is a hand-built tally driving `ToFileStep`, because a genuine mid-`CopyToAsync` failure
is not reachable from this suite. The one line that sets the flag to true is not covered by a test.

**Now covered by a test that discriminates.** `RestoringOverALinkedFile_WritesThroughTheLink` creates a
**hard** link (`CreateHardLink` needs no privilege) and asserts the file behind it moved too. Verified by
reinstating the temp-file swap and watching it fail with `ORIGINAL`.

**How far the link claim is actually measured**, since the revert rests on it and the wording matters:

| fixture | privilege needed | result |
|---|---|---|
| hard link, in-place `Create` | none | both names stay in sync — link intact |
| hard link, temp + `Replace` | none | names split — link broken |
| junction (reparse point), in-place `Create` | none | wrote **through** to the real target; reparse point intact |
| **file symbolic link** | `SeCreateSymbolicLinkPrivilege` or Developer Mode | **not measured — unavailable in this session** |

The A/B on one fixture isolates the write strategy rather than arguing it, and the junction shows in-place
`Create` resolves *through* a reparse point instead of replacing it. What remains inference is the tag: a
junction is `IO_REPARSE_TAG_MOUNT_POINT`, a file symlink is `IO_REPARSE_TAG_SYMLINK`. Both are resolved by
the object manager during path parsing and .NET opens neither with `FILE_FLAG_OPEN_REPARSE_POINT`, so the
mechanism is the same one — but the mechanism is what was measured, not that specific tag. Stated at that
strength and no higher.

Two existing tests were re-scoped rather than deleted. `RestoringOverAHardenedFile_PreservesItsPermissions`
now passes for free — `FileMode.Create` truncates rather than replaces, so the security descriptor
survives — but is kept as the guard against any future scheme that stages through another file and
reintroduces the `File.Move(overwrite)` ACL trap. `SuccessfulCopy_LeavesNoTemporaryFileBehind` is likewise
now a regression guard rather than a live hazard. `FailedCopy_LeavesTheDestinationAtItsPreviousContents`
had its comment corrected: it pins that `CopyFile` does not truncate on its way to failing at the *source
open*, which is worth having, and it is explicitly not evidence of atomicity that no longer exists.

### `EEnvironment` gains an opt-in filtered sibling

`EEnvironmentFiltered` exports the same key and rewrites the `.reg` without values whose **names** match a
credential fragment list (`RegSecretFilter`). `EEnvironment` is unchanged.

The earlier text above argued no filter should be built, because name guesswork is wrong in both
directions. That was right about the filter and wrong about the conclusion: it assumed the filter would
**replace** the plain export, and a partial backup silently standing in for a complete one really would be
worse than an honest disclosure. Two separate tree entries make both failure directions visible instead —
a false positive is named in the step reason the user reads at backup time with the unfiltered module one
tick away, and a false negative is bounded by never claiming the export is free of secrets. The stale
reasoning is corrected in `EEnvironment`'s remarks rather than deleted, because it looked conclusive and
turned on an assumption it never stated.

Design points worth keeping:

- **The tree checkbox is the entire opt-in mechanism.** The app has no settings surface, and adding one
  for a single choice would be a larger change than the feature.
- **Whole logical values are filtered, never lines.** `regedit` wraps `hex(2)` payloads — how every
  `REG_EXPAND_SZ` variable including `PATH` is written — across many lines with a trailing backslash.
  Line-wise filtering would drop the naming line and leave the credential's continuation bytes behind as
  orphans, in a file that no longer parses. `AMatchingMultiLineHexValue_IsRemovedEntirely` covers this and
  was verified to fail without the continuation walk.
- **A filter failure discards the export.** At that point the file on disk is a complete export under a
  filename promising it excludes credentials — the worst artifact this module could leave. If the delete
  also fails, the reason names the path and says what is in it.
- **There are THREE continuation shapes, not one, and the third was found by measurement contradicting a
  stated belief.** The walk originally handled only regedit's trailing-backslash wrap. A comment asserted
  that a `REG_SZ` containing a CRLF would be escaped as `\r\n` and stay on one logical line. It is not.
  Measured 2026-07-21 — `Registry.SetValue(key, "MULTILINE", "line1\r\nline2")` then `reg.exe export`,
  unelevated, exit 0:

  ```
  "PLAIN"="ordinary"
  "MULTILINE"="line1
  line2"
  "EXPANDED"=hex(2):25,00,...,49,00,\
    4c,00,45,00,...
  "TRAILING_BS"="C:\\Users\\me\\"
  ```

  The newline is emitted **raw** — no escape, no marker. The same export confirms the two shapes the code
  was right about: `REG_EXPAND_SZ` gets the backslash wrap, and a value ending in a literal backslash still
  ends on its closing quote.

  **The cost of the wrong belief was a false refusal, not a leak.** The walk read the first physical line
  as a complete value, met `line2"` at the top level, could not account for it, and refused the entire
  export — so `AbandonUnfiltered` deleted it and the step went red. For any user with a newline in an
  environment variable: no filtered backup, ever, with a reason they cannot act on. Fail-closed kept it
  safe and made it useless. Blast radius was the filtered module only; plain `EEnvironment` never parses
  the file.

  Now a quoted value that does not close on its physical line continues to the closing quote, and one that
  never closes (a truncated export) fails the file. The over-consumption check is deliberately **not**
  applied to these lines: a backslash continuation carries hex payload, so a declaration inside one is
  always a boundary error, but a quoted continuation carries arbitrary user data where text shaped like
  `"NAME"="x"` is legitimate content — checking it would reintroduce the false refusal from the other side.

  **That exemption immediately let the credential leak back in, and it was caught by running it rather
  than by re-reading it.** The reasoning above says a quoted continuation's terminator is unambiguous.
  That is true and it is not the whole story: what follows the terminator is not. Measured:

  ```
  "PATH"="line1
  "GITHUB_TOKEN"="ghp_LEAKED"     -> Ok=True  removed=[]  credential retained
  xx"GITHUB_TOKEN"="ghp_LEAKED"   -> Ok=True  removed=[]  credential retained
  ```

  The quote that closes `PATH`'s string is the one at the *start* of the token line, so the walk swallowed
  that whole physical line — declaration and credential — into `PATH`'s block, found `PATH` innocent and
  wrote it back whole. The same silent leak as the backslash case, arriving through the shape exempted
  from the check that catches it. Fixed by requiring that nothing but whitespace follow the closing quote;
  a well-formed export always terminates at end of line, so this refuses none of the measured shapes
  (`line2"`, or a bare `"` for a value ending in a newline). Both variants pinned, both verified to return
  `Ok=True` and retain the credential without the check.

  **And a fourth shape, found by PR review and confirmed by measurement.** `Continues` decided "is this a
  hex wrap" from the trimmed line's LAST CHARACTER being `\`, which cannot tell a genuine marker from a
  still-open quoted string whose fragment ends in an **escaped literal backslash**. Measured — a `REG_SZ`
  whose value is `abc\` + a raw newline + `def`:

  ```
  "MULTILINE"="abc\\
  def"
  ```

  The old order ran the backslash walk first, so it swallowed `def"` as payload without ever inspecting it
  for a closing quote, then hunted for that quote one line too late. Real export, real result: the whole
  file refused. Fail-closed again, so no leak — but a total outage reached by *ordinary* content, since
  Windows paths end in backslashes constantly.

  Fixed by asking `OpensUnterminatedString` **first** and choosing the branch from the value's form, rather
  than adding a fifth special case. That function walks the escapes, so it knows `\\` is a literal
  backslash and not a terminator; a hex payload is not a quoted string, so exactly one branch can apply.
  Third time this parser was wrong about a continuation shape and the first time the fix was to stop
  guessing. Both the literal fixture and the real-`reg.exe` export test verified to fail without it.

  **The same defect then turned up one value-form over.** A DEFAULT value (`@="..."`, not
  `"NAME"="..."`) containing a newline was still refused, because the quoted-continuation walk keyed
  only on the named form. Found on real `reg.exe` output. Fixed by a `ValueStartOf` that knows both
  forms — deliberately *not* by teaching `TryReadDeclaration` about `@=`, since `IsUnaccountable` and
  the over-consumption check both use "parses as a declaration" as their test, and a default-value line
  must not look like a swallowed declaration. Two questions that share a prefix: *where does the value
  start* and *is this a named declaration*.

  Low reachability for this module — `HKCU\Environment` does not normally carry a default value — but
  `reg export` walks subkeys, so any subkey with a multi-line default trips it, and the cost is the
  whole feature rather than a warning.

  One test builds a real key and exports it with `reg.exe` rather than hand-writing the fixture, and
  asserts the raw newline and the `@=` form are present before filtering. Every other fixture in that file is a string
  literal encoding my beliefs about the format, and one of those beliefs was wrong; this one fails if the
  format drifts again. All four newline tests verified to fail without the rule.
- **A boundary error is refused in BOTH directions.** The check below was initially one-directional and a
  review caught it by running the code rather than reading it. `IsUnaccountable` only ever sees `block[0]`,
  so it catches a boundary read *early* — a stranded continuation lands at the top level and is inspected —
  and structurally cannot catch one read *late*, where the walk over-consumes and swallows the next
  declaration into a benign block:

  ```
  "PATH"=hex(2):43,00,\
    3a,00,\                      <- dangling continuation marker
  "GITHUB_TOKEN"="ghp_LEAKED"    <- eaten as more of PATH's payload
  ```

  `block[0]` is `PATH`, `PATH` is not a secret, so the block is written back whole with the credential in
  it — `Ok=true`, `removed=[]`. A filtered-looking artifact reporting success while holding exactly what
  it promised to exclude, reached from the opposite side of the same assumption. Now any *continuation*
  line that itself parses as a `"NAME"=` declaration fails the file. Verified to fail without the check.

  Not reachable from well-formed `regedit` output, which escapes backslashes inside quoted strings as
  `\\` so such a line ends on the closing quote. It is reachable from a **truncated** export — a `.reg`
  cut off mid-hex-block has precisely that shape — and truncated dumps are a hazard this codebase already
  acts on elsewhere, deleting partial captures because they pass a non-empty check and restore as if whole.
- **An export whose structure cannot be accounted for is refused, not half-filtered.** Found by running
  the filter against a real `HKCU\Environment` export: a malformed continuation produced a file where the
  credential's *name* had been stripped and its payload bytes were still present, and `FilterInPlace`
  returned `Ok`. That artifact looks filtered, passes a header check, and is not filtered. Any top-level
  line that is not the header, a blank, a `[key]`, a `"name"=` declaration or a `@=` default — most
  importantly a stranded continuation — now fails the whole filter. The refusal quotes the offending line
  truncated to 40 characters, because that line can itself be credential payload and the reason reaches
  the log and the results summary.

  Worth recording how it was found, because the trigger was a **bad test fixture, not a bug**: the fixture
  ended a hex line with `,\r\n` in C# source (comma, CR, LF) where a literal trailing backslash was
  needed, so it was never a continuation. The filter handled that input correctly. But the *failure mode*
  it exposed — name stripped, payload retained, success reported — is real for any future misread
  boundary, and it is silent. The fixture was fixed and the defence was added anyway.

  Verified against a genuine export afterwards: the injected credential and its multi-line payload are
  both gone, `PATH` and a three-line `TEMP` survive intact, and the result still passes `RegFile.Validate`
  with the correct UTF-16LE BOM.
- **Not tested end to end.** `Backup` shells out to `regedit`; the suite covers the filter and the two
  step-building methods directly, leaving the export→filter→aggregate wiring uncovered. Stated in the test
  file header.

## Open, and needing a decision rather than a fix

Nothing outstanding. The two entries that stood here — the plaintext-secrets exposure and the symlink
behaviour — were decided on 2026-07-21 and are recorded above.

## Deferred, with reasons

- **WSL configuration.** The state that matters lives inside distro filesystems; `%USERPROFILE%\.wslconfig`
  is only the outer shell of it. Honest coverage needs distro enumeration and per-distro export, which
  is a different shape of work from a file copy.
- **VS Code extension list and reinstall.** Reinstalling extensions is an `AStoreApps`-style dialog flow
  (the user picks a subset), not a file copy. Roadmap-deferred; unchanged here.
- **VS Code Insiders and VSCodium; per-profile settings under `User\profiles\`.** User decision,
  2026-07-21: stable, default profile only. Widening is additive and cheap; the file names are the
  compatibility surface and none of them would change.
- **`WM_SETTINGCHANGE` broadcast after an environment-variable restore.** See above.
