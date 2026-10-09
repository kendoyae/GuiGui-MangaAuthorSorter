# GuiGui V1.13.6 — 统一公共库下载 / 增量合并 / 完整构建

## 三条使用路径

- **普通用户（推荐）**：开发者发布 `GuiGuiAuthorIndex.db` 和对应 SHA-256 manifest，用户在「作者参考数据更新 → 公共作者库」先「检查官方库」、再「下载官方库」。完整文件经大小、SHA-256、Schema v4 和 SQLite 完整性校验后暂存，并在下次启动时直接切换；此流程不创建、读取或重放 `GuiGuiReferenceEvidence.db`。
- **常规用户增量更新**：Github `sources.json` 中启用的 `github-monthly-csv` 源，按 Git blob SHA 下载变化文件，导入作品及作者社团证据，从官方基线重建合并后的公共库，在重启后安全切换。若已有公共库，首次仍保持历史基线只取最近月份；已有历史文件变化时会补录。用户数据层 `AuthorEntities.json` 不变。
- **开发者 / 高级用户完整构建**：按「完整构建（高级）」后先确认所有历史 CSV 下载列表，再下载尚未**正式导入**（`State='imported'`）的文件。即使没有公共库，也会先创建 **Schema v4 空基线**，再基于当前全部正式导入作品证据生成 `GuiGuiAuthorIndex.db`。没有数据源证据时，空库不等价于完整官方库；CSV 以外原始格式目前仍须开发额外解析器。

## 公共库分发：你需要先发布清单

V1.13.6 **不会假设** GitHub 已经发布 `.db`，仓库当前不存在可靠的公开下载地址时配置为 `"manifestUrl": ""`。程序会清晰提示该地址未配置；同时允许用户从磁盘「导入本地库」。

1. 发布经过构建验证、符合 Schema v4 的 `GuiGuiAuthorIndex.db`（暂不压缩）到 GitHub 发行资产或可直接 GET 的 HTTPS 路径。
2. 在 Windows PowerShell 中运行：

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\Build\CreatePublicIndexManifest.ps1 -DatabasePath .\GuiGuiAuthorIndex.db -DownloadUrl 'https://github.com/YOUR/REPO/releases/download/RELEASE/GuiGuiAuthorIndex.db' -Revision 'YYYY-MM-DD-r1'
   ```

3. 上传生成的 `public-index-manifest.json` 到你控制的 GitHub Pages 或 `raw.githubusercontent.com` 对应路径；把实际 URL 填进 `SourcesConfig.json` 的 `publicDatabase.manifestUrl`，再发布源配置。
4. 确认两个 URL 可被无登录用户访问。客户端要求清单 `schemaVersion = 4`、明确的 `size`、`sha256`、`revision` 和允许域名的 HTTPS `url`；不自动接受未校验的 Release/数据库。

示例结构（以下哈希只是字段说明，不代表真实发布）：

```json
{
  "schemaVersion": 4,
  "revision": "2026-10-r1",
  "url": "https://github.com/OWNER/REPO/releases/download/TAG/GuiGuiAuthorIndex.db",
  "size": 12345678,
  "sha256": "<64-character SHA-256 of exact DB file>"
}
```

## 文件分工与恢复

- `GuiGuiAuthorIndex.db`：下一次启动时激活的公共统一查询索引。
- `.official-base`：开发者基线；本地 CSV 增量永远重放在其上，不污染基线。
- `.official-base.next`：新官方基线暂存，合并完成激活时一并晋升为官方基线。
- `.pending`：新构建结果，经 SQLite 校验后等待下一次冷启动切换；已有库会备份到 `.pre-merge.bak`，替换后的旧官方基线备份到 `.previous.bak`。
- `.merge-state.json`：记录预期文件校验值，阻止旧 pending 覆盖用户刚手动更换的新数据库。
- `GuiGuiReferenceEvidence.db`：仅供高级来源构建保存来源文件 SHA、作品证据与撤销记录；普通公共库更新不依赖此文件。
- `AuthorEntities.json`：用户层，不参与覆盖或构建，无需从旧版转换。

## 安全边界

- 只允许 Schema v4、通过 SQLite 校验的公共库切换；联网发布还校验下载 SHA-256/大小和来源域名。
- GitHub 远程来源 JSON 是**数据地址与下载配置**，不是执行代码；本次不加载任何远程 DLL。
- 同名不等于同一实体，多作者共现仅作为作品关联，不自动合并两人；E-Hentai 标签不能由 nHentai CSV 猜测。
- 计划中断只留下可重试的证据或临时文件，不应使现有活动库失效；切换只能在 MainForm 打开 SQLite 读取连接前发生。
- 离线完整构建依赖现有证据：尚未导入的历史 CSV、官方库额外的 E-Hentai / Danbooru 来源身份不会凭空产生。
- 本机无 Windows Framework 编译器，**必须在 Windows 运行 BUILD_EXE.cmd 并完成启动、离线导入、联网更新、重启激活、取消/重试测试**。
