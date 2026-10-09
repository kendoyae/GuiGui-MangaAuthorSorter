# Windows acceptance — V1.13.8

1. Extract the **entire** archive into a new directory, preserving `Core`, `Services`, `Forms`, `Build`, `UI`, etc.
2. Run `BUILD_EXE.cmd`. Confirm `[OK] C# source manifest: 79 files exist` and language checks before `Building GuiGui.exe...`.
3. If any `CSxxxx` error follows, retain the entire console output and report it; runtime compilation was not available when this archive was generated.
4. If compilation succeeds, launch `bin\Release\GuiGui.exe`, open advanced author database builder, and verify the three sources and actions.
5. Before filtering sources, back up `GuiGuiAuthorIndex.db`, `AuthorEntities.json`, and evidence DB. Test using a separate application directory.

Only the build manifest/validation and release-version metadata changed in V1.13.8.
