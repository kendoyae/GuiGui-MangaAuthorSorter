# 工程结构说明

完整的当前工程说明见 `Docs/DEVELOPMENT.md`。本文保留目录拆分的设计背景与源码清单维护规则。

V1.11.9 起，项目按“职责”而不是版本或功能历史拆分目录。命名空间继续使用 `MangaAuthorSorter`，本次整理不改变运行逻辑。

## 根目录

- `Program.cs`：应用入口。
- `AppFiles.cs`：应用级文件/目录定位。
- `MangaAuthorSorter.csproj`：Visual Studio / MSBuild 项目。
- `BUILD_EXE.cmd`：直接使用 .NET Framework csc 编译，统一输出到 `bin/Release/GuiGui.exe`。
- `MAKE_RELEASE.cmd`：生成用户分发目录。
- `START.cmd`：本地快速启动。
- `README.md`：GitHub 项目首页与用户快速说明。
- `Docs/Guide.<语言>.md`：程序内嵌的多语言使用说明。
- `Docs/Changelog.<语言>.md`：程序内嵌的多语言更新说明。

## Core

纯业务与规则层：归档规划、作者识别、文件类型、命名、扫描模式、安全检查、数据模型等。这里不放 WinForms 窗口。

## Services

数据持久化和外部能力：别名/实体/历史/设置存储、Everything、在线作者 Provider、语言管理等。

## Forms

所有 WinForms 窗口，包括 `MainForm.cs` 和各设置/管理窗口。

## UI

可复用界面辅助：DataGridView、表格交互、状态视觉与统一样式。

## Docs

开发者规则和内部设计文档。不会复制到普通用户的 Portable 分发目录。

## Build

`CompilerSources.rsp` 是 BUILD_EXE.cmd / MAKE_RELEASE.cmd 共用的唯一 csc 源文件清单。新增、删除或移动 `.cs` 时：

1. 修改实际文件位置；
2. 同步 `MangaAuthorSorter.csproj` 的 `<Compile Include=...>`；
3. 同步 `Build/CompilerSources.rsp`；
4. 运行 `BUILD_EXE.cmd` 或 `MAKE_RELEASE.cmd` 验证。

## Languages

官方语言包、模板和翻译规范。语言包是运行时内容，不属于源码层。

## 发布边界

`MAKE_RELEASE.cmd` 生成：

- `RELEASE/Single_EXE/`：单 EXE；
- `RELEASE/Portable/`：EXE + 便于外部阅读的多语言 Docs + Languages。

源码、Docs、Build、csproj 和构建脚本不进入用户 Portable 包。单 EXE 已包含运行所需的固定资源；Portable 中的文档与语言文件用于外部阅读和语言包维护，不是核心界面启动依赖。
