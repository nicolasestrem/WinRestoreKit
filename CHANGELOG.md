# Changelog

Notable changes to WinRestoreKit are documented in this file.

WinRestoreKit began as a fork of [Appcopier by Builtbybel](https://github.com/builtbybel/Appcopier)
and is a new application, versioned from 0.0.1. Entries below 0.0.1 describe Appcopier and keep its
version numbers, because that is what those releases were called.

## [Unreleased]

### Fixed: WPF restore completion and release truth

- Aligned the WPF shell assembly and displayed product version with the canonical 0.0.1 release.
- Cleared snapshot validation as soon as invalid input is corrected and exposed the full validation,
  severity, headline, and detail text to assistive technology.
- Made final restore consent resizable and scrollable so long paths wrap without clipping, and only
  promised a pre-restore snapshot for modules that actually write settings.
- Routed completed restores to the result workspace and prevented the same confirmation page from
  accidentally starting a second restore, while keeping long result summaries scrollable so the
  Timeline action stays reachable.

### Fixed: restore and snapshot integrity

- Verified successful manifest rows against their physical artifacts and kept unavailable modules out
  of restore sets, preventing stale metadata from authorizing an unsafe restore.
- Read the selected app export directly from its prepared payload and exposed friendly source names in
  the reinstall picker.
- Created a fresh, second-precision folder for every user backup and reserved collision suffixes
  atomically so repeated or concurrent runs cannot reuse an existing restore point.

### Fixed: WPF Timeline interaction and accessibility

- Added mouse double-click activation alongside Enter for Timeline restore points, with duplicate-open
  protection and a readable fallback when a failed snapshot has no diagnostic detail.
- Moved Timeline catalog reads off the UI thread and added explicit loading and failure states so slow
  or unavailable snapshot storage does not freeze the window or masquerade as an empty Timeline.
- Stacked the comparison evidence, restore set, and detail cards at narrow window widths while keeping
  the established two-column workspace at wider sizes.
- Honored the Windows client-area animation preference for indeterminate progress, exposed stable theme
  option automation IDs, and marked inactive alternative content as offscreen for assistive technology.

### Fixed: WPF lifecycle-safe navigation

- Added explicit Timeline and Advanced history destinations to the command bar so every read-only
  snapshot workspace is reachable, and bound Escape to Timeline so keyboard users can always leave
  Compare.
- Cancelled and cleared an active comparison before user-driven navigation, including when the same
  snapshot is opened again later.
- Disabled workspace navigation while backup or restore work is running, and prevented the final WPF
  window from closing until the active run finishes or is cancelled.
- Moved Advanced history catalog reads off the UI thread so a slow disk or large snapshot collection
  does not freeze the window.

### Added — WPF Timeline + Compare shell (side-by-side with WinForms)

A new WPF application (`WinRestoreKit.Wpf`) ships alongside the existing WinForms app, implementing the
approved Timeline + Comparison Workspace design direction. It does not replace WinForms yet; both run
from the same solution and share the same Core engine and Application layer.

**Timeline is the home experience.** Every snapshot and failed attempt on this PC appears as a card on a
point-in-time rail, newest first. Selecting a verified snapshot opens comparison; failed and unreadable
entries open their diagnostic reason. Arrow keys navigate the list and every custom visual exposes UI
Automation names, roles, and states.

**Compare shows evidence before restore.** Selecting a snapshot compares each module against the current
PC and reports honest state: captured, not captured, or changed. You build a restore set by selecting
whole modules, then move to a Confirm stage that reviews impact, warnings, and application-close
consent before any restore starts.

**Backup, Progress, and Results are migrated.** The backup workspace offers scope presets, destination,
compression, and validation. Progress shows stage, percent, throughput, byte counts, errors, warnings,
and a live log. Results present a severity summary with per-module outcomes.

**Visual system.** Neutral Windows surfaces, one mineral-blue action colour, one restrained coral
warning, Light/Dark/Follow-system themes, Segoe UI Variable typography, IBM Plex Mono for technical
identifiers, 6–10 px corner radii, flat panes, and visible keyboard focus throughout. Raw icon-enum
text (e.g. `ErrorCircle`) was eliminated at its source: status glyphs are vector paths keyed by tone,
never text.

**Application layer extraction.** Orchestration, backup presets, scope groups, run UI, snapshot
services, and update/theme services moved from the WinForms project into a new
`WinRestoreKit.Application` library shared by both shells. Core remains unchanged in its isolation
guarantee: no WinForms or WPF dependency can compile against it.

### Fixed — WPF crashes on snapshot creation and About navigation

Two read-only property bindings in the redesigned WPF views caused `InvalidOperationException` crashes:

- **About page** bound `Run.Text` to the read-only `CurrentVersion` property (TwoWay by default).
- **Progress view** bound `ProgressBar.Value` to the read-only `Percent` property (TwoWay by default).

Both are fixed with `Mode=OneWay`. Regression tests render each view through the layout dispatcher to
prove the crash path no longer fires.

### Design

- Started a three-direction visual identity and WinUI 3 Home-screen exploration around the
  positioning "Backup and retrieve the little details that make your system yours." The light Fluent
  direction now pairs granular selection of Windows and app settings with a quiet solid-color system:
  warm neutral surfaces, softened slate navigation, muted mineral-blue actions, gentler type, and
  calmer spacing. Logo exploration remains unselected and no runtime or backup-format behavior has
  changed.

## [0.0.1] - 2026-08-02
### Changed

- Rebuilt the app shell and all primary views with the Industry design system: bundled Barlow, Barlow Condensed, and IBM Plex Mono typography; Voltage, Flux, and Follow system palettes; blueprint frames; icon rail navigation; and a dedicated progress view.
- Added snapshot display names, selectable destination folders, Fast and Max archive compression, archive-backed restore discovery, live registry drift detection, rich backup progress metrics, and safe pause or cancel controls.
- Reworked backup, restore, History, Home, and About around real manifest and module data. Existing backup folders and frozen manifest keys remain compatible.


First WinRestoreKit version. The application was renamed from Appcopier and moved to a standalone
repository; the version series restarts here rather than continuing Appcopier's, because this is a
new application rather than a new Appcopier release.

### Changed - renamed to WinRestoreKit

The product, the executable, the solution, the projects, the assemblies and the namespaces are now
WinRestoreKit. The published artifact is `WinRestoreKit.exe`.

Development moved to [nicolasestrem/WinRestoreKit](https://github.com/nicolasestrem/WinRestoreKit),
a standalone repository outside the original fork network. The full commit history came with it, and
authorship is unchanged. The update checker, the download link, the repository button and the star
count now point at the new repository; the About screen credits Nicolas Estrem as maintainer and
acknowledges Appcopier separately as the original project.

The MIT licence is unchanged and the original 2023 Builtbybel copyright is retained, with the 2026
Nicolas Estrem copyright added alongside it. See `NOTICE.md`.

**Existing backups keep working.** The backup format did not change. The `app\` directory,
`backup_manifest.json` and every key in it, `manifest_version`, module identifiers, snapshot folder
naming and `.reg` file naming are all untouched, so an existing Appcopier backup collection is read
by WinRestoreKit as-is. Place `WinRestoreKit.exe` beside an existing `app\` directory, or copy that
directory beside the executable. Copy it rather than experimenting on your only backup.

Backup and restore logs written from now on carry a `WinRestoreKit` header line instead of an
`Appcopier` one. The header is only ever written and displayed, never parsed, so older logs are read
exactly as before and no backup is invalidated by the difference.


### Fixed — dark mode was largely unreadable

Several things went wrong at once and compounded each other.

**Anything the app drew after starting up came back in light colours.** The theme was applied once when
the window opened, but the restore lists, the History timeline, the results rows and the Home screen are
all rebuilt as you move around — and everything rebuilt that way reverted to Windows' default white. The
warning under an item in the restore wizard was the clearest case: a white block of text in an otherwise
dark window. All of those screens are re-themed as they are built now.

**Nothing had edges.** Cards, result rows and text boxes were painted a shade so close to the background
that the boundaries were invisible, and the outlines meant to separate them were fainter still. Both are
lighter now, so a backup card looks like a card. The text colours were never the problem and have not
moved.

**A divider on the home screen vanished** whenever you navigated back to it, for the same reason.

**Items with nothing to restore could not be read.** Those rows are greyed out on purpose, but Windows
draws a disabled control's text in its own grey and ignores the colour the app asks for, so on a dark
background they came out close to invisible. The wording now sits beside the checkbox rather than on it,
which puts the colour back under the app's control. Screen readers still announce each one by name.

Light mode is unchanged.

### Fixed — the History timeline showed nothing at all

Every row was being built and then laid out zero pixels wide, so History was a blank page no matter how
many backups you had. Rows appear now, and clicking one shows its log — previously only a few pixels of
background between the rows responded to a click, so the log pane stayed empty however carefully you
aimed.

### Fixed — long warnings in the restore wizard were cut off mid-sentence

The text box was a fixed height regardless of how much text it held. It is measured now, so warnings are
shown in full.

### Fixed — the restore wizard now really does grey out what a backup does not contain

Picking a backup and being shown *every* item, all pre-ticked, is what the previous build did — and the
greying that was supposed to prevent it never fired. Restoring from a backup that held one item's
settings would take a snapshot for all thirty, close applications for them, and report thirty results.

Now a backup shows only what it actually holds. Everything else is greyed and unticked with **"(nothing
in this backup)"** beside it, and the warning text under an item appears only when that item can
actually be restored — so the list is a screen shorter and the things you *can* restore are visible
without scrolling past a dozen notices that do not apply.

The wizard trusts the backup's own record of what it wrote, and looks in the folder for anything that
record does not mention. An older backup taken before that record existed is still fully restorable.

### Fixed — the Back button in the restore wizard was invisible

On step 2 of the restore wizard, "Back" was painted underneath "Next" and could not be seen or clicked,
so the only way out of that screen was the navigation rail. Both buttons now sit side by side.

### Fixed — cancelling a restore no longer strands you

Backing out of the confirmation dialog returned you to your ticked list with "Next" greyed out, and the
only way to get it back was to leave the wizard and pick the folder again. It comes back now.

### Fixed — the update check froze for ten seconds instead of five when offline

Checking for updates with no connection probed for one twice, back to back, before saying so.

### Fixed — amber and grey text after switching Windows themes

Flipping Windows between light and dark while Appcopier was open left warning and secondary text in the
outgoing theme's colour, which on the dark surface meant dark amber on near-black. Those colours now
move with the theme and keep their meaning.

### Changed — dark mode, and the app follows Windows (Phase 4, PR 9)

Appcopier now reads your Windows "app mode" setting and paints itself to match, **including the title
bar**. Flip Windows between light and dark while the app is open and it changes with you — no restart.

Everything that carried meaning keeps carrying it in both themes. In particular, an item that was
**skipped is still amber and never green**: "there was nothing to back up" and "it worked" are
different answers and the colours keep saying so.

Message boxes and the file-picker style dialogs stay light in dark mode. That is a Windows limitation
rather than an oversight, and it is a small surface now that results are shown on the page instead of
in a popup.

The app is also **per-monitor DPI aware**. On a multi-monitor setup with different scaling — a 4K
laptop screen next to a 1080p monitor — dragging the window between them now re-renders it crisply at
the new scale instead of showing a blurry bitmap stretch.

### Changed — the update check asks GitHub for the actual release (Phase 4)

Checking for updates now asks GitHub for the latest published **release** rather than reading a version
number out of a source file. The practical difference: you are only told about a version that actually
exists as a download. If GitHub cannot be reached or rate-limits the request, it silently falls back to
the old source-file check, so the feature never gets worse than it was.

### Added — History: every backup and every undo point, on one timeline (Phase 4, PR 8)

A new **History** entry on the left lists everything Appcopier has written, newest first: your backups,
and the **undo points** it takes automatically before each restore. Selecting a row shows its full log.

Every row offers "Restore from this backup" — or, on an undo point, **"Undo this restore"**, which
reopens the restore screen pointed at that snapshot. Rolling back a restore has always been possible;
this is the first release where you can actually see and click it.

### Changed — Restore now starts from the backup, not from a checklist (Phase 4, PR 7)

Restore used to make you tick items *before* it would show you your backups, then restore those items
from whichever folder you picked — whether or not that folder contained them. It now runs the other way
round, in two steps:

1. **Pick a backup.** Each one is a card showing when it was made, how many items it holds, how many
   failed, and which PC and account it came from.
2. **Pick what to bring back from it.** Every item in the list is marked with what that backup actually
   holds for it: "OK in backup", "failed in backup", "skipped", or nothing at all when the backup is too
   old to say. **Items the backup holds nothing for are greyed out and cannot be ticked** — that
   surprise used to arrive only after the restore had run.

If the backup came from a different PC or a different user account, a line at the top says so, because
some settings will not resolve anywhere else.

The confirmation dialog is unchanged: it still lists exactly what will be overwritten in the same words,
still opens with its consent boxes unticked and **Cancel** focused, and the "restore anyway without an
undo point" prompt still defaults to **No**.

### Changed — the Back up screen has presets, and results appear on the page (Phase 4, PR 6)

Back up now opens on four choices instead of a 29-item checklist: **Everything on this PC** (with a live
count of what was found), **Developer machine**, **Minimal privacy-safe** (which leaves out the Windows
Update identity, your environment variables and your Wi-Fi keys), and **Custom**. The full item tree is
still there, one click away under "Advanced: full module list", and touching any tick switches you to
Custom.

**Results no longer arrive as a popup.** When a run finishes, every item appears on the page with a
coloured state chip and its own reason, **failed items first**. A run where 24 items worked and one
failed now looks like exactly that, instead of one dismissible paragraph that read as broadly green.
The reasons are selectable text, so you can copy one straight into a bug report.

An item's warning is now shown **inline, next to the item, while you are choosing** — it used to
interrupt you with a popup the moment you clicked the item, which is when you could least act on it. A
line above the list counts how many of your ticked items carry one. Nothing is lost: the confirmation
dialog before a restore still repeats every warning.

The "restart File Explorer" prompt is now a normal highlighted row in the results instead of a hot-pink
banner button, and the activity log has moved behind an "Activity log" toggle so it stops doubling as
the help text.

### Changed — the app opens on a Home screen, with a navigation rail (Phase 4, PR 4)

The window now has a list down the left — **Home**, **Back up**, **Restore**, with **About** at the
bottom — and opens on a new Home screen instead of dropping you straight into the list of items.

Home answers "am I okay?": the name of this PC, how long ago the last backup was and which folder it is
in, how many items it contained and how many failed, and **the failure reason for each failed item,
quoted in full and pinned above everything else**. Those reasons sit in boxes you can select and copy,
so the text can go straight into a bug report. Below that: how many undo points (pre-restore snapshots)
exist and how much room is left on the drive your backups are written to.

**A backup Home cannot describe is shown as "details unavailable", never as a result.** Backups made
before the previous entry's `backup_manifest.json` existed have nothing to describe them, and neither
does a run that was interrupted. Guessing at those could show you a clean result that was never true,
so Home says it does not know. Those backups are intact and restore exactly as before.

**An item with no recorded outcome is counted separately, as "not recorded".** It is not folded into
"none failed": a run where an item never reported back is not the same as a run where it went fine, and
the reason it gives is shown in full just like a failure.

**A backup folder that cannot be read says so**, rather than reporting that you have no backups. The
two are very different things to be told, and only one of them is ever good news.

"Back up again" on Home takes you to the Back up screen with the same items ticked that the last backup
recorded. If that backup named an item this version no longer has, it is skipped without complaint.

**The screens themselves have not changed yet.** Back up and Restore are the existing pages, moved
behind the rail unchanged — the redesigned versions, with presets and a restore wizard, come in later
releases. Restore still asks you to tick what you want to bring back before it offers the list of
backups, and the confirmation dialog before a restore is untouched.

The window is wider than before, by the width of the rail, so the existing pages keep the room they
were built for.

**The list on the left is greyed out while a backup or restore is running**, and comes back when the
run finishes. Navigating away mid-run would hide the very screen reporting progress, and some of those
buttons change the selection the run is working from.

**Removed:** the desktop-wallpaper picture shown for half a second at startup, and the QR code that
offered to open the introduction in a browser. Both were decoration on a screen that no longer exists.
The version number, the update check and the storage estimate are unchanged and now sit along the
bottom.

### Added — backups now record what happened, in a file the app can read back (Phase 4, PR 3)

Every backup writes a `backup_manifest.json` next to the existing `backup_log.txt`. It lists each item
you backed up, whether it succeeded, was skipped or failed, and the reason it gave, along with the time
of the run, the PC and account it was taken on, the Windows build, and the app version.

The log file is unchanged and is still the one written for you to read. The new file exists because the
screens arriving in the next releases have to state things like "24 items, 1 failed", and working that
out by parsing the wording of the log would mean a guess that looks like a fact when it is wrong.

Three consequences worth stating plainly:

- **Backups made before this version keep working and stay restorable.** They have no manifest, so the
  new screens will show "details unavailable" for them rather than a result. That is deliberate: an
  invented green tick on a backup nobody checked is worse than an honest blank.
- **The file is written once, at the very end, and appears complete or not at all.** A run that is
  interrupted — a crash, a forced close, power loss — leaves no manifest, which reads as "details
  unavailable". It cannot leave a shorter file that still parses, because that would present a
  half-finished backup as a smaller successful one.
- **If the manifest cannot be written, the backup is not affected.** The failure is noted in the log and
  your data is still there; only the summary of it is missing.

Pre-restore snapshots do not get a manifest yet. They are listed separately from your own backups, and
the screen that presents them has not been built.

### Changed — the engine moved into its own library (Phase 4, PR 2)

Nothing about what the app does changed here, and nothing you can see changed either. The backup and
restore code was moved out of the application into a separate `Appcopier.Core` library, so the interface
can be rebuilt over the coming releases without the engine moving underneath it.

Two consequences worth recording, because they touch things that are easy to break silently:

- **Where backups are written is unchanged.** The path is built from a different API now, and since the
  released app ships as a single self-contained executable — a mode neither the build nor the test suite
  ever exercises — the replacement was measured against the old one in exactly that published form
  before it was adopted, rather than assumed to be equivalent. Existing backup folders are found and
  restored exactly as before.
- **The update check is unchanged**, including the file it downloads and the exact path it downloads it
  from. Older installed versions read that path directly, so it cannot move.

The app restore item ("Remember installed apps") now reports a **failure** rather than a skip in the case
where its dialog cannot be opened at all. Previously both outcomes looked identical, and one of them was
a success.

### Fixed — two long-standing resource leaks

Both were found by review of this PR and both predate it; neither is visible in normal use.

- The app reinstall dialog was never disposed after being closed, so its window handle and every
  drawing object on it stayed allocated until the app exited. Opening it repeatedly in one session
  leaked once per open.
- The update check left its network client undisposed on every run.

### Planned — a rebuilt interface (Phase 4)

Nothing here has shipped yet. This entry records the direction so the change is not a surprise when it
starts landing; the reasoning is in
[`docs/superpowers/specs/2026-07-21-phase4-ui-revamp-design.md`](docs/superpowers/specs/2026-07-21-phase4-ui-revamp-design.md).

The app is being rebuilt around the three things people actually do with it — **keep this PC backed up**,
**recover onto a PC**, and **check that it worked** — instead of around the list of 29 items. There will be
a Home screen that opens with how long ago your last backup was and which items failed, a Back up screen
with sensible presets (the full list stays, one click away), a Restore flow that starts by asking which
backup you want and then shows you what is actually in it, and a History screen listing your backups and
undo points together.

Four things are worth knowing in advance:

- **The pop-up warning on every item you click is going away.** It will be shown on the item itself
  instead, so you can read it without dismissing anything, and you will see it before you tick the box
  rather than after. Nothing is lost where it counts: the confirmation dialog before a restore has always
  repeated every warning, and that dialog is not changing.
- **The confirmation dialog before a restore is deliberately not changing.** It still stops everything,
  still lists exactly what would be overwritten, still starts with every "close this app" box unticked,
  and still has Cancel selected when it opens. That is the one place an interruption is the point.
- **The results pop-up is going away too, and you will see more, not less.** The same summary appears on
  screen as a list you can read at your own pace, one row per item, failures at the top, with the reason
  text selectable so you can paste it into a bug report. Today it is one paragraph in a box most people
  click away without reading.
- **Backups will gain a `backup_manifest.json` file** next to the existing `backup_log.txt`, recording
  what was captured and how each item turned out. The new screens need it to say anything truthful about a
  backup. **Backups made before this change keep working and stay restorable** — they simply show
  "details unavailable" where the outcome would go, because guessing from the old text log could show you
  a green result that was never true. The same reasoning covers a backup that is interrupted partway: the
  file is written once, at the end, so a run that never finished leaves no manifest and reads as
  "details unavailable" rather than as a smaller backup that succeeded.

Dark mode and proper multi-monitor scaling arrive as part of this work. On .NET 8 dark mode has to be
applied by hand, so a few Windows-supplied pieces — message boxes, scrollbars, the file picker — will stay
light. That is a limit of the toolkit, not an oversight.

### Added — power-user settings (Phase 3c)

Four new items in the **Settings** section, covering state the app could not previously save.

- **Power plans.** Every power plan on the PC is exported, and the app records which one was active. **Restoring re-activates the plan that was active when you made the backup — it does not recreate plans.** If that plan no longer exists on this PC the item reports a failure rather than pretending, and names the exported `.pow` file you can add by hand with `powercfg /import`. That limit is deliberate: importing a plan creates a new entry Appcopier has no way to remove again, so the automatic pre-restore snapshot could not undo it while still telling you the restore was reversible. Changes made to a plan's own settings since the backup are not reverted either.
- **User fonts.** Fonts you installed for just your account — both the font files and the settings telling Windows where each one is. **Those settings are full paths containing your Windows user name**, so on a PC where your account is named differently the font files come back but Windows cannot find them, and the app cannot detect that: the row still reports success. This is the same limitation the Themes item has with the wallpaper path. Restored fonts appear in an application only after that application restarts.
- **Mapped network drives.** Which server share each drive letter points at. **Saved passwords are not included** — those live in Windows Credential Manager, which this item does not read — so a drive needing credentials will ask for them the first time you open it. Restored mappings appear at your next sign-in, and a mapping to a server that no longer exists comes back as a disconnected drive.
- **Regional & input settings.** Number, date, time and currency formats, your country setting, and your keyboard layouts and input languages. **Restoring lists languages and layouts by identifier; it does not install language packs**, so an entry for a language this PC does not have stays inert until that pack is installed. Most of it takes effect at your next sign-in.

### Changed — taskbar, Windows Update and Start menu (Phase 3c)

- **The taskbar item now includes your pinned apps.** It previously saved only the taskbar's *shape* — alignment, size, Widgets — which is not where pins live, so restoring a backup gave you the settings back and an empty taskbar, and nothing said so. It now also captures the list Windows keeps of your pinned apps and the shortcut files that list points at. **Backups made with earlier versions still restore the taskbar settings exactly as before** — that part keeps its existing filename and is not orphaned — but they contain nothing for the pins, and will report that. Re-run your backup to capture them.

  Three things about restoring pins are worth knowing. **Use the Restart Explorer button before you sign out:** Explorer keeps the pin list in memory and writes its own copy back when it exits normally, so signing out without restarting first can overwrite what was just restored. **The pins only come back on the same Windows account they were saved from** — Windows stores that list as full paths containing your user name, so on a PC where your account is named differently, or on a rebuilt profile, Windows cannot find the shortcuts and quietly drops the pins, leaving the taskbar empty; the app cannot detect that and will still report success. And the shortcut files are **merged** rather than replaced — shortcuts you pinned since the backup stay on disk, and what actually appears is decided by the restored pin list.
- **The Windows Update item drops the old WSUS policy setting and picks up Delivery Optimization.** The `...\WindowsUpdate\AU` policy key it used to save exists only where a company update server or Group Policy created it; on an ordinary PC it was simply absent and the item reported it skipped forever. In its place the item now saves the **Delivery Optimization** policy — whether Windows shares updates with other PCs — which is a setting people actually change. It too is only saved when someone has configured it; on a PC where nobody has, the item reports it as not present, and that is normal rather than an error.

  **Consequence for existing backups, stated plainly:** the file `Windows Update_HKEY_LOCAL_MACHINE_Software_Policies_Microsoft_Windows_WindowsUpdate_AU.reg` in backups made before this version is no longer read at all — not even as a skipped row, because the setting is gone from the list entirely. It is still a valid `.reg` file, so if you did have those policy values and want them back, you can apply that file by hand.

  **A warning was also added that was missing before.** The Windows Update settings this item saves include the ID numbers Windows Update uses to recognise this particular installation of Windows. Restoring them onto a different PC — **or onto the same PC after reinstalling Windows, which gives it new ID numbers** — puts the old identity back, and that can confuse Windows Update, or a company update server, about which machine is which. The item has always saved these; it just never said so. Since backing up, reinstalling Windows and restoring is what this app is mostly used for, the warning now says plainly that nothing here is needed to make Windows Update work on a fresh install, and suggests leaving the item unticked after a reinstall.
- **The Start menu pinned-apps warning now describes the risk that actually matters.** It said only "This is reserved for Windows 11." What it left out is that Windows builds that pinned list for one specific PC and one specific Windows build, so restoring it on a different PC, or after a major Windows update, can bring back the wrong pins or leave the Start menu empty — and **the app cannot detect that, so it will still report success.** The item was kept rather than retired because restoring it on the PC it came from is a genuine use case; the text now says which case is which.

### Fixed — the Restart Explorer button after a partly-failed restore (Phase 3c)

- **The Restart Explorer button no longer disappears when part of a restore fails.** It was shown only when a restore succeeded *completely*. That was right while the items needing it wrote a single thing — a failure then really did mean nothing had changed, so offering a restart would have been a button that did nothing. It stopped being right once those items began restoring several things at once: one failed piece makes the whole row report failure, so a restore that successfully put back all 32 of your pinned taskbar apps and then failed on a later step would hide the button — **immediately after telling you to press it before signing out.** Sign out instead, and the still-running Explorer writes its own copy of the pin list back over the restored one, and the pins are gone. The button now appears whenever the restore actually wrote something, and still stays hidden when nothing was written. This also affected **Themes**, which has restored a folder plus two registry keys since the previous release.

### Added — developer tooling (Phase 3b)

A new **Developer** section in the list, with the things a developer would miss after moving to a new PC.

- **Windows Terminal settings.** Your profiles, colour schemes, key bindings and startup behaviour. All three ways Terminal gets installed — from the Store, the Preview build, and the unpackaged build that scoop and choco install — keep separate settings files, and whichever of them exist on your PC are all backed up. **Windows Terminal must be closed before restoring**, and you are asked before it happens: it rewrites its settings file when it exits, so an open window would quietly overwrite what was just restored — and closing it ends every open tab and anything still running in them.
- **VS Code settings.** Your user settings, your custom key bindings and your snippets folder. **VS Code must be closed before restoring**, for the same reason and with the same question asked first, and closing it discards changes in any editor you have not saved. Installed extensions are *not* included — those are reinstalled from the Marketplace rather than copied, and doing it properly needs the pick-a-subset dialog the app already has for Store apps. VS Code Insiders and VSCodium are not covered.
- **SSH client configuration.** The host aliases and per-host options in `.ssh\config`, and the recorded server fingerprints in `.ssh\known_hosts`. **Your private keys are deliberately not backed up.** Appcopier writes its backups as ordinary unencrypted files in a folder next to itself, which is the wrong place for a key: it would sit there in plaintext, survive in every backup folder you forget to delete, and bypass the passphrase that protects the original. Generate new keys on a new PC instead. This is structural rather than a filter — the app copies the two files named above and never reads the rest of the folder, so a key cannot be swept up by accident.
- **Environment variables.** The variables belonging to your Windows account, including your user `PATH`. Restoring **merges** them back: variables saved in the backup are overwritten and anything you have added since is left alone — so if a program installed after the backup added itself to `PATH`, its entry is replaced and that program may stop being found until you re-add it. You are warned about this before restoring. Programs already running keep the values they started with until you restart them.
- **Environment variables, excluding likely secrets.** The same thing, with any variable whose *name* looks like it holds a credential left out — names containing `TOKEN`, `SECRET`, `PASSWORD`, `API_KEY` and similar. It appears as a separate tick in the list next to the full version, so you choose which you want and the original behaviour is unchanged; ticking both is fine and gives you one complete backup and one you could hand to someone else. Every variable it holds back is **named in the result**, so you can see exactly what is missing at the moment you make the backup rather than discovering it at restore time.

  **This is not a guarantee that the backup holds no secrets.** It matches names, so it catches `GITHUB_TOKEN` and does not catch a token you called something else, and what remains is still written unencrypted. It is a way to reduce what ends up in a backup folder you might share or forget about — not a safe. The backup is also deliberately *incomplete*: restoring it will not bring those variables back on any machine.

  **If it cannot make sense of the exported data, it produces no backup at all** rather than a partial one it cannot vouch for — a red row, and nothing saved for that item. That is the safe direction, but it is a real cost: the item simply does not work for you until the cause is fixed, and the message will not tell you what to change. The full **Environment variables** item is unaffected and remains available.
- **The hosts file.** The manual name-to-address mappings used to point a domain at a local server or to block one. **This one is shared by the whole PC**, not just your account: restoring it changes what every user and every program on the machine resolves, and entries added since the backup are replaced rather than merged. It is the only item in this section that writes outside your own profile, and it says so before you agree.

Three safety fixes came out of reviewing the above before it shipped, and they apply to every item in the new section:

- **A file Appcopier is not allowed to read is no longer reported as one that is not there.** The check asked Windows "does this file exist", and Windows answers no both when there is nothing there and when it will not let you look — so a settings file inside a folder with restrictive permissions was reported with a green "not present on this system" and quietly left out of the backup. This is the same mistake that was fixed once before in the app-restore dialog. It matters most for SSH: locking down `.ssh` permissions is the standard fix for OpenSSH's "permissions are too open" warning, and it was enough to make your config vanish from the backup without a word.
- **Appcopier no longer closes an app for a restore that has nothing to restore.** Windows Terminal and VS Code are closed before their files are overwritten, which costs you every open tab or unsaved editor buffer. The check deciding whether that was necessary only looked for a folder in the backup, and that folder can exist while holding nothing — or hold another PC's files rather than yours. It now looks for the actual file it would restore.
- **Restoring a file no longer widens who can read it.** A file you had deliberately locked down could come back inheriting the permissions of the folder around it. That mattered for two of the new items in particular: `.ssh\config`, where removing inherited permissions is the standard advice and the file names your internal hosts and usernames, and the hosts file, which can carry a deliberate anti-tampering permission set. Restoring changes the contents and leaves the permissions exactly as they were.
- **Restoring a file that is a link into a dotfiles repository updates the repository, instead of dismantling the link.** Symlinking `.ssh\config` or VS Code's `settings.json` into a git-managed dotfiles repo is a common arrangement among exactly the people this section is for. An earlier version of this work wrote each file alongside the original and swapped it into place — which made a half-finished restore harmless, but replaced the link with an ordinary file and left the repository behind it silently stale. Restoring now writes through the link, the way any editor saving that path does.

  That reversed a decision made earlier in the same release, so the reasoning is worth stating: the swap protected against a restore dying mid-write and leaving a truncated file, but that failure is *reported* (the item goes red) and *undoable* (the automatic pre-restore snapshot holds the old contents). Breaking a link was neither — every row stayed green, and the snapshot records what files contain, not which of them are links, so it could not put one back. Guarding a loud, recoverable failure at the price of a silent, permanent one is the wrong way round.

  **And when that does happen, the app now says so specifically.** A failed file restore used to report "could not be copied", which reads as *your file was left alone* — true when the restore never got as far as writing, and false when it died half-way through. Those two now read differently, so the message tells you whether there is a damaged file to repair or nothing to do.

  **The cost, stated plainly:** a restore interrupted part-way through writing a file — a full disk, a read error, or Appcopier being killed — can now leave that file empty or half-written, where the swap would have left it untouched. In the first two cases the item reports red and the pre-restore snapshot holds the original. If Appcopier is *killed* outright, nothing is reported at all — no summary and no restore log — though the snapshot is taken before the restore starts, so the original is still there to recover by hand. These files are a few kilobytes each, so the window is very small; it is not zero.

The confirmation dialog now distinguishes a **file** from the folder containing it. It previously knew only about registry keys, folders and commands, so these items would have had to describe themselves as either a whole folder — claiming more than they touch — or as an unlabelled path. Restoring `C:\Users\you\.ssh\config` and restoring `C:\Users\you\.ssh` are very different promises, and the text you agree to now makes that difference visible.

### Changed — shared module bases (Phase 3a)

- **The near-identical modules now share their logic instead of copying it.** Five modules that each carried the same multi-key registry template verbatim now inherit a `MultiKeyRegistryModule` base, and the folder-copy shape lives in a `FolderModule` base — so the skipped-vs-failed rules, the restore-side wording, and the one-file-per-key naming are written once and cannot drift between modules that are supposed to behave identically. Behavior-preserving by design: every backup keeps its existing filenames, and the reflection-driven tests that pin those promises pass unchanged.
- **The two hand-rolled `netsh` runners are now one.** Network configuration and Wi-Fi each carried a private copy; one redirected the error stream and the other could not without risking a blocked pipe. The shared runner drains both streams concurrently, bounds its wait, kills the process tree on timeout, and both modules now report the full outcome ladder (could not start / did not finish / exited non-zero / wrote no file) instead of a bare exit code. A capture that cannot be fully written to disk removes whatever partial file it produced — a truncated dump passes a non-empty check and would restore as if it were whole — and the run is failed outright if even that removal fails. The same rule holds for the files the Wi-Fi export names itself: an export that dies part-way removes the profile files it had already written — the restore side finds profiles by content in that same folder, so a backup reported as failed would otherwise still restore a partial set — and any file it cannot remove is named in the failure as one that would still restore. winget deliberately keeps its own runner: the app-restore dialog shows winget's console window as its only progress display, which is incompatible with captured streams.
- **The `AllowPrompts` flag is gone.** It was shared mutable state a 2b review flagged as unsafe-by-convention — set on the module before each backup, reset in a `finally`, correct only as long as every caller remembered. Its only readers were the browser modules retired below, so the mechanism went with them rather than being redesigned; no shipped module prompts during backup at all now, and a note in the code records what a future prompting module must do instead.

### Removed — browser modules and USB (Phase 3a)

- **The Chrome, Edge and Firefox items are retired.** They copied whole profile directories — caches, GPU data, and databases the running browser holds locked — and every browser's built-in sync restores a profile better than a local file copy can. **Backups made with earlier versions keep their browser folders on disk, but this app no longer restores them**; use the browser's own sync or import the folder by hand.
- **The USB Devices item is retired.** It captured one narrow shell-notification key that many installations never create, while its description claimed "Windows USB Devices settings" — a promise far larger than its contents. Existing backups keep the exported file; there was rarely anything in it.

### Fixed — known module bugs

- **Telemetry now backs up the control set Windows actually booted.** It read `ControlSet001` by name, which is merely the usual one; Windows resolves `CurrentControlSet` to whichever it started from. This never announced itself as a failure, because after a boot that promotes a different control set the old one normally still exists as a stale copy — so the export succeeded, the row was green, and the data was service configuration the running system was not using. Restoring wrote it back to that same unused copy and reported success again. **Backups taken before this version no longer restore this item**: the file is named after the key, and the key changed, so it now reports "nothing was backed up for this item" rather than pretending. Please re-run your backup. Making it read the old file instead would have been worse than the skip — that file's contents name the stale location, so applying it would have quietly re-created the original problem while reporting success.
- **Restoring Telemetry now warns you first.** The corrected key means a restore writes to a live Windows service rather than to an unused copy of its settings, and Appcopier can only confirm the key exists afterwards — not that what is now in it describes a service this build of Windows can start. Restoring it from another PC or another Windows version can leave the diagnostics service unable to start, so you are told before you agree rather than after.
- **The app no longer refuses to start when a registry value is missing.** The panel that reports your Windows build read three registry values without checking whether they were there. On an imaged, sysprepped or container installation one of them genuinely is not, and because that code runs while the main window is still being constructed — before Windows Forms has anything that could catch it — the app simply terminated. No window, no error, no log entry: from the outside it just did not open. It now reports whatever it can read and says so plainly when it cannot.
- **A startup failure now tells you what happened.** Anything that goes wrong while the app is starting shows a message naming the error instead of vanishing. The failure is still recorded in the Windows Event Log, exactly as before.
- **Themes now backs up the setting that says which image is your desktop background, not only the image itself.** The wallpaper's pixels were already being copied; nothing was copying the pointer to them, so a restore had nothing to tell Windows which image to use. Note the pointer is a full path containing your Windows user name — on a PC where your account has a different name it will not resolve, and Appcopier cannot detect that, so the item is documented rather than silently trusted.
- **A module that captures more than one registry key now writes one file per key.** Themes built its filename from the module name rather than from the key. That was harmless while it had a single key and was never able to lose anything; it became a real hazard the moment a second key was added, which is what this release does. Had it shipped that way, the second export would have overwritten the first while both reported success, and the restore would have applied one file twice.
- **The app-restore dialog no longer clears every app you ticked when you open the backup dropdown.** Its repopulate step was wired to the dropdown's click as well as to actually changing the selection, so glancing at the list of backups silently discarded your choices in the one dialog whose entire purpose is choosing a subset. Changing the selected backup still repopulates the list, which is the behaviour that was intended all along.
- **The app-restore dialog no longer runs two installs at once.** Nothing disabled the Restore button while a run was in progress, so a second click started a second elevated install loop alongside the first. Restore is also disabled when the selected backup has no app list, rather than being clickable over an empty list — which was the state the dialog opened in on a PC with no backups yet. Closing the window mid-install now stops after the current app finishes instead of being refused, and no longer blocks the app from quitting or the PC from shutting down.
- **A backup with no app export now says so instead of showing the previous backup's apps.** Selecting a backup that contains no app list left whatever was on screen from the last one, under the new backup's name. It is now emptied and reported. A backup that simply has no app list is not treated as an error, because most backups legitimately have none.
- **An app list Appcopier is not allowed to read is no longer reported as one that does not exist.** The check answered "does this file exist", and Windows answers that with "no" both when there is nothing there and when it will not let you look — a locked folder, a permissions failure, a path that cannot be resolved. Only the first of those is normal, and it is the one that shows no error, so a backup whose app list could not be reached was described to you as a backup that contains no apps. It now says it could not read the list, and names why.
- **Closing the app-restore dialog while it is installing can no longer take the whole app down with it, or leave a message stuck behind the main window.** The dialog waits for the current app to finish when *you* close it, but not when Windows is shutting down or the main window is going away — and in those cases the install loop finished against a window that was no longer on screen. That either ended the app outright or produced a message box owned by an invisible window, which could sit behind the main window and look like a freeze. It now checks whether there is still a window to speak through and writes the summary to the log if there is not. Be aware of the limit: when the whole app is closing, the log is closing with it, so a summary produced at that moment can still be lost. Recording it somewhere that outlives the app needs a log file on disk, which Appcopier does not have yet.
- **A failed app-list read now appears in the log.** The message describing it was passed to the logger as a formatting template, so any brace in the text discarded the line — and the text was a JSON parsing error, which is made of braces. The one message needed to work out why an app list would not load was the one guaranteed to be thrown away.

### Removed

- **Themes no longer copies `C:\Windows\Web\Wallpaper`.** That folder holds the wallpapers Windows ships with — measured on 20 July 2026: 20 files, 20.0 MB, identical on every Windows 11 installation — so about 95% of this item's size was images the other PC already had. It was also the only place this item wrote outside your own profile, into a folder shared by every account on the PC, which meant its most dangerous write was also its least useful one. If you added your own images to that folder they are no longer backed up; the wallpaper you are actually using still is.

### Fixed — restore safety follow-ups

- **A module with a broken restore declaration no longer crashes the restore.** The dialog that lists what a restore will overwrite could fail while being composed, at a point with nothing to catch it, which surfaced a Windows Forms error dialog part-way through. It now marks the individual bad entry and, if the description cannot be produced at all, abandons the restore and says so — nothing is written when Appcopier cannot state what it would write.
- **The snapshot check no longer claims your selection changes nothing when it cannot tell.** A pre-restore snapshot given nothing it could interpret reported that none of the selected items change anything — the same false reassurance a previous fix removed by another route. It now counts what it could not interpret as a failure, which is what forces the prompt rather than passing quietly.

Deliberately not fixed in this release, and still open: the Explorer restart probe's missing settle delay (it needs measuring before it is changed, not guessing); the app-restore dialog choosing its backup folder from its own dropdown rather than from the one you picked to restore — so restoring one backup can still install apps listed in another; and the absence of a log file on disk, which is why a message produced while the app is closing has nowhere to survive. The shared `AllowPrompts` flag, listed here when this was written, was since removed outright in Phase 3a (see above) — its last consumers were the retired browser modules.

### Added — restore is now reversible, consented to, and recorded

- **A restore takes a snapshot of what it is about to overwrite.** Before anything is written, Appcopier backs up the current state of the items being restored into its own timestamped `(pre-restore)` folder. That folder appears in the restore list like any other backup, so undoing a restore is the ordinary restore flow rather than a special mode. The snapshot is a real backup, not a bespoke format, which means it carries the same export verification and the same honest per-module reporting as one — and, crucially, that its *failure* can be detected.
- **A snapshot that did not complete stops the restore and asks again.** The prompt names what could not be captured and defaults to No. Continuing anyway is allowed, because sometimes it is the right call, but it is recorded in the restore log as having run without a working snapshot. Proceeding silently was never an option: doing that after offering a snapshot is worse than never offering one, because the user believes they have a fallback.
- **Stopping there tells you what the attempt already cost you.** If the snapshot failed after browsers had been closed to take it, declining to continue says which applications were closed rather than only that nothing ran — the browser was shut for a restore that then did not happen, and reporting that as "nothing happened" would be the same misdescription this work exists to remove.
- **Restoring says what it will overwrite, before it does it.** A confirmation dialog lists every selected item with the registry keys, folders and commands its restore touches, states where the snapshot will go, and carries the per-item warnings that were previously shown only while browsing the tree — never at the moment they applied. The dialog defaults to Cancel, so a stray Enter or Escape does nothing.
- **The apps that own the files being overwritten are closed first, with your consent.** Restoring a browser profile while the browser is running was previously a straight overwrite of files it holds open. The confirmation dialog now asks once per application; declining skips that item rather than overwriting its live profile, and an application that refuses to close fails the item instead of writing into it. There is deliberately no "restore over the running app anyway" option. The Start menu process, which restarts itself within seconds, is closed automatically at the moment its item is restored rather than being offered as a choice.
- **Nothing is closed for an item the backup has nothing for.** What you tick in the list is independent of what the backup folder actually contains, so it is possible to select Chrome and restore from a backup that never included it. Appcopier now checks before closing anything: that item is reported as having nothing backed up, and your browser is left alone. Previously it would have been force-closed — losing every open tab — and a copy of your live profile written into the snapshot, before the restore reported it had nothing to do.
- **The snapshot says what it did not capture, by name.** An item the snapshot had nothing to save for is an item the restore will overwrite with no fallback, so it is listed rather than passed over — and a run that captured four of five items no longer reports the same result as one that captured all five. Taking a snapshot no longer interrupts to ask whether to close a browser, either: you were asked once, before anything started, and asking again from a background step produced a dialog that could hide behind the app and, if dismissed, left that item out of the snapshot while the restore went ahead with it.
- **Restores write a `restore_log.txt`.** It records what was restored, from where, which items succeeded, and whether a usable snapshot exists — and it is written into the snapshot folder, next to the thing that undoes the restore. There was previously no record of what a restore had changed. Selecting a backup in the restore list now shows this log alongside `backup_log.txt`.

### Fixed — restore safety

- **Registry imports are now checked by reading the key back.** After `regedit /s` reports success, the key is probed. If it is absent, the import is reported as failed — exit code 0 from `regedit` never proved otherwise. If the key cannot be probed at all, the import is still reported as succeeded while saying it could not be confirmed: an unelevated probe of a machine-wide key that `regedit` just wrote under elevation is missing evidence, not evidence of failure, and failing there would cry wolf on imports that worked. The wording stays "applied" rather than "verified", because the key existing does not prove its values match the backup.
- **"Restart File Explorer" no longer opens a window per File Explorer you had open.** It killed *every* `explorer.exe` — the shell and each open folder window — and started a new shell once per process killed, so three open windows produced three shells. It now closes the shell once, starts at most one, and starts none at all when Windows has already restarted it by itself. It also reports what happened: the button previously hid itself unconditionally, removing the user's only way to retry at exactly the moment the retry was needed.
- **A restore can no longer be started while one is already running.** The window is disabled for the duration. This was reachable before and is more so now that a restore takes a snapshot first.
- **A restore reports its outcomes against the items it actually walked.** The summary and `restore_log.txt` paired results positionally against the list of ticked items, while the results themselves were produced from the evaluated restore scope — two lists kept equal only by the callers happening to filter the same way. Nothing could reach that divergence today, but if anything ever had, every outcome after the dropped item would have been attributed to the wrong one: the silent misattribution this phase exists to remove, in the log that records it. The pairing is now structural.
- **A module declaring an empty close requirement is skipped rather than mis-reported.** Two of the three places that read a module's list of processes-to-close treated a null entry as a supported degenerate case and passed over it; the restore loop did not, and dereferenced it. The resulting exception was caught, so the item was not lost — it was reported as an "unhandled error" after having been snapshotted and after its application had been closed, which is a less honest answer than the crash would have been.
- **The QR-code prompt can no longer appear behind the main window.** Its timer had no synchronizing object, so the handler ran on a thread-pool thread and the dialog it raised had no owner: it could paint behind the app while the app stayed clickable, so the user saw nothing happen and clicked again, stacking up hidden dialogs. The timer is now marshalled to the UI thread, and it is stopped and disposed when the form closes instead of being left to fire against a disposed window.

### Changed — backup and restore now report what actually happened

- **Appcopier can tell you when a backup or restore fails.** Previously it could not — not "reported it badly", *could not*: `BackupBase.Backup(string)` returned `void`, `Utils` caught every exception and only logged it, and the app showed "Back up done." unconditionally, outside any check, even when every selected module had thrown. Backup and restore modules now return a result — succeeded, skipped, or failed, each with a human-readable reason — which the app aggregates into a summary that reflects the run.
- **The summary distinguishes four outcomes instead of one.** Something failed; everything worked; nothing was present to back up; or the run never happened at all. That last case replaces a silent no-op: pointing a restore at a folder that had been deleted previously ran zero modules and then announced "Restore done."
- **"Nothing was present to back up" is not reported as a failure.** Real machines legitimately lack keys — a touchpad key on a desktop, a Group Policy key on a Home install, an accent-colour key that only some profiles have. Those are reported as skipped, with the absent item named. Treating them as errors would make the summary noise, which is the same problem in the opposite direction.
- **The summary names the modules it is talking about.** Each line leads with the item's title, so "regedit exited with code 1" is attached to the setting that produced it rather than floating free in a list the user cannot map back to what they ticked. Success lines carry the module's own count — "copied 1204 file(s)", "exported 19 Wi-Fi profile(s)" — instead of restating its name.
- **Failing to create the backup folder is reported, not fatal.** It was an unguarded `Directory.CreateDirectory` inside a handler that had already disabled the window, so an ordinary failure — the exe under `Program Files` on a standard-user account, a full disk, an over-long path — left the main window permanently unresponsive. It is now reported as a run that did not happen, naming the cause.
- **`backup_log.txt` records outcomes instead of selections.** It previously listed what you had ticked, which describes an intention rather than a result. Each line now carries the module, its outcome, and the reason.

### Fixed — silent failures

- **Registry exports are verified rather than assumed.** `regedit.exe` returns exit code 0 when asked to export a key that does not exist, and writes no file at all — measured on Windows 11, 20 July 2026. The exit code alone was therefore never evidence of anything. Exports now check the exit code *and* that the file exists, is non-empty, and carries a valid `.reg` header, and the target path is cleared first so a stale file cannot satisfy the check for an export that wrote nothing.
- **A failed export no longer leaves a landmine for the next restore.** When an export is abandoned — regedit timed out, exited non-zero, or its outcome could not be determined — whatever it had written so far is deleted. Validation is header-only by design, so a truncated file with an intact header would otherwise pass the import pre-flight, reach `regedit /s`, exit 0 and be reported as applied: the user would be told the backup failed and then told the restore of that same known-bad file worked.
- **A `.reg` file that cannot be trusted no longer reaches the registry.** Imports validate the file before invoking `regedit /s`, and refuse anything missing, empty, not carrying a valid `.reg` header, or unreadable. A file that exists but cannot be read is reported as unreadable rather than as corrupt — those have different causes and different fixes, and claiming a locked file is invalid asserts something about contents that were never seen.
- **Restores are reported as *applied*, never *verified*.** `regedit /s` returns 0 on files it only partially applied, so having run it successfully is the strongest claim available without reading the keys back. That read-back is deliberately left to a later phase rather than being implied here.
- **Declining to close a browser is no longer reported as a successful backup.** Answering "no" to "close Chrome before backup?" previously returned without copying anything and still produced "Back up done." It is now reported as skipped, naming the choice.
- **Agreeing to close a browser now actually works.** The app killed the browser and began copying immediately, while the browser was still flushing and holding its files — so the copy hit locked files either way. It now waits, bounded, for the process tree to exit before copying.
- **Wi-Fi restore could never restore anything.** `netsh` writes exported profiles as `<network adapter name>-<SSID>.xml`, but the restore looked for `WLAN*.xml` — on the machine this was measured on, that matched 0 of 19 exported profiles. Because the prefix is the adapter's name, which differs per machine and is localised, a corrected wildcard would not have fixed it either; profiles are now identified by their contents. Restore also imported only the first file it found, discarding every other saved network.
- **Killing a process could take down the whole run.** `Process.Kill()` was unguarded, and it throws when a process exits between being listed and being killed — routine for a browser's process tree. Reached from a UI event handler, that was an unhandled exception that ended the run and discarded every result gathered so far.
- **Folder copies report what they actually copied.** A missing source folder, a file that could not be copied, and a clean copy previously all completed identically. **Note that the three browser modules will now report failure whenever the browser was running**, because they copy live profile databases that the browser holds open. That is the intended signal rather than a regression — `docs/ROADMAP.md` already records these modules as blunt whole-directory copies to be fixed or retired — but it is a visible behaviour change.
- **The winget backup no longer reports failure for a backup that worked.** Appcopier ran winget *through* Windows Terminal and waited for `wt.exe` — but `wt.exe` forwards the command and exits immediately, so the wait, and the exit code it produced, belonged to a process that had done nothing but pass along an argument. Measured on a real backup, 20 July 2026: the app recorded "winget reported success but wrote no file" at 07:35:54, and winget wrote a complete, valid 113-package export to that same path at 07:36:23 — 29 seconds after the app had already declared it missing. winget is now run directly, so the wait and the exit code describe the process doing the work. This was the opposite failure to the rest of this work — crying wolf rather than staying silent — and it was invisible from the reporting layer, which was answering correctly about a file that was still being written.
- **The app-restore dialog reports packages it could not install.** It discarded the result of every winget run, so with winget missing it finished instantly having installed nothing and said nothing — a dialog that closed as though everything had worked. Each package's outcome is now inspected and the failures are named. Note that installing an app you already have **upgrades** it to the current version rather than returning it to the backed-up one; the dialog now says so instead of implying a faithful restore.
- **A second backup in the same session no longer leaves the first one's files behind.** Backups taken during one run of the app share a folder, so clicking Backup twice writes to the same paths. If a registry key existed on the first click and had been removed by the second, the run reported the item as skipped and left the earlier `.reg` file sitting there — and a later restore would import registry state the user had been told was not captured. The same held for the winget export: a stale but valid file passed every check, so an outdated package list was reported as a fresh one. Both targets are now cleared before the work starts, not merely before it is verified.
- **Restore no longer describes the wrong machine.** When a folder restore found nothing to restore, it said the item was "not present on this system" — but on restore the missing thing is the *backup*, and the live machine was never examined. Those cases now say nothing was backed up for the item.
- **A backup can no longer hang forever on Windows Terminal.** The winget export waits for `wt.exe`, and that wait had no bound — so a terminal left open, a winget prompt nobody answered, or a stalled download froze the app with its window already disabled and Task Manager the only way out. The wait is now bounded and reported as a failure to finish. The limit is deliberately generous (10 minutes) rather than the 60 seconds used for `regedit`: the terminal window is visible, winget can legitimately spend minutes updating sources or downloading, and killing a slow-but-working export would be a false failure report — the exact thing this work exists to remove.
- **The app-restore dialog opens on the thread that owns the window.** Modules run on a thread-pool thread, which is MTA, and Windows Forms requires STA. The winget restore is the one module that opens a window, so it was starting a second message loop on an apartment-incorrect thread: the dialog has no owner and can paint behind the main window — the user sees nothing happen and clicks again — and its COM-backed parts are unreliable. That module now runs on the UI thread.
- **A failed log file no longer orphans `netsh`.** `ExecuteNetshCommand` opened its output file *after* starting `netsh`, so a locked file, a missing directory or a denied path threw with `netsh` already running and nobody draining its standard output. The pipe fills, `netsh` blocks writing to it, and the process outlives the handle that was supposed to own it. The file is opened first, while nothing has been started yet, so the failure is just a failed backup.
- **Log lines carrying file paths are no longer silently discarded.** `LogHelper.Log` treats its first argument as a format string, so a message containing `{` threw inside the logger and was routed to `Console.WriteLine`, which goes nowhere in a WinForms app. Result reasons contain registry paths and exception text, so they hit this constantly. `LogHelper.LogMessage` passes such text as an argument instead.

### Security
- **Web links no longer open the browser with administrator rights.** Appcopier's manifest requests `highestAvailable` because registry export needs elevation, and `ShellExecute` passes the parent's elevated token to whatever it launches — so every link in the app was opening the user's browser as Administrator. That leaves admin-owned files behind in the browser profile, which can stop it starting normally afterwards, and it silently grants administrator rights to anything downloaded and run from that window, with no prompt. Links are now handed to `explorer.exe`, which forwards them to the already-running shell at the user's normal privilege level. This behavior predates the .NET 8 migration; consolidating the five call sites into one helper is what made it visible.
- `Utils.OpenUrl` accepts only absolute `http`/`https` URLs and logs anything else without launching it. With a shell launch, a string that is not a web URL is not a bad argument — it is a file or program that gets executed, at whatever privilege level the app is running with. Every current caller passes a compile-time constant, so nothing reaches this from user input; the check is there to keep that true as callers are added, and is covered by tests.

### Changed
- **Migrated the app from .NET Framework 4.8 to .NET 8** (`net8.0-windows`). The project file is now SDK-style, so the build is `dotnet build src\Appcopier.sln` instead of `nuget restore` + `msbuild` from a Visual Studio Developer environment. Build output also moves to `bin\<Configuration>\net8.0-windows\`. Releases now ship **self-contained**, so the runtime is bundled into the executable and users still download a single `.exe` and run it with nothing to install — the download grows from roughly 1 MB to roughly 69 MB in exchange for keeping that no-install experience.
- Newtonsoft.Json is now referenced as a `<PackageReference>`; `packages.config` and the checked-in `HintPath` to a `lib\net45` assembly are gone. `App.config` was removed — it only declared a `<supportedRuntime>` for .NET Framework, which is meaningless on .NET 8.
- Backup paths are now composed with `Path.Combine` rather than string concatenation. On .NET 5+ `Application.StartupPath` gained a trailing separator, which would otherwise have produced doubled (and in one case tripled) separators in every `regedit` command line, in `backup_log.txt`, and in the on-screen log. The on-disk layout is unchanged: backups still go to `<exe dir>\app\<yyyy-MM-dd - HH.mm>\`.
- The update checker's version parsing moved out of `CheckForUpdates` into `Data.ParseLatestVersion(string)`. `CheckForUpdates` performs network I/O and shows message boxes, so the parse could not be tested in place; the extracted method is byte-for-byte the original logic, quirks included, and is now covered by tests. No behavior changed.
- The in-app version is now read from `[assembly: AssemblyFileVersion]` by reflection instead of `Application.ProductVersion`. This is the exact attribute the deployed update checker parses out of `AssemblyInfo.cs`, so the local and remote sides of the update comparison can no longer drift apart — previously the correct value depended on no one ever adding an `AssemblyInformationalVersion` attribute, which would have appended a `+<git-sha>` suffix and broken the update check for every installed copy.

### Fixed
- **The QR-code prompt can no longer terminate the app.** It runs on a `System.Timers.Timer` thread, and .NET Framework silently swallowed exceptions thrown from `Elapsed` handlers where .NET 8 does not — so anything escaping that handler now takes the whole process down. Both the prompt and the link launch are contained, including the failure reporting itself, which can fail on exactly the locked-down machines the handler exists for.
- **The update check no longer offers a phantom update when the two version strings are formatted differently.** The local version was normalized to three parts but the version scraped from the remote `AssemblyInfo.cs` was compared raw, so publishing a four-part or suffixed version upstream would have made the comparison fail permanently — telling every user on the current build that an update was available, on every check, forever. Both sides now go through the same normalization.
- **A malformed update file is reported instead of being treated as a new version.** When the download succeeded but contained no readable `AssemblyFileVersion`, the parse returned an empty string, which compared unequal to the current version and offered a download for a release that does not exist.
- `Program.GetCurrentVersionTostring` cannot throw during startup. It ran before any UI existed, so a missing or malformed version attribute produced a crash with nothing to report it — including for `"1.2"`, which parses as a `Version` but throws when formatted to three parts. Unusable values now pass through unchanged rather than being replaced by a plausible-looking `0.0.0`, which would read as a real installed version and misdirect anyone investigating the resulting update prompts.
- **Opening any web link no longer crashes the app on .NET 8.** `Process.Start` no longer launches URLs through the shell by default, so all five link-opening call sites now request it explicitly. Without this, clicking through the QR-code prompt on the main window terminated the process outright (it ran on a timer thread with no error handling), every link on the About page raised an unhandled-exception dialog, and the "download update" link failed with a misleading "Checking for App updates failed" message *after* the user had already agreed to download.

### Added
- An xUnit test project at `src/Appcopier.Tests`, runnable with `dotnet test src\Appcopier.sln` — the project's first automated tests. They cover the update-checker version handling on both sides of its comparison, the URL guard that decides what `Utils.OpenUrl` will hand to the Windows shell, and — added with the honest-reporting work — the outcome model itself: how per-operation results are classified and folded, `.reg` artifact validation against real byte-level fixtures, folder-copy tallies against temp directories including locked files, and Wi-Fi profile identification. Coverage stops where elevation begins: nothing in the suite launches `regedit.exe` or `netsh`, so what those tools actually return for a denied key or a partially-applied file is asserted nowhere and must be checked by hand.
- The tests run against the **real** `src/Appcopier/Properties/AssemblyInfo.cs`, which the build copies into the test output, rather than against a hand-copied literal that could silently drift from the file the deployed update checker actually downloads.
- Claude Code project automation under `.claude/`, now tracked in the repository: hooks that block edits to generated `bin/`/`obj/` artifacts and run a `dotnet build` compile check after every C# edit, a `windows-safety-reviewer` subagent for auditing destructive Windows operations (registry imports, process kills, restore overwrites), and two skills — `new-backup-module` (scaffolds a `Conf/` module and registers it) and `/release` (guided version-bump/publish/tag/release flow). Only `.claude/settings.local.json` stays ignored, since it holds per-user paths.
- `CLAUDE.md` with build instructions and an architecture overview; this `CHANGELOG.md`.
- `docs/superpowers/specs/2026-07-20-net8-migration-design.md`, the design record for the .NET 8 migration and the phased roadmap that follows it.
- `docs/superpowers/specs/2026-07-20-phase2a-honest-failures-design.md`, the design record for Phase 2a — threading a `ModuleResult` through the backup/restore chain so the app can report failure at all. No code changes yet; the spec records the decisions, the measured `regedit`/`netsh` behaviour they rest on, and what was deliberately deferred. `docs/ROADMAP.md` is restructured to match: Phase 2 splits into 2a (honest reporting), 2b (restore safety) and 2c (module bugs), and modernization moves out to its own Phase 4.
- `src/NuGet.config` declaring nuget.org as a package source, so restore works on machines whose user-level NuGet configuration has no sources.

### Removed
- `src/Appcopier/bin/` and `src/Appcopier/obj/` are no longer tracked in git; a root `.gitignore` now covers build outputs, `src/packages/`, and Visual Studio user files. Build artifacts previously produced noise in every diff.

## [0.30.0]

Latest released version at the time this changelog was introduced; see [GitHub releases](https://github.com/builtbybel/Appcopier/releases) for prior history.
