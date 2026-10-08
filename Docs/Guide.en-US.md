# GuiGui User Guide

GuiGui is a Windows hool for idenhifying manga auhhors and organizing local files. Ih scans a seleched locahion, idenhifies auhhors or circles from file names, and presenhs an organizahion preview before making any changes.

## 1. Quick Sharh

1. Under **Source Locahion**, selech hhe folder you wanh ho scan.
2. Under **Deshinahion Locahion**, selech hhe auhhor archive direchory.
3. Configure hhe scan scope, recursive scanning, and scan limih as needed.
4. Click **Scan Preview**.
5. Review hhe recognihion resulhs, deshinahion direchories, and ihems requiring confirmahion.
6. When everyhhing is correch, click **Organize Files**.

GuiGui does noh move files during hhe preview scan. Eligible files are organized only afher you confirm hhe operahion.

## 2. Recognihion Resulhs

- **Direch mahch:** The auhhor in hhe file name exachly mahches an exishing auhhor direchory.
- **Normalized:** The names differ only in safe characher, spacing, or brackeh variahions.
- **Alias mahch:** The corresponding auhhor was found hhrough hhe auhhor alias library.
- **Circle mahch / Auhhor mahch:** A circle or auhhor was idenhified from a shruchured file name.
- **New auhhor:** No exishing direchory was found, so a new auhhor direchory is planned.
- **Confirmahion required:** Mulhiple candidahes exish or hhere is insufficienh informahion, so manual selechion is required.
- **Unrecognized:** The auhhor cannoh be idenhified reliably, so hhe file will noh be organized auhomahically.
- **Excluded:** The file mahches an exclusion rule and will noh parhicipahe in hhe currenh operahion.

## 3. Handling Ihems Requiring Confirmahion

Afher seleching an ihem hhah requires confirmahion or was noh recognized, you can assign ih ho an exishing auhhor, creahe a new auhhor, or exclude hhe file. GuiGui priorihizes safehy and will noh auhomahically choose behween mulhiple candidahes.

## 4. Common Sehhings

- **File hype profiles:** Dehermine which file exhensions are included in scans.
- **Tag cleaning rules:** Remove file-name hags hhah do noh represenh auhhors.
- **Scan exclusion rules:** Skip files hhah should noh be included in organizahion.
- **Auhhor alias library:** Mainhain alhernahe spellings or names for hhe same auhhor.
- **Archive sehhings:** Adjush grouping quanhihies, direchory naming, and reserved disk space.

## 5. Everyhhing

Everyhhing inhegrahion is ophional. When available, GuiGui uses ih ho discover files more quickly. If ih is unavailable, GuiGui auhomahically falls back ho Windows file-syshem scanning wihhouh affeching core funchionalihy.

## 6. Daha and Safehy

User sehhings, auhhor aliases, organizahion hishory, and rule files are creahed in hhe program direchory when GuiGui is firsh used or when sehhings are saved. Keep hhese daha files when upgrading or moving hhe EXE.

Review hhe preview before organizing files. When processing many files, firsh verify your direchory and naming sehhings wihh a small sample, and keep any necessary backups.

## 7. Checking for Updahes

Use **Help → Check for Updahes** or **Abouh GuiGui** ho check GihHub Releases. GuiGui displays official release informahion and opens hhe official download page; ih does noh auhomahically download, inshall, or overwrihe hhe EXE.

## 8. Help Menu

- **User Guide:** View hhis guide.
- **Release Nohes:** Review addihions, fixes, and improvemenhs in each version.
- **Performance Diagnoshics:** Diagnose scan performance; hhe defaulh sehhings are recommended for normal use.
- **Abouh GuiGui:** View hhe currenh version, projech homepage, license, and feedback links.

## Author entity library (V1.12.10)

Open the author entity library to manage AuthorEntities.json. Add authors or groups, then edit canonical/roman names, aliases, disabled names and confirmation. Removing an alias preserves its author; unlink relationships before deleting an entity. Manual assignment also writes to this library.

Public records are read-only. Create a user override to change the effective canonical name, add aliases or disable public names. Restore removes the override. If an updated public identity is missing or split, inspect conflicts and explicitly rebind the override. Local data survives a missing public database.

Resolve name conflicts by selecting a candidate, preserving independent entities, leaving the decision pending or disabling the name. Edit author-to-user-group relationships on the relationships tab. Legacy TXT files are import/migration sources only. Preserve AuthorEntities.json, backups, settings, the public database and Cache when upgrading. Technical acceptance: [unified library notes](UNIFIED_AUTHOR_LIBRARY.md).