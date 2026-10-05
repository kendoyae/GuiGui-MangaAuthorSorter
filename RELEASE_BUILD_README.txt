归归 / GuiGui V1.11.29｜一键分发构建说明
================================================

在 Windows 上解压源码包后，可直接双击：

MAKE_RELEASE.cmd

它会：
1. 读取 Build\CompilerSources.rsp 中的统一源码清单
2. 编译到 bin\Release\GuiGui.exe
3. 创建 RELEASE 文件夹
4. 生成 Single_EXE 与 Portable 两种分发目录

单 EXE：
RELEASE\Single_EXE\GuiGui.exe

Portable：
RELEASE\Portable\

Portable 包保留以下方便用户浏览和自定义的附加文件；程序本身不依赖它们启动：
- GuiGui.exe
- README.txt
- CHANGELOG.md
- Languages\（含正式 zh-CN / en-US / de-DE）
- Assets\（支持项目二维码与 QQ 反馈链接配置）

Docs\ 中的规则说明、Build\ 中的编译清单、源码文件、csproj 和构建脚本不会复制到用户分发包。

程序首次运行会按需创建：
AuthorAliases.txt
AuthorEntities.json（作者实体与在线解析缓存）
TagCleaningRules.json（标签清洗规则）
ScanExclusionRules.json（全局扫描排除规则；默认示例规则关闭）
ExclusionList.txt
UserSettings.ini
FileTypeProfiles.json
History.json
GridLayouts.ini

V1.11.6 起，排除结果通过主界面“已排除”状态筛选查看；排除项始终不参与执行整理。
UserSettings.ini 中 SafetyReserveGb 默认为 5 GB，可在“设置 → 归档设置”修改。

在线作者解析默认关闭；启用后需要网络连接，当前兼容数据源仍为 Danbooru。
E-Hentai Provider 不在 V1.11.28 接入范围内，后续版本单独处理。
网络不可用时本地扫描/归档仍可继续。

Everything64.dll / Everything32.dll 不是强制文件。
Everything SDK 不可用时，程序会自动回退到文件系统扫描。

当前程序使用 .NET Framework 4.8 WinForms。
目标 Windows 需要兼容的 .NET Framework 4.x 运行环境。

支持项目资源：
- 微信与支付宝二维码、使用说明、更新说明和许可证全文均已内嵌到 EXE。
- 单 EXE 目录无需复制 Assets、README.txt、CHANGELOG.md 或 LICENSE_NOTICES.txt。
- QQ 反馈群直达配置：Assets\QQ_GROUP_JOIN_URL.txt。
- 缺失二维码不会影响运行；QQ 直达链接未配置时只复制群号，不调用 mqqapi。

公开说明：Portable 包会携带 LICENSE_NOTICES.txt；关于页“许可证”按钮也可直接查看相关说明。
