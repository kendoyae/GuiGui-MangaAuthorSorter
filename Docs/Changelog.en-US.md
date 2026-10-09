## V1.13.10 — Public index merge performance and progress
- Ordinary public-index updates install the complete index directly without creating, reading, or accumulating a work-evidence database; evidence remains exclusive to advanced source builds.
- Add missing indexes and replace quadratic cross-source conflict subqueries for large tag imports.
- Add Rebuild Merge action using already-imported evidence; no repeat download or filtering.
- Support SQLite pause/resume/cancel with elapsed time shown during merge.
- Preserve the existing Schema v4 and user data. Windows build and large-source testing pending.

## V1.13.9 — Advanced source download API compilation fix
- Add the missing optional pause-control argument to the advanced source downloader, fixing CS0103 and CS1501.
- Keep pause, resume and cancel behavior. Full Windows compilation is still pending.

## V1.13.8 — Compiler source paths
- Fix CS1504 for five advanced-builder files by using Windows backslash paths in CompilerSources.rsp.
- Verify every compiler input and its agreement with the project before building.
- No functional changes from V1.13.7; Windows compilation remains to be verified.

## V1.13.7 — Three advanced builder sources
Native WinForms source cards, EH current/gallery and tag-aggregate import, CSV incrementals, explicit Schema v4 fresh base; Windows testing pending.

## V1.13.6 — Official Index Distribution and Full Build

- Official Schema v4 index manifest, verified download and local import.
- Advanced full-history CSV build, including a missing-index bootstrap.
- Stage the new official baseline and replay local evidence at cold startup.
- No .NET 8/WPF runtime dependency; developer must first publish the manifest.

## V1.13.5 — Incremental public author/group index merge

- Monthly CSV imports share name filters with AuthorDbBuilder and stage an updated public index.
- Rebuild from an official baseline makes local CSV withdrawals reversible; restart activates the staged file.
- User-confirmed entities are preserved. Ambiguous identities are not silently merged.

## V1.13.4 — Startup crash and language validation fix

- Fix startup crash caused by a duplicate key in the built-in English language dictionary.
- Validate unique Chinese, English and German built-in keys against the official language files before compiling.

## V1.13.3 — GitHub metadata evidence

- Renamed entity details button; removed nHentai API v2 online lookup.
- Configurable GitHub CSV SHA incremental download, storing gallery-level corroboration in a separate SQLite file.
- No automatic identity merge from gallery text; the public author index remains read-only.

## V1.13.2 — Author library interaction fixes

- Place Previous Page on the left and Next Page on the right in public-data paging.
- Fix invalid selected-index errors when opening the author–circle relationship window.

## V1.13.1 — Unified author library interface

- Search and manage authors and circles together; see aliases, sources and relationships in one details view.
- Simplify the library into three main pages: Authors & Circles, Conflicts & Review, and Data Maintenance.
- Keep the public database read-only; save personal edits as user overrides.
- Add public-data pagination and move folder naming rules to Archive Settings.

## V1.13.0 — Filename structure and activity prefixes

- Parse identities after unknown parenthesized prefixes followed by [circle (artist)]; exclude suspected activities from scoring and increase explicit artist-role weight.
- Discover scan-local prefixes from distinct circle/artist pairs without permanent cleaning rules; preserve user-confirmed identities and conflict checks.
- Search titles for files without authors, cache review candidates for offline reuse without learning titles as aliases, explain decisions and invalidate older recognition caches.

## V1.12.13 — Online settings and entity-library window navigation

- Use a single modeless online-settings window owned by home; entity/reference libraries share the home owner, removing hidden modal blockers.
- Reuse existing windows and restore minimized entity libraries; preserve save/cancel behavior and close owned windows with home.
- Add isolated window-navigation regression for home availability, reopen/reuse, persistence, cancellation and shutdown.

## V1.12.12 — Public database Schema v4 compatibility

- Read compatibility views and ArtistGroup relationships without accessing internal Data tables or the builder maintenance database.
- Map DanbooruArtistTag into identities, disabled public overrides and cache dependencies; preserve older schemas with an optional-column fallback. Use restored Source labels and scoped provider ExternalId bindings.
- Add compressed-view fixtures and read-only validation of actual builder output.

## V1.12.11 — Public-override scan stalls and cancellation

- Project public override dependencies from the loaded lookup keys; avoid per-file identity queries while reading/writing plan caches.
- Compile default tag-cleaning rules once instead of repeatedly compiling regexes for each file dependency.
- Cancel plan-cache reads and roll back canceled writes; propagate cancellation instead of swallowing it as a cache failure.
- Add an 8,490-file public-override regression and cache cancellation/rollback checks.

## V1.12.10 — Unified author entities and safe public overrides

- Remove the separate alias library. AuthorEntities.json now manages author/group/alias CRUD, relationships, conflicts, public overrides, import/export and lookup status.
- Keep the public database read-only. Unique stable identities carry overrides across versions; missing/split identities retain edits and require explicit rebinding instead of guessing from row IDs.
- Invalidate recognition and durable plans by file identity dependencies while preserving the source index. Session-only online entities refresh recognition and can be promoted safely.
- Real 8,490-file migration comparison has zero identity/target changes; repeated planning hits all 8,490 cached rows with zero recalculation. Six historical identity conflicts now require confirmation; resulting allocation changes are documented.
- See Docs/UNIFIED_AUTHOR_LIBRARY.md and Docs/AuthorLibraryAcceptance for operations, recovery and measured acceptance. Version: 1.12.10.0.

## V1.12.8 — Fast zero-result scans and incremental plan persistence

- Verify Everything's indexed-directory coverage for a successful empty query, avoiding unnecessary filesystem rescans while retaining fallback for uncertain coverage.
- Skip plan-cache writes on a full cache hit; write only newly recalculated rows and reuse a prepared SQLite statement.
- Prevent stale provider timings and deltas from warmup snapshots from contaminating current-run diagnostics; record plan cache read/write and finalization time.

## V1.12.6 · Performance history display fix

- Fixed newer background warmup status overriding selected historical scan metrics after restarting GuiGui.
- Copy now uses the selected historical performance report, even when a newer warmup fails.
- Clearing performance history also clears the on-disk log; failures preserve the current list and are reported.
- Added a regression contract for persistence and history selection without changing scan or recognition behavior.

## V1.12.6

- Replaced repeated full target-author index rebuilds with incremental registrations during planning.
- Added real-scan phase diagnostics and index-update counters (GL4 format, reads GL3).
- Added regression coverage for incremental vs rebuilt matching semantics. Language schema 75.

## V1.12.5

- Ordinary `GuiGuiAuthorIndex.db` author resolution now builds one read-only in-memory identity index instead of issuing several SQLite queries for every new author; GuiGui warms this index in the background.
- The simulation benchmark now supports a unique-author count plus cold/warm author-index modes for realistic author repetition, cold-start, and steady-state tests.
- Reports now show author-index build time and index size so database preparation can be separated from recognition time.
- Fixed raw language keys appearing in parts of performance diagnostics and simulation progress; the official language schema is now version 74.

## V1.12.4

- Added live simulation stages, progress, current-item, and elapsed-time feedback.
- The simulation benchmark is now an independent modeless window and no longer blocks the main UI or performance diagnostics.

## V1.12.3

- Fixed the simulation window failing to open on some DPI/layout configurations because of an early SplitContainer distance assignment.

## V1.12.2

- Added a fully in-memory simulation benchmark that reads `GuiGuiAuthorIndex.db` only for test data and measures first, cached, and incremental scans without modifying real files or production caches.

## V1.12.1

- Reworked the source index around a persistent FileIndex, incremental deltas, Recognition Cache, and Query/View projections.
- Toggling recursive scanning is now an index projection and no longer rediscoveries or re-recognizes cached files.
- Unchanged files are excluded before parsing and recognition; pure moves preserve recognition facts while true renames invalidate only that file.
- Everything metadata is consumed directly for size and timestamps, avoiding a physical metadata read for every result.
- GuiGui-owned moves and renames now update the durable index, active scan session, and in-memory snapshot immediately.
- Scan diagnostics now report index hits and Added/Modified/Moved/Renamed/Deleted deltas, plan-cache hits, and recalculated files.

## V1.12.0

- Added Chinese, English, and German user guides and release notes that follow the interface language.
- Improved the main filter bar at different window widths.
- Items requiring review no longer block other files; they are clearly reported and skipped during organization.
- Added early target-name conflict detection; affected items appear under both Duplicate and Needs review.
- Improved explanations when files cannot be organized.

## V1.11.30

- Secondary windows no longer show the application icon in their title bars; the main-window icon is unchanged.

## V1.11.29

- Standardized the public brand as “GuiGui” and renamed the executable to `GuiGui.exe`.
- Centered the 64×64 application icon with the information area on the About page.

## V1.11.28

- Moved the quantity explanation into a tooltip to save space in the main window.
- Added path commands and status-specific author actions to the main-list context menu.
- Added Comic content tags and cleaning rules for CSP, Reitaisai, and Ragna Festival event numbers.
- Fixed existing circle folders not matching names joined with a plus sign, such as `Xration+(mil)`.

## V1.11.27

- Added a “0 = all” hint to quantity inputs.
- Column-width changes are now shown immediately while dragging.
- Added cleaning rules for ComiTre and Tora Festival event numbers.

## V1.11.26

- List columns can now be resized independently without squeezing adjacent columns; blank space may remain on the right.
- Release notes now open directly at the version content.

## V1.11.25

- Embedded the user guide, release notes, license, and support QR codes so the standalone EXE remains fully usable.
- Added a complete user guide and separated release history from developer documentation.
- Standardized the lower-left workflow guidance into three numbered steps.
- Added direct author-folder selection from ambiguous and unrecognized results.
- Renamed list “Search” to “Filter” and added multi-keyword matching.
- Improved recognition for legacy names, consecutive tags, untagged names, and event descriptions at the start of a file name.
- Added default cleaning rules for common content tags and dōjin event numbers.

## V1.11.24

- Added update checks for the latest official GitHub release and its notes.
- Improved scan diagnostics, layout, and multilingual display.

## V1.11.23

- Accelerated repeated scans, filter switching, and large-list display.
- Added background scan warm-up to reduce waiting time.
- Improved author and circle recognition for more common name formats.
- Fixed display issues in archive settings, file-type settings, and multilingual UI.

## V1.11.22

- Accelerated large scans and exclusion-rule evaluation.
- Improved Everything and file-system scan efficiency.
- Fixed incomplete English and German text in several settings windows.

## V1.11.21

- Added German as an officially supported interface language.
- Improved multilingual button, label, and list sizing.
- Fixed recognition of author names containing spaces, brackets, or alternate character forms.

## V1.11.20

- Improved multilingual layout throughout the application.
- Fixed clipping and misalignment at different display scaling levels.

## V1.11.19

- Added step-by-step guidance for first-time use.
- Improved the support page and QR-code display.

## V1.11.18

- Redesigned the support page for clearer payment and online-support options.
- Improved layout consistency across settings pages and lists.

## V1.11.15–V1.11.17

- Improved the About, license, community feedback, and support pages.
- Fixed multilingual and display-scaling issues in several windows.

## V1.11.10–V1.11.14

- Items awaiting confirmation no longer block other confirmed files.
- Added a clearer confirmation workflow and pre-execution review.
- Improved duplicate, destination-conflict, and result messages.
- Separated the About and Support entries.

## V1.11.6–V1.11.9

- Added filters for excluded, confirmation-required, and duplicate states.
- Added author assignment, file exclusion, and result viewing to the preview.
- Improved large-list performance, column-width memory, and table operations.
- Reorganized the project and established separate developer documentation.

## V1.11.0–V1.11.5

- Added author entities and a local lookup cache.
- Added tag-cleaning and global scan-exclusion rules.
- Added support for more multi-author, circle, and complex file-name formats.
- Added background execution, progress display, and safe cancellation.

## Earlier versions

- Established author recognition, alias matching, archive preview, file moving, and history.
- Continuously improved file-name compatibility, directory matching, execution safety, and UI operation.
