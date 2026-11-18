# v0.1.2 release handoff

Pre-publication snapshot: 2026-11-17, America/Los_Angeles.
Application version and intended release tag: `0.1.2` / `v0.1.2`.
Base commit: `0b6450c865c81f48ca01c90bf84852fa6f7cd4c5` on `main`.
The publication check fetched `origin` and found no divergence from that base;
GitHub had no existing releases. This snapshot records local evidence before
publication; the release tag identifies the final published source commit.
No repository CI workflow or observed CI run is claimed.

## Behavior

Physical damage reduction and flat armour now have separate altar entries for
decreases and increases. Coloring a player penalty no longer colors the monster
or boss bonus through the same shared description. Negative values use `#|-1`,
zero uses a plain `0` branch, and positive values use an independently configured
`1|#` branch. The UI labels the direction and retains the actual altar targets.

Eight explicitly reviewed stat families use altar-local descriptions: four base
resistances, physical damage reduction, flat armour, flat evasion and critical
multiplier per power charge. The latter two and the four resistances expose
negative values only, preserving plain nonnegative values. Equipment/shared
descriptions are not the editable targets for these rules.

This is not a universal negative-number filter. All 233 local altar choices and
the 29 English entries with player-downside uses were inspected. The four new
families were the remaining numeric catch-all player penalties. Increased flask
charges used, incoming damage and harmful chances retain their positive numeric
conditions. Existing reduced-effect variants retain their negative conditions
and value-formatting handlers. Nonnumeric boolean effects remain unchanged.
The semantic list needs review if a game update adds new mixed-sign altar stats.

English and Traditional Chinese remain independent. Existing resistance keys
and legacy English-only resistance blocks remain compatible. The one-time
Chinese rule inheritance behavior is unchanged.

## Migration and undo

Catalog refresh moves an old broad rule to its negative altar key only. Positive
keys never inherit that saved color. Undo reconciliation still precedes rule
migration, and the catalog is saved before migrated settings.

Applying a scoped rule restores an old shared color only when its exact applied
journal history proves ownership. If a language has no existing local negative
branch, that actually applied shared color is carried into the new negative
branch and included in the same block journal. This also preserves the negative
color when the first selected edit is a positive or other-language row. An
existing local branch wins, and a selected negative color overrides the carry.
Cleared settings are not repopulated merely because a color is still applied.

Changes to one stat share a complete physical block and must be undone newest
first across signs and languages. Rule records include optional language and
condition metadata; old JSON records remain readable. Undoing the first local
block removes that block and aligns its recorded rules; it does not recolor the
restored shared description. This is the existing initial-scope undo behavior.

Production preparation reparses generated blocks and checks unique rule keys,
language, numeric conditions and resulting text. Existing exact block matching,
encoding/format preservation and byte-reversibility guards remain in place.

## Observed verification

- Project-local .NET 8 Release compiler baseline, final build, and publication
  build: 0 warnings, 0 errors. Framework-dependent publish completed for
  Windows x64. A self-contained Windows x64 single-file publish also completed;
  its .NET runtime is included and Oodle remains a separate DLL beside the EXE.
- Read-only import using the new assembly succeeded against the current GGPK:
  2,347 English entries and 2,316 Traditional Chinese entries. Both languages
  still resolve 232 of 233 choices; the existing Grasping Vines gap is unchanged.
- All 22 saved colors are retained. One English broad physical-reduction red
  rule moved to the negative altar key. The four positive physical-reduction /
  armour rows across both languages have no saved colors.
- The program/catalog update preserved all 30 prior history records. The user
  subsequently authorized one real apply operation, which added two records and
  marked the original broad physical-reduction change undone. There are now 32
  records and no prepared, unknown or conflict entries.
- The four current altar DAT resources match the earlier inspected resources by
  SHA-256. The three current CSD resources were also extracted read-only.
- The installed assembly matches the published assembly and reports 0.1.2.
- With the game, launcher and editor closed, one call to the installed
  `AppController.ApplyAsync` applied the 22 configured rules. It reported two
  applied changes, 17 unchanged skips after block grouping, and no notices.
  The actual writes restored the shared English physical-reduction text and
  inserted the dedicated altar block.
- An independent readback of the three resources matched all 22 saved colors.
  English physical reduction is red only for `#|-1`; `0` and `1|#` are plain.
  Traditional Chinese physical reduction remains uncolored as configured, and
  both shared physical-reduction descriptions are plain.
- Readback resource hashes match the journal. Reversing the two recorded edits
  in memory reconstructs the exact pre-apply resources, proving no unrecorded
  resource bytes changed. No actual undo was performed. Settings are byte-identical
  to the pre-apply snapshot; archive path, codec preference and languages remain
  unchanged.
- Combined source review and whitespace checks passed. The locked dependency
  file is unchanged; no production dependency was added.

Local evidence is under `work/checks/signed-values/`: `baseline.txt`,
`final-build.txt`, `publish.txt`, `installed-import.txt`,
`preview-preservation.json`, `installed-preservation.json`, and archive metadata
snapshots. Pre-update program files and the complete local data folder are also
retained there. The effect-by-effect audit and plan are in the workspace outputs.
The authorized apply evidence is in `apply-2026-11-17-01/`, including
`selection.json`, `apply-result.json`, `new-records.json`,
`readback-verification.json`, and before/after resources. Game resources and
private JSON are excluded from the source distribution.

## Acceptance boundaries and next action

The program and local catalog/settings are updated in place, and the configured
rules have now been applied once to the GGPK. The game can be reopened for visual
verification; another apply is not needed for this batch.

No test suite, UI automation or browser automation was run. Real GGPK undo,
interrupted-write recovery and in-game rendering remain NOT RUN. The successful
write and exact readback do not prove game-engine override precedence or visual
acceptance. Per-change journals are not a complete GGPK backup.

## Release artifacts

`PoE-Modifier-Colorizer-0.1.2-win-x64-complete.zip` is a clean distribution
containing the self-contained EXE, the existing `oo2core.dll`, matching source,
quick-start instructions and third-party notices. It excludes local settings,
catalogs, comparison results, undo history and game resources. The ZIP entries
and EXE/DLL/source hashes were checked after creation. The separate EXE asset
still needs `oo2core.dll` beside it. The combined package has not been launched
or verified in-game; it is prepared as a prerelease.

The Oodle binary contains version string `2.9.10` and matches the DLL from the
LibGGPK3 v2.7.5 Windows x64 tool archive. Oodle is not covered by this project's
AGPL license. Its redistribution permission and compatibility with the AGPL
components remain unconfirmed. `licenses/Oodle-NOTICE.txt` records its origin
and status.

The older unversioned `PoE-Modifier-Colorizer.zip` is the local installation
archive and contains user data; it is not the release asset. Preserve the whole
generated `data` directory when moving an existing installation.
