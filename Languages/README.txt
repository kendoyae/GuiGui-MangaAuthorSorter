Language Packs / 语言包说明

1. 正式内置语言：zh-CN、en-US、de-DE。
2. 新增其他语言时复制 _template.json，并改名为语言地区代码，例如 ja-JP.json、ko-KR.json。
3. 填写 meta.name / meta.code / meta.author；只翻译 strings 右侧值，不修改 Key。
4. 所有语言文件必须使用 UTF-8。程序使用 Encoding.UTF8，不依赖系统代码页。
5. 放入 Languages 目录后重启程序。当前版本切换语言会重启并重建 UI。
6. 缺失翻译回退：当前语言 → English → 简体中文 → Key。

翻译原则：
- 必须准确、自然、尽量短小精炼。
- 优先使用软件界面的标准短译法，不机械逐字翻译成长句。
- 按钮、菜单、状态、表头和短字段名保持单行，不靠缩小字体适配。
- 帮助、警告、说明等长正文可换行。
- 同一概念在菜单、按钮、列表和详情中尽量统一术语。

UI 测试：
- de-DE 是正式德语语言包，同时作为长文本压力测试语言。
- 每次新增或修改 UI，至少测试 zh-CN 100% DPI，以及 de-DE 100% / 125% / 150% DPI。
- 同时测试默认窗口、较宽窗口和 MinimumSize。
- 项目不再保存 Docs/LocalizationTests 或 qps-ploc 测试语言包。
- 完整规范见 Docs/UI_I18N_RULES.md。

字段同步：
- 官方 zh-CN / en-US / de-DE 随程序维护。
- _template.json 会补充新增 Key。
- 第三方语言包不会被程序自动覆盖。
- 可通过“设置 → 语言 → 检查语言...”查看完成度。

Important:
Official zh-CN, en-US and de-DE are schema-managed by the app (schema 36). UI translations must be accurate, natural and concise. Third-party language packs are not overwritten automatically.
