# V1.13.7 — 高级作者库三来源构建验收（Windows）

测试环境：Windows 10/11 x64 + .NET Framework 4.8；**无需 .NET 8**。先备份正式 `GuiGuiAuthorIndex.db`、`GuiGuiReferenceEvidence.db`、`AuthorEntities.json`，使用隔离的测试目录编译和运行。

1. `BUILD_EXE.cmd --no-pause` 应无编译错误；启动时简中、英文、德文内置字典没有重复 Key。
2. 「参考数据更新」→「完整构建（高级）」进入原生 WinForms 三来源窗口，只看到 E-Hentai Current、nh-metadata-archive、E-Hentai Tag Aggregate 三张卡；缩放、滚动、卡片按钮布局正常。
3. 每张卡的「来源」展示实际 URL；「检查」能显示远程元数据；断网失败不会修改已有 DB；源地址可由配置的 `advancedSources` 调整（解析类型固定）。
4. nh-metadata-archive 下载历史或变化 CSV 先只保存原文件及 SHA；「过滤」随后批量导入，重复过滤不增加记录；改写历史 CSV 能替换旧证据。也能选择单个本地 CSV。
5. E-Hentai Tag Aggregate：下载/选择 `aggregated.sqlite.gz`、自动 GZIP 解压与流式标签过滤；确认 `artist`、`group/circle` 出现在公共索引，`WorkEvidence` 没有凭空新增聚合画廊，ArtistGroup 不会凭聚合标签产生。
6. E-Hentai Current：下载 `e-hentai.db.zstd`、可选择已解压的 `.db`；若过滤 zstd，需要另行提供可信的 `Modules/zstd.exe` 或 PATH 中的 zstd；没有工具时给出清晰错误。验证真实 SQLite 的标签表/关系表是否被识别。
7. 同一 E-Hentai 标签分别出现在 Current 与 Aggregate 时，公共库中只生成一个对应 Provider 身份，不重复新建实体；不同作者同名或跨来源仅名称相同不能静默合并。
8. 在旧 Schema（例如 v3）公共库上尝试更新时应出现操作说明，不应删除原库。明确点击「新建空库」并确认后备份为 `.before-v4-build.bak`；重启前不替换活跃库。
9. 成功过滤后 `.pending` 在重启时激活；反复更新可重入；中途取消、下载失败、SQLite 事务失败不得破坏现有公共库；`AuthorEntities.json` 保持原样。
10. 下载进行中点击「暂停」与「继续」验证网络流检查点；取消期间旧公共库应保持可用，压缩解压外部 zstd 阶段暂停可能延迟到下一检查点。
11. 开启「过滤完成后删除原始数据库」只允许删除 `AuthorDbSources` 下的文件。用户通过文件选择器指定的外部数据不得删除。

## 现阶段尚未声称完成的实机验收

当前运行环境没有 Windows C# 编译器；未实测下载 1 GB 级 E-Hentai Current 资源，也未对每种真实上游数据库 schema 执行完整导入。源码内含 Schema v4 合并 SQL 的便携回归脚本，但**不能替代 Windows 上的真实测试**。若上游 SQLite 变更表结构，应根据实际 `sqlite_master` / `PRAGMA table_info` 诊断修改解析器，不能假定任何任意 SQLite 都可导入。
