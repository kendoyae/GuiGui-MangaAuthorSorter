# 公共库 Schema v4 接口

V1.12.12 按 GuiGui-AuthorDbBuilder 的 SCHEMA-CONTRACT.md 对接公共库。仅打开只读 GuiGuiAuthorIndex.db；不打开 GuiGuiAuthorBuildState.db。

实体、别名及提供方身份通过 Entity、EntityAlias、ProviderIdentity 兼容视图读取，关系沿用 ArtistGroup。不查询 EntityData、EntityAliasData、ProviderIdentityData 或构建证据表。来源显示使用视图联表恢复的 Source；SourceId 为内部来源编号，不作为实体身份。

自动识别继续使用 NormalizedCanonical、NormalizedAlias、NormalizedTag 精确查询及派生内存索引；LIKE 仅用于公共管理页候选展示。作者和社团按 EntityType / Namespace 隔离。

DanbooruArtistTag 现已加入公共实体字段映射与派生查询键、公共覆盖及禁用名称处理。旧库缺少此列时，通过 Entity 的列元数据检测并投影空字符串，继续兼容旧表结构。用户实体保留该标签并将其纳入识别依赖。

ProviderIdentity.ExternalId 通过 Provider + Namespace 构造覆盖身份，不依赖 SourceId 或 SQLite 行号；不能唯一绑定时保留原覆盖并要求确认。原始公共规范化键在覆盖视图中保留。

测试覆盖压缩表对应的三个兼容视图、来源联表、Provider ExternalId、Danbooru 标签、搜索、社团、禁用覆盖及只读字节校验。构建器实际产物验收结果见 [Schema v4 报告](AuthorLibraryAcceptance/SchemaV4/results.md)。用户当前公共库不会自动被测试替换。
本次识别规则缓存版本提升到 5，防止沿用缺少 Danbooru 字段映射的旧识别/计划结果；首次需要重新验证识别，源文件索引保留，不重新发现文件。
