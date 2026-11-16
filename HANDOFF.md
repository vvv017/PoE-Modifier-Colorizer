# Traditional Chinese game text handoff

Snapshot: 2026-11-15, America/Los_Angeles. Application version: 0.1.1.
Base commit: `a01d7e960aa1c99298158fe528498bc2adda4ca6` on `main`.
Source publication target: `main` at
https://github.com/vvv017/PoE-Modifier-Colorizer, as requested by the user.
No hosted deployment or CI run was performed for this update.

## Delivered behavior

The editor reads English and the exact `Traditional Chinese` language blocks
from the game's own CSD resources. Game text and interface language are separate
selectors. Both languages can be searched and colored independently. Apply all
covers configured entries in both languages, regardless of the current filter.
English keys remain unchanged; Traditional Chinese keys include a language
namespace. Existing JSON without a language field continues to mean English.

On the first bilingual import, saved colors are copied only where resource path,
ordered Stat IDs and numeric conditions identify exactly one entry in each
language. An early legacy key can be used only when its own applied journal
record identifies one current English entry by exact live text. Original rules
are retained. The one-time completion flag prevents later imports from restoring
Chinese colors the user deliberately changed or cleared.

Four base resistance rules in each language target only the altar file's negative
branch (`#|-1`). Nonnegative values (`0|#`) retain the original text. The existing
English-only altar blocks are accepted and upgraded on apply. One language's
color does not overwrite the other. Shared equipment resistance descriptions
are excluded as editable targets. Other shared modifiers retain their existing
cross-context behavior.

Simultaneous English and Chinese edits of the same altar resistance become one
whole-block journal entry with both rule states. Undo ordering uses the physical
block, preventing an older English undo from overwriting a newer Chinese edit.
The journal preserves legacy records and exact before/after blocks. Undoing a
Chinese upgrade restores the previous English-only block. Ordinary text changes
remain language-specific. Pending writes reconcile against their recorded text
or complete block before changing statuses or saved rules.

PoEDB comparison continues to check English pages only. Chinese entries are
explicitly unchecked; English results are not presented as Chinese verification.

## Observed verification

- Application Release compiler baseline and final build: 0 warnings, 0 errors.
- Catalog: 2,345 English entries and 2,314 Traditional Chinese entries. All 2,345
  prior English keys remain. Each language resolves 232 of 233 local altar
  choices; the pre-existing Grasping Vines gap remains.
- Current settings: all 10 existing saved colors are retained and 10 independently
  editable Traditional Chinese colors were added, for 20 rules total.
- All 18 pre-existing history records remain. `changes.json`, its backup, and
  `comparison.json` are byte-identical to their update-time snapshots. Archive
  path, codec preference and interface language remain unchanged.
- Actual edit preparation was exercised against the live game's three resources
  without writing them. Proposed Chinese changes preserve every English entry,
  all other language sections and the four complete shared resistance blocks.
  Both languages' altar nonnegative resistance templates remain plain.
- Reversing the prepared edits reconstructs all three complete resources exactly.
  Preparing different English and Chinese colors for one resistance produces one
  block with two independently recorded rules and the intended separate colors.
- A local JSON recovery check confirmed a recovered legacy undo is synchronized
  before migration and cannot resurrect a scoped color. The catalog is made
  durable before the one-time Traditional Chinese settings marker is saved.
- Source review and `git diff --check` passed. The scratch runner built using the
  existing dependency cache; it retains a NU1900 vulnerability-feed TLS warning.
  The application build itself has no warnings.

Evidence is local under `work/checks/traditional-chinese/`: `baseline.txt`,
`final-build.txt`, `installed-prepare.txt`, `installed-preflight/preflight.json`,
`final-prepare.txt`, `preservation.json`, and archive metadata snapshots. Prepared resource bytes and
local JSON recovery copies remain outside the source distribution.

## Not applied and acceptance boundaries

The user chose to apply later. This update performs no GGPK writes. The game
remained running while the editor was closed for an in-place program and local
catalog/settings update. GGPK size and last-write time were unchanged. The user
stopped desktop automation with Escape, so no further window interaction or
new-version visual acceptance is claimed.

After closing Path of Exile and its launcher, open the updated editor and use
Apply selected for specific Chinese rows, or Apply all configured in both
languages. The new language selector defaults to Traditional Chinese. Importing
or choosing colors does not write game resources.

In-game Chinese rendering, actual GGPK apply/undo, interrupted-write recovery,
and full UI interaction are NOT RUN for this update. No test suite or browser
automation was run. Prepared byte equality is not proof of game-engine override
precedence or every possible interruption recovery. Per-change journals are not
a full GGPK backup; an interrupted archive write may require official repair.

## Distribution

The installed executable and local ZIP are updated in place. Preserve the whole
`data` folder when moving the installed copy; the local executable ZIP contains
that user's settings and history and is not a public source package.
The source folder and source ZIP contain code, docs and licenses only. They
exclude GGPK resources, native codec/dependency binaries, user JSON and diagnostic
captures. Build commands and locked dependency versions remain in README.md and
packages.lock.json. No new dependency was added.
