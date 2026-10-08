# 持久化文件索引与增量识别架构

本文档定义 V1.12.1 起源文件扫描的架构边界。它不是额外的“扫描缓存”，而是归归处理源文件的主索引模型。

## 核心不变量

1. `FileIndexCache.db` 保存持久化文件事实和识别事实；内存快照只是运行时加速层。
2. 文件发现 Provider（Everything / Windows 文件系统）只负责提供当前文件状态，不负责作者识别。
3. 源索引始终按完整递归范围维护；“扫描子目录”、数量限制、筛选和搜索属于 Query/View 投影，不能删除或使识别事实失效。
4. FileIndex 先计算 Delta，再决定哪些文件可进入解析/识别。`Unchanged` 文件不得进入 Recognition。
5. 路径移动且文件名不变时保留 filename-derived 解析和识别事实；文件名改变时只使该文件的解析、识别与计划缓存失效。
6. 归归自己完成 Move/Rename 后立即同步数据库、活动 ScanSession 和内存快照，不等待下一次文件系统扫描。
7. Everything 查询应直接使用其索引返回的大小/创建时间/修改时间，不能为了“验证缓存”再逐文件读取 `FileInfo.Length` / `LastWriteTime`。
8. 缓存失效必须分层：UI/View 条件不能清空 FileIndex 或 Recognition；识别规则变化也不能迫使文件重新发现。

## 数据流

```text
Everything / FileSystem
        ↓
PersistentSourceIndexProvider
        ↓
FileIndexCacheDatabase
        ↓
FileIndexDelta
 ├─ Unchanged → 直接复用
 ├─ Added     → 解析/识别
 ├─ Modified  → 仅失效受影响计划
 ├─ Moved     → 更新路径并保留识别事实
 ├─ Renamed   → 只重算该文件
 └─ Deleted   → 删除持久化事实并驱逐内存事实
        ↓
Recognition / Migration Plan（仅 Miss）
        ↓
Query / View
        ↓
DataGridView
```

## 持久化索引范围

`PersistentSourceIndexProvider` 无论调用方当前是否勾选“扫描子目录”，都维护一个 canonical recursive source index。调用方的 `recursive=false` 只从这个索引投影根目录直属文件。

文件类型配置通过 `SourceScopes` / `SourceFileScopes` 记录独立成员关系。某一文件类型范围的同步不得因为“本次看不到”而删除其他范围已经建立的文件事实。

Provider 选择、排除规则、blocked paths 与递归开关不拥有持久化识别事实。它们改变视图或同步方式，不等于文件本身发生变化。

## Delta 与缓存失效

- `Unchanged`：记录 `CacheHits`；不 UPDATE 文件行，不调用解析/识别。
- `Added`：建立 SourceFiles 行，随后按正常流程生成识别事实与计划。
- `Modified`：文件名不变时保留作者识别事实，仅使依赖文件属性的计划缓存失效。
- `Moved`：同名路径迁移时复用相同 FileId，并迁移内存 parsed/recognition cache key。
- `Renamed`：复用 FileId，但删除该 FileId 的 ParsedMetadata、RecognitionCache 和 MigrationPlanCache。
- `Deleted`：删除 SourceFiles（FK 级联删除持久化事实），并通过 invalidation paths 驱逐运行时缓存。

外部移动当前使用“大小 + 修改时间 + 创建时间 + 扩展名”的唯一签名作为无额外逐文件 I/O 的回退匹配。归归自己执行的移动不需要猜测，直接按已知 old/new path 更新。

## ScanSession

持久化 FileIndex 是全局事实集；一次用户扫描只创建其 Query 范围内的 `ScanSession`。Recognition、统计和 MigrationPlan 只能看到当前 ScanSession，不能因为后台索引比当前视图更大而处理额外文件。

## 性能验收

核心验收不是“第二次扫描更快”，而是调用数量：

- 1500 文件第一次扫描：Recognition 1500。
- 无变化再次扫描：Recognition 0。
- 改一个文件名：Recognition 1。
- 归归移动一个同名文件：Recognition 0。
- 切换“扫描子目录”：File discovery 0（内存索引健康时），Recognition 0。
- 搜索/状态筛选/排序：Recognition 0。
- 外部变化：允许重新查询 Everything 以同步 Delta，但 Unchanged 文件仍不得进入 Recognition。

`Tests/IncrementalIndexContractTests.cs` 维护这些关键边界的契约测试。
