# V1.13.3 · Reference sources and evidence database

## Distribution / remote edits

- `SourcesConfig.json` is the portable JSON fallback and an embedded single-EXE bootstrap.
- The app tries `https://kendoyae.github.io/GuiGui-MangaAuthorSorter/data/sources.json` first on each explicit check, then `SourceConfigCache.json`, portable `SourcesConfig.json`, and finally the embedded JSON. **To enable network-maintained changes, publish the `SourcesConfig.json` contents to that GitHub Pages path**. This release does not publish it by itself.
- `type=github-monthly-csv` is currently the only implemented parser; new repositories sharing the CSV schema can be added remotely. Unsupported types intentionally fail validation instead of being executed.
- `refreshHours` is the auto-check TTL, capped by per-source `checkIntervalHours`. The Check button always checks immediately.
- GitHub trees are retrieved from `GET /repos/{owner}/{repo}/git/trees/{branch}?recursive=1`; truncated responses fail closed. Only changed CSV blobs download. Each raw download is verified against size and Git blob SHA-1.

## Initial baseline

The distribution ships a public `GuiGuiAuthorIndex.db` that is maintained separately. On the **first reference check**, older upstream CSV files are marked `baseline-only`, *not* `imported`, and only the most recent month per source is scheduled for download. This intentionally avoids hundreds of megabytes of redundant downloads but does **not** assert complete historical work evidence. If an older CSV blob later changes, it will be downloaded and recorded. A future full-history import command can be added separately.

## SQLite reference evidence

`GuiGuiReferenceEvidence.db` is user-owned and separate from read-only `GuiGuiAuthorIndex.db`. It contains `SourceFile`, `WorkEvidence`, and `EntityEvidence`. Work-level keys use source/galleries, with source file and blob SHA recorded. Each changed CSV file replaces its previous rows atomically; withdrawn upstream CSV files remove their source-only records. Local and public entity name matching uses the normal `AuthorEntityStore.LoadIndex()` / `AuthorEntityIndex.Resolve(name, type)` implementation, including user overrides. Matching is **corroboration**, not provider-ID verification; ambiguity is retained. Original gallery title, raw artists and groups remain traceable. Reusing the same work must not artificially boost evidence confidence.

## Limitations and checks

- Titles and tags are not promoted to author aliases automatically; multiple artists in one gallery are not equated.
- The stored match statuses are a snapshot of the author index at import time; existing source files are not re-validated merely because user aliases later change. The `Revalidate` button recalculates local evidence statuses using the latest unified author index, without downloading CSVs.
- Only SHA-changed CSV evidence is processed; upstream undated historical records that exist only in the complete split SQLite are outside this CSV workflow.
- Unit-level code, language schema and static structure validation are possible here; the Windows `.NET Framework 4.8` build, `winsqlite3` runtime and live GitHub downloads must be tested on Windows.
- License for the referenced source repository: GPL-3.0. Review downstream distribution obligations for any redistributed derivative datasets.
