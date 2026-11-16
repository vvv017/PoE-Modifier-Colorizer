# Source publication snapshot

Date: 2026-11-15, America/Los_Angeles. Application version: 0.1.1.

## Current status

This is an experimental Windows Forms application for English PoE 1 modifiers.
Compilation and local catalog import succeeded. Desktop usability is not accepted:
the user still reported the already-open dialog after the startup change, and the
last desktop capture showed the main window marked Not Responding. Computer Use
was then stopped by the user. The cause of that unresponsive state is unresolved.

An earlier tool-launched instance held the single-instance mutex without appearing
in the user's desktop window inventory. It was closed, and launching through the
desktop created a visible main window. This observation does not prove normal UI
interaction, minimize/restore, or repeated launches on the user's desktop.

## Implemented behavior

- The catalog follows local altar choices through Mods and Stats to English CSD
  descriptions, including player penalties and minion rewards and downsides.
- Altar filters combine faction, target and effect on the same local use. Shared
  map/altar descriptions keep one write key. Per-context PoEDB results are retained;
  the list and preview show the least verified status for each description.
- Color rules, catalog, comparisons and the undo journal are local JSON files.
  Selecting a color saves a rule; applying it is a separate GGPK write operation.
- Duplicate startup attempts locate the existing window in the same session,
  request restoration and foreground activation, or flash its taskbar button.
  The original mutex still prevents two instances from editing concurrently.

## Verification observed locally

- `dotnet build GgpkColorizer.csproj -c Release --no-restore`: 0 warnings and
  0 errors before and after the final code change.
- Framework-dependent `dotnet publish` succeeded.
- Read-only import resolved 232 of 233 local altar choices to 199 text variants.
  All twelve faction/target/effect combinations had descriptions. The catalog
  contained 2,345 variants and retained all 2,214 previous keys.
- PoEDB comparison completed. Text candidates do not prove matching website Mod
  IDs or numeric ranges. Rows with missing faction data are excluded and reported.
- A background process check observed a second launch exiting with code 0 while
  the first process remained. This did not establish visible desktop behavior.
- Hash comparisons showed that local app data was unchanged by the final startup
  update and desktop relaunch.

Evidence comes from local build output, import reports, process observations,
data-file hash comparisons and the final desktop capture. Private diagnostic
files and extracted game data are intentionally excluded from this repository.
No CI workflow or hosted CI run is included in this snapshot.

## Remaining work and limits

Resolve the reported desktop startup and responsiveness behavior, then verify
ordinary user interaction. The current capture is a failure observation, not a
successful UI acceptance result.

One legacy Grasping Vines altar choice has no matching English CSD description in
the inspected installation and is reported as unresolved. Catalogs may contain
historical, hidden and Ruthless records. Future DAT schema changes stop import
rather than replacing the previous catalog with guessed data.

No test suite, new GGPK write, undo, failure injection or in-game color acceptance
was performed for the final startup change. Shared CSD text may affect other game
contexts. Per-item undo cannot recover every interrupted archive write.

## Build and publication contents

Build commands and dependency versions are in README.md and packages.lock.json.
The repository contains source, documentation and license notices. It excludes
GGPK files, extracted game tables/text, codec and dependency binaries, local JSON
data, build output and diagnostic captures. Obtain a compatible x64 codec
separately before importing or editing a GGPK.
