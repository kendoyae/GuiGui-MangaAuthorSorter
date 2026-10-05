# 开发与工程说明

本文记录归归当前工程结构、运行数据边界和构建约定。版本历史统一记录在根目录 `CHANGELOG.md`；具体不可破坏的开发约束继续由 `Docs/` 下各专项规则维护。

## 技术基线

- Windows Forms
- .NET Framework 4.8
- 主命名空间：`MangaAuthorSorter`
- 正式内置语言：简体中文、英语、德语
- 正式版本通过 GitHub Releases 发布

## 运行时数据文件

- `AuthorAliases.txt`：作者别名数据
- `AuthorEntities.json`：作者实体、外部身份与在线查询缓存
- `TagCleaningRules.json`：标签清洗规则
- `ScanExclusionRules.json`：全局扫描排除规则
- `ExclusionList.txt`：手工排除列表
- `UserSettings.ini`：用户设置
- `FileTypeProfiles.json`：文件类型方案
- `History.json`：整理历史记录
- `GridLayouts.ini`：表格列宽与显示状态
- `ScanPerformance.log`：启用性能诊断后产生的性能记录

这些文件属于用户数据，不嵌入 EXE。缺失时由现有逻辑按需创建；开发时必须保持旧数据兼容。

## 固定内嵌资源

程序图标、关于页图标、微信与支付宝二维码、使用说明、更新说明和许可证全文属于程序固定资源，必须嵌入 EXE。单 EXE 运行不得依赖外部 `Assets`、`README.txt`、`CHANGELOG.md` 或 `LICENSE_NOTICES.txt`。

## 工程目录

- `Core/`：核心识别、归档规则与业务模型
- `Services/`：数据存储、在线查询、Everything、语言及更新服务
- `Forms/`：WinForms 窗口
- `UI/`：通用控件、表格交互与视觉辅助
- `Docs/`：开发约束与设计说明
- `Build/`：构建辅助文件和统一源码清单
- `Languages/`：官方语言包、模板和语言包说明
- `Assets/`：用于编译进 EXE 的固定资源及开发期素材

## 构建入口

- `START.cmd`：本地快速启动；缺少 EXE 时先构建
- `BUILD_EXE.cmd`：使用 `Build/CompilerSources.rsp` 编译主程序，统一输出到 `bin/Release/GuiGui.exe`
- `MAKE_RELEASE.cmd`：编译并生成分发目录
- `MangaAuthorSorter.csproj`：Visual Studio / MSBuild 工程

新增、删除或移动 `.cs` 文件时，必须同步维护项目文件和 `Build/CompilerSources.rsp`，并验证两条构建路径。

## 版本号规则

- 程序版本的唯一来源是 `Program.cs` 中的程序集版本；窗口标题、关于页、更新检查和联网标识均从程序集读取。
- 普通保存、内部调试和重复编译不自动增加版本号。
- 某个 EXE 已交付测试或正式发布后，只要再次加入用户可见功能、行为变化或错误修复，下一次交付前必须更新版本号，禁止不同内容的 EXE 共用同一版本号。
- 小功能与错误修复增加第三段修订号，例如 `1.11.24 → 1.11.25`；一组较大的新能力增加第二段次版本号；不兼容的大改动才增加第一段主版本号。
- 更新版本时必须同步 `CHANGELOG.md` 和带版本文字的发布说明，并在构建后核对 EXE 的程序集版本。

## 文档职责

- `README.md`：GitHub 项目首页和普通用户快速说明
- `README.txt`：程序内嵌的完整使用说明
- `CHANGELOG.md`：版本更新记录
- `Docs/DEVELOPMENT.md`：稳定的工程说明
- `Docs/*_RULES.md`：具体功能不可破坏的开发约束

工程规则需要长期保留，但不应混入用户使用说明或版本更新记录。长期说明描述“现在是什么”；首次加入版本、兼容演进和历史修复写入 CHANGELOG。
