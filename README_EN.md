## V1.13.10 Large reference-index merge performance fix

- Index and group large E-Hentai Tag Aggregate merge work to avoid repeated full table scans.
- Display elapsed merge time with indeterminate progress, and enable SQLite pause/resume/cancellation.
- Preserve imported evidence if a merge is interrupted; do not modify public-index schema.
- Requires Windows .NET Framework 4.8 compilation and real-world verification.

## V1.13.9 Advanced source download compilation fix
Fix the missing optional operation-control parameter in `AdvancedSourceService.DownloadAsync` (CS0103, CS1501). The existing pause/cancel behavior is preserved. Still targets .NET Framework 4.8; Windows build verification is pending.

## V1.13.8 — Windows compiler input fix
Correct five advanced-builder source paths in CompilerSources.rsp and validate all 79 sources before calling csc.exe. No functional changes; native Windows compilation is still pending.

## V1.13.7 Advanced builder: three source cards
Native WinForms UI for E-Hentai Current, nh-metadata-archive, and E-Hentai Tag Aggregate. No .NET 8 runtime. Large downloads and optional zstd.exe are kept separate from the distribution. A public index still requires Schema v4; Windows verification is pending.

## V1.13.6 public index maintenance

Official Schema v4 index distribution (requires published manifest), routine SHA-incremental CSV merge, and advanced full-history build with empty-index bootstrap, all without a .NET 8 runtime. See `Docs/PUBLIC_INDEX_RELEASE_V1.13.6.md`.

# GuiGui User Guide

[简体中文](README.md) | [English](README_EN.md)

![GuiGui application icon](Assets/AppIcon256.png)

[Download](https://github.com/kendoyae/GuiGui-MangaAuthorSorter/releases) [https://github.com/kendoyae/GuiGui-MangaAuthorSorter/releases](https://github.com/kendoyae/GuiGui-MangaAuthorSorter/releases)



**Gui Gui, wan with yearning,**  
**follows her love through coming and going;**  
**Gui Gui, faint with longing—**  
**where now shall she seek her beloved?**

If, like me, you enjoy organizing authors into separate folders, GuiGui may be just what you need.

GuiGui is a Windows tool for identifying manga authors and organizing local files. It scans a selected location, identifies authors or circles from file names, and presents an organization preview before making any changes. Its recognition rules are based on the default Japanese titles used for downloads from E-Hentai. Some files may require manual handling. GuiGui contains no download functionality; all operations are limited to organizing local files.

## 1. Quick Start

1. Under **Source Location**, select the folder you want to scan.
2. Under **Destination Location**, select the author archive directory.
3. Configure the scan scope, recursive scanning, and scan limit as needed.
4. Click **Scan Preview**.
5. Review the recognition results, destination directories, and items requiring confirmation.
6. When everything is correct, click **Organize Files**.

GuiGui does not move files during the preview scan. Eligible files are organized only after you confirm the operation.

![GuiGui scan preview](Assets/README/001@en.png)

![GuiGui recognition results](Assets/README/002@en.png)

![GuiGui organization screen](Assets/README/003@en.png)

## 2. Recognition Results

- **Direct match:** The author in the file name exactly matches an existing author directory.
- **Normalized:** The names differ only in safe character, spacing, or bracket variations.
- **Alias match:** The corresponding author was found through aliases in the author entity library.
- **Circle match / Author match:** A circle or author was identified from a structured file name.
- **New author:** No existing directory was found, so a new author directory is planned.
- **Confirmation required:** Multiple candidates exist or there is insufficient information, so manual selection is required.
- **Unrecognized:** The author cannot be identified reliably, so the file will not be organized automatically.
- **Excluded:** The file matches an exclusion rule and will not participate in the current operation.

## 3. Handling Items Requiring Confirmation

After selecting an item that requires confirmation or was not recognized, you can assign it to an existing author, create a new author, or exclude the file. GuiGui prioritizes safety and will not automatically choose between multiple candidates.

## 4. Common Settings

- **File type profiles:** Determine which file extensions are included in scans.
- **Tag cleaning rules:** Remove file-name tags that do not represent authors.
- **Scan exclusion rules:** Skip files that should not be included in organization.
- **Author entity library:** Add, edit and delete authors, groups and aliases; manage relationships, public overrides and conflicts in AuthorEntities.json.
- **Archive settings:** Adjust grouping quantities, directory naming, and reserved disk space.

## 5. Everything

<mark>Everything integration is optional. When available, GuiGui uses it to discover files more quickly. If it is unavailable, GuiGui automatically falls back to Windows file-system scanning without affecting core functionality.</mark>

## 6. Data and Safety

User settings, author aliases, organization history, and rule files are created in the program directory when GuiGui is first used or when settings are saved. Keep these data files when upgrading or moving the EXE.

Review the preview before organizing files. When processing many files, first verify your directory and naming settings with a small sample, and keep any necessary backups.

## 7. Checking for Updates

Use **Help → Check for Updates** or **About GuiGui** to check GitHub Releases. GuiGui displays official release information and opens the official download page; it does not automatically download, install, or overwrite the EXE.

## 8. Help Menu

- **User Guide:** View this guide.
- **Release Notes:** Review additions, fixes, and improvements in each version.
- **Performance Diagnostics:** Diagnose scan performance; the default settings are recommended for normal use.
- **About GuiGui:** View the current version, project homepage, license, and feedback links.


V1.12.8: Shallow scans now query only the selected directory (Everything `parent:`); recursive indexes are built on demand. File-provider and SQLite reconciliation timings are reported separately. See `Docs/INDEX_SCOPE_V1.12.8.md`.

V1.12.10 author library: author/alias CRUD, public overrides, relationships and conflicts. Legacy TXT is import-only. [Operations and acceptance](Docs/UNIFIED_AUTHOR_LIBRARY.md).

## V1.13.5 public-index incremental merge

Downloaded monthly CSVs are filtered with the same reference-name rules as AuthorDbBuilder. Work evidence is persisted in GuiGuiReferenceEvidence.db; a new GuiGuiAuthorIndex.db is staged from an official baseline and applied on the next cold startup. User confirmations remain in AuthorEntities.json. A full Windows build and GUI smoke test are required before deployment.
