# 在线作者解析证据链规则

## 定义

在线作者解析不是“选择一个网站搜索名字”，而是从作品元数据取得 `artist` / `group` 身份与共现证据，再分别判断身份、别名和作者—社团关系。

- 身份证据：本地作者是否对应某个 `artist`。
- 别名证据：两个同 Namespace 名称是否属于同一实体。
- 关系证据：`artist` 与 `group` 是否存在作品关系。

三类证据不得互相替代。`artist + group` 共现只建立关系，绝不合并实体；多个 `artist` 共现默认是合作，不是别名。

## 数据来源与链路

正式 Provider 只有 E-Hentai 与 nHentai。GitHub 仅可作为本地元数据的下载渠道，不得记成 Provider；标签翻译数据库不得进入评分链。

```text
用户确认 / AuthorEntities / 本地 Alias
  → Provider Evidence Cache
  → EH 与 NH 本地 Metadata
  → 统一作品模型与同作品去重
  → Identity / Alias / Relation / Affinity 四类评分
  → 冲突检测与 Early Stop
  → 仍有证据缺口时才访问 EH 或 NH 在线数据
```

身份查询优先 EH；补充独立作品关系时可优先 NH。在线取样按 `5 → 10 → 25` 渐进扩展，达到门槛立即停止。

## 自动确认门槛

- `IdentityScore >= 90` 才可自动确认身份。
- NH 只能提供身份与别名辅助证据，不能单独确认 Alias。
- 出现 `CanonicalTagCollision`、`MultipleCanonicalCandidates`、`NamespaceCollision` 或 `ProviderIdentityConflict` 时，无论分数多高都禁止自动确认。
- 网络错误、限流、协议变化和真正的 `NoResult` 必须分开记录。
- Provider 证据先进入缓存；只有证据充分且无冲突时才写入实体。
- 用户确认永远最高优先级，Provider 不得覆盖。

## 去重与合集降权

Gallery 数量不等于独立作品数量。同一 Gallery Chain、翻译、重传、修正版和跨来源同一本作品必须聚类；同作品的 EH + NH 只增加来源一致性，不算两票。多作者、多社团作品按人数降权，避免合集污染。

## 作者—社团关系

作者与社团是多对多。单次共现只是一条作品关系证据；自动落库要求关系分达到门槛、双方身份已确认且没有硬冲突。Affinity 只表示某社团对作者的重要程度，不能用来删除真实的低频关系。

## 当前 V1 落地

设置页固定展示证据链，不再提供 Provider 下拉框。扫描使用统一 `EvidenceChain` 标识；先复用本地实体与查询缓存，再以 EH 为主身份源、NH 为辅助源按缺口请求。身份分不足 90 的结果保存为 `candidate`，不会写成已确认作者。

本地解析始终可独立工作；在线链路默认关闭，失败不会中断扫描或归档。
