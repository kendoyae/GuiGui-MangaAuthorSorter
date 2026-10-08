# V1.12.6 构建修正版

本修正版不改变应用版本与作者识别算法，只处理 V1.12.6 编译与批处理编码故障。

- 修复 `Core/AuthorIndex.cs` 的 CS0191：`FolderCount` 改为从 `_folders.Count` 读取的只读属性，并移除增量登记中的 `FolderCount++`。
- 修复 `START.cmd`、`BUILD_EXE.cmd`、`MAKE_RELEASE.cmd` 的 UTF-8 BOM 导致 Windows CMD 将 `@echo off` 解码为 `锘緻echo` 等乱码命令的问题。
- 所有 CMD 文件使用无 BOM、CRLF 换行。

请在 Windows 执行 `BUILD_EXE.cmd`，然后运行 `Build\RUN_INCREMENTAL_CONTRACT_TESTS.cmd`。当前打包环境无法执行 Windows .NET Framework 的 `csc.exe`。
