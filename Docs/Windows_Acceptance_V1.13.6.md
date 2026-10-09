# V1.13.6 Windows 验收清单（必须在本机执行）

**前提**：`.NET Framework 4.8`、Windows 10/11 x64；无需安装 .NET 8。不要在唯一生产库上操作，先备份 `GuiGuiAuthorIndex.db`、`.official-base`、`.merge-state.json`、`GuiGuiReferenceEvidence.db`、`AuthorEntities.json`。

1. `BUILD_EXE.cmd --no-pause` 编译成功，`bin\Release\GuiGui.exe` 正常启动，语言切换简中/英文/德文没有重复 Key 崩溃。
2. 用全新临时目录启动（无 `GuiGuiAuthorIndex.db`）：进入「作者参考数据更新」，应优先检查官方库，未配置 manifest 时提示未配置；直接点击常规「下载更新」应先提示必须获取公共库或使用高级完整构建，不应擅自下载数百 MB 历史 CSV。
3. 准备一份已知合格的 Schema v4 测试数据库：选择「导入本地库」，提示重启生效。重启后浏览「作者与社团」，字段、别名、Provider 身份可正常加载；`AuthorEntities.json` SHA-256 不变。
4. 配置到**真实**官方 manifest 测试地址：检查时显示 revision/size，下载后重启，确认新的官方基线及本地已下载的作品证据都保留；尝试故意改错 manifest 的 SHA-256 或长度，验证导入被拒绝且旧库可正常启动。
5. 用隔离测试 GitHub 仓库或本地测试 CSV 模拟：第一次导入、新增 CSV、修改历史月份、撤销来源，逐次重启；检查作者、社团、ArtistGroup 与 `PublicMergeConflict`；重复执行不得增加重复作者与计数。
6. 完整构建：删除测试目录公共库，在高级「完整构建」确认后检查全部历史 CSV，应该**只下载尚未正式导入的文件**，不把 `baseline-only` 误当已导入；重启生成 Schema v4 索引。
7. 下载中暂停、网络断开、官方库下载中断、索引构建崩溃时旧数据库不可损坏；重启冷启动只激活 SHA 和 Schema 校验通过的 `.pending`。
8. 创建已确认的用户实体与别名、禁用名、公共覆盖；分别在本地 CSV 增量合并和官方库升级后确认它们完全不变。
9. 退出重启后检查内存索引和 FileIndexCache 识别一致，新增实体、社团与关联在作者详情出现；没有随程序发布任何 .NET 8/WPF Runtime DLL。

> 以上是待在 Windows 实机执行的验收项目，不应把静态测试当作实际测试通过。
