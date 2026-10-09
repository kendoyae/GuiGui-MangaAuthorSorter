# V1.13.10 高级数据源合并：Windows 验收

## 当前 V1.13.9 卡在「正在合并公共库」时

1. 请勿点击「新建空库」，勿删除已下载的 `aggregated.sqlite.gz` 或 `GuiGuiReferenceEvidence.db`。
2. 先尝试「取消任务」并等待。如果旧版长时间没有响应（V1.13.9 合并 SQLite 时可能无法及时响应取消），确认没有其它归归操作后，在任务管理器结束旧版归归进程。
3. 先备份 `GuiGuiAuthorIndex.db`、`GuiGuiAuthorIndex.db.official-base`（存在时）、`GuiGuiReferenceEvidence.db`、`AuthorEntities.json`、`GuiGuiAuthorIndex.db.merge-state.json`（存在时）以及所有 `.pending` 文件。
4. 用独立目录编译 V1.13.10。要复用此前已导入的作品证据，需要把原数据目录内的上述数据库与合并状态文件复制到新 EXE 所在目录；切勿用新版本的空白数据覆盖原文件。
5. 打开「作者参考数据更新 → 完整构建（高级）」点击**重新合并**，无需再次过滤 `aggregated.sqlite.gz`。
6. 等待「公共库合并完成，将在重启归归后生效」，退出并重启后，再在公共作者/社团列表里核对数量与来源身份。

## 性能与安全验证

- 首先拿备份副本测试 E-Hentai Tag Aggregate 的 102,582 条标签。
- 「过滤完成」之后进度应显示「正在合并公共库」，并周期性报告已经运行的秒数，而不是立刻 100%。
- 取消合并时，只回滚当前合并的临时数据库；`GuiGuiReferenceEvidence.db` 中已经提交的证据不应被删除。
- 再次点击「重新合并」应复用本地已过滤证据，无需重复下载与 GZIP 解压。
- 重复执行不应重复创建作者实体；E-Hentai 已存在的身份不应被其他来源同名内容覆盖。
- 确保 `.pending` 待激活文件完整，且下次重启安全切换。检查 SQLite `PRAGMA quick_check` 与 `PRAGMA foreign_key_check`。

## 当前环境限制

便携 SQLite 性能和实体回归测试通过；Windows Framework 4.8 `csc.exe`、WinForms、Native `winsqlite3.dll` 回调、以及真实 E-Hentai 大型数据库实测，仍需在 Windows 执行。
