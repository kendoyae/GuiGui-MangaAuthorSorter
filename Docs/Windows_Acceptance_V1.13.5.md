# V1.13.5 Windows 构建与上线验收

这是尚未实际完成 Windows 编译/运行的**源码候选版**。首次测试一定要使用拷贝目录和少量作品样本，不要直接操作唯一一份实体数据库。

1. 备份 `GuiGuiAuthorIndex.db`、`AuthorEntities.json`、`GuiGuiReferenceEvidence.db`；构建器备份 `GuiGuiAuthorBuildState.db`。
2. 在 Windows 执行归归 `BUILD_EXE.cmd`；校验全部官方语言包与内置字典，再启动。
3. 对构建器运行 `dotnet test`，特别核对 `AnewFileVersionDoesNotCountAnExistingGalleryTwice` 和 `ReadsNhMonthlyCsvWithQuotedMultilineTitles`。
4. 准备一个已知 Schema v4 的小型公共索引和含作者/社团的单月 CSV；运行下载/导入后应创建 `.official-base`、`.pending`、`.merge-state.json`。原 `GuiGuiAuthorIndex.db` 在运行中不可改变。
5. 关闭并重启归归后，确认 `.pending` 被切换、原库备份文件存在，新作者和社团可在“作者与社团 → 公共数据”里找到。
6. 检查之前存在的 E-Hentai `artist:`/`group:` 身份仍存在，用户库确认名称没有被自动覆盖。
7. 同一 CSV 重复更新应不增加重复实体/关系；修改一个画廊的社团后应替换旧的**本地新增**关联；撤销来源时应删除依赖于撤销来源的新增关联。原公共基线自带的证据不可随意删除。
8. 让两个不同作者拥有相同规范化名称和两个不同提供方身份，新 CSV 匹配此名称应进入 `PublicMergeConflict`，不应自动合并作者。
9. 模拟官方发布新基线替换当前公共索引，运行“重新校验”重新构建，确认用户此前下载的合法证据没有丢失。
10. 模拟断网、CSV 下载中断、SHA 错误和 SQLite busy，确保旧公共库始终能够启动、错误可重试。

**说明：** 构建器旧批次算法不会撤销以前已累计的历史记录，若要更正已经导入的原始大库，请重新构建官方基线。作品标题用于证据显示和人工交叉审核，不会凭相似标题自动合并实体。
11. 远程 `sources.json` 中新增一个不同来源 ID、同样的 `github-monthly-csv` 格式，确认公共库中新增 `Archive:<source-id>` 提供方身份且 `EHNamespace`/`EHTag` 不改变。
12. 同一画廊重复存在于两个不同月份且元数据不同时，仅最新有效文件的作者/社团信息应贡献本地新关联。
