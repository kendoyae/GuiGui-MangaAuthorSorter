# V1.13.5 公共库合并边界

- `GuiGuiAuthorIndex.db`：统一实体、别名、Provider 身份、ArtistGroup，客户端以只读方式扫描。
- `GuiGuiAuthorIndex.db.official-base`：开发者发布的基线快照；客户端必须保留。
- `GuiGuiReferenceEvidence.db`：下载清单 SHA、作品原始元数据及来源撤销状态，不参与作者扫描主索引。
- `AuthorEntities.json`：用户个人实体、关系及公共覆盖，任何增量更新均不得修改。
- 构建器 `GuiGuiAuthorBuildState.db` 不随客户端分发。

## 工作流
1. GitHub Tree SHA 检查；只下载变化的 CSV。
2. 按完整 CSV 替换同一来源文件的作品证据；下载失败不得更新 SHA。
3. 统一规范化、来源身份匹配，歧义键不自动合并，生成公共候选。
4. 在临时 DB 中从官方快照 + 全部有效证据重建公共索引。
5. SQLite quick_check 通过后生成待切换文件；在下一次启动前原子切换。
6. 文件修改或撤销时重新生成，不无限累计旧画廊或错配关系。

## 已知限制
- nHentai 的 artist/group 名字来自作品元数据，属于未确认的离线来源证据，不能作为 E-Hentai TAG 身份。
- 旧构建器批次数据库包含的历史记录无法凭聚合计数精确撤销；需要重新构建官方基线。
- 没有 Windows C# 编译器时，只能执行静态校验和可移植 SQLite 合并契约测试。
- 需要检查远程来源许可；本次没有自动改写远程 GitHub 配置。

## 远程新增同格式来源

- `SourcesConfig.json` 可增加新的 `github-monthly-csv` 来源；其作品证据同样会整合进入公共主索引。
- 未知来源采用 `Archive:<source-id>` 的独立、未验证 Provider，不会冒充 E-Hentai 或 nHentai 官方身份。
- 相同姓名、不同 Provider 实体 ID 不一致时，记录 `PublicMergeConflict`，不自动合并。
- 如果同一来源的同一画廊在两个按月 CSV 中重复，以路径排序较新的仍有效文件作为作品记录。
- 新来源需要其他数据格式/协议时，仍须由新解析器支持；远程配置只能使用 EXE 已支持的解析协议。
