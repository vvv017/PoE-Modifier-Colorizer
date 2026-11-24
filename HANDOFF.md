# v0.1.3 clear-color fix handoff

Snapshot: 2026-11-24, America/Los_Angeles.
Release preparation base: `270838a60a2698e6824d72751115799585db745c` on `main`.
Assembly version and intended release tag: `0.1.3` / `v0.1.3`.
The pre-publication fetch found no divergence from that base. The release tag
identifies the final published commit; local publication verification is recorded
under `work/checks/release-v0.1.3-20261124/`. No repository CI run is claimed.

## Behavior

"Clear and restore" / "清除並還原" operates on selected modifiers in the GGPK,
including entries whose saved rules were removed by the old button. The game
and launcher must be closed. Setting a color still saves a local rule until
Apply is selected.

Clear checks the current description against the catalog and follows matching
successful journal history back to plain text. It handles repeated recoloring
and recorded undos. Unsupported colors, missing ownership history and changed
text are left intact. Ordinary missing resources/modifiers are reported as
skipped; scoped altar batches retain strict preparation checks. Settings are
cleared only for verified completion.

Clear is a new journaled change, so it removes the current color instead of
restoring an earlier color. Undoing a clear restores the actual pre-clear
archive text. Already-plain text is recorded as a completed no-op, allowing its
saved rule to be cleared without a resource write. Undo does not restore a draft
color that was never applied.

Scoped altar changes retain their complete-block write and undo boundary.
Other languages and value branches are preserved. Legacy shared-color migration
and the associated altar block share a `ClearGroup`. Selecting either record
for undo includes that entire group, but not unrelated clears from the same
button operation. A newer edit or unresolved member blocks the whole group's
undo. A recorded failed forward step remains `not-applied` when an undo confirms
that it already has its original contents.

## Persistence and recovery

`ClearsColor` and optional `ClearGroup` default to false/null in older JSON.
Completed clear/undo records synchronize saved rules after archive readback.
Every member of the same clear group and operation batch must be applied before
that group's settings synchronize. Pending synchronization is retained across
restart; prepared/unknown writes still require archive reconciliation.

Restoring a legacy shared rule also remaps it to the current scoped catalog key.
Carrying an unselected legacy color into an altar branch does not repopulate a
cleared setting. Existing hashes, conflict checks, atomic JSON replacement and
readback are reused. Preserve the complete `data` directory when moving the app.
The journal is not a full GGPK backup. No dependency or database migration was
introduced.

## Observed verification

- Project-local .NET 8 Release compiler baseline and final build both passed
  with zero warnings and zero errors:
  `dotnet build GgpkColorizer.csproj -c Release --no-restore`.
- Framework-dependent local publishing uses
  `dotnet publish GgpkColorizer.csproj -c Release --no-restore --self-contained false`.
  This installation uses its existing Windows Desktop runtime and adjacent
  Oodle DLL; it is separate from the self-contained release ZIP.
- Independent static review covered absent saved rules, ownership history,
  repeated recolors/undos, scoped signs/languages, no-op completion, linked
  legacy recovery, partial results and settings synchronization.
- The running local installation was closed normally, fully backed up, updated
  and reopened. All five app files match the final publish hashes. All five
  existing data files are byte-identical before installation and after startup.
  The new process has a window titled "PoE 詞綴上色器" and reports responding;
  no control interactions or visual acceptance were performed.
- The user subsequently authorized one real English Vulnerability clear through
  the installed assembly's `ClearColorsAsync`: one applied, zero skipped and no
  notices. Independent read-only extraction verified that the shared description
  resource changed only by removing the single red wrapper. Map and altar
  resources and saved settings are byte-identical. The new clear record is
  applied, rules-synchronized and matches both resource hashes.
- No automated suite or UI/browser automation was run. Real undo, scoped altar
  clear, interrupted-write recovery and in-game rendering remain NOT RUN. This
  real clear verifies only the ordinary English entry with an absent saved rule.

Evidence is under `work/checks/clear-color-fix-20261124/`, including compiler,
publish, delivery and startup records. The final compiled files in
`work/clear-color-release-20261124/` are installed at
`outputs/PoE-Modifier-Colorizer/`. The delivery record lists installed hashes and
the preserved-data comparison. Temporary installation/data backups and extracted
game snapshots are excluded from the release; their cleanup status is recorded
in `work/checks/release-v0.1.3-20261124/cleanup.json`. Small verification reports
and the active installation's settings and undo history are retained.

## Current issue evidence and user action

Read-only inspection on 2026-11-24 found that the English
`map_player_has_level_X_vulnerability` setting was absent while the archive
retained `<colour:red>{{Players are Cursed with Vulnerability}}`.
Record `4841209b0e7a4e7087a5d5402b87e190` was applied with no undo.
Evidence is under `work/checks/vulnerability-clear-20261124/`.

The subsequent authorized clear has now removed the wrapper. Its record is
`920557ec024b44f29df3ab8d138540ea`; the resulting shared resource SHA-256 is
`EA521325A1682B922C6C8327F1BADFC40C69C7F1373919EBCC04DF3B4D77DBE0`.
The action ran once through the installed API, not through the UI. The user can
reopen the game for visual confirmation; no further clear is needed for this
verified operation. Operation output, before/after hashes and readback checks are
in `work/checks/vulnerability-clear-apply-20261124-01/`. The full data/resource
snapshots used for those checks are temporary and not part of the release.

## Historical release boundary

Published v0.1.2 source is the base commit above and lacks this clear fix.
Earlier 2026-11-17 evidence
verified an authorized altar-value apply, resource readback and in-memory
byte-exact reversal; it did not verify real undo or game rendering. That evidence
remains under `work/checks/signed-values/` and does not verify this later fix.

The v0.1.3 complete ZIP contains the self-contained Windows x64 EXE,
`oo2core.dll`, source from the release commit and notices. It excludes personal
settings, catalogs, undo history and game resources. Keep the EXE and DLL
adjacent, and preserve an existing installation's complete `data` directory when
upgrading. The older unversioned installation ZIP contains user data and is not
a clean release asset. Package/in-game acceptance remains separate from the
observed local clear and compiler checks.

Oodle 2.9.10 is a proprietary component from the LibGGPK3 v2.7.5 Windows x64 tool
archive. Redistribution permission and AGPL compatibility remain unconfirmed;
see `licenses/Oodle-NOTICE.txt`. No new licensing conclusion was made here.
