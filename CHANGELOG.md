# CHANGELOG 📝

此文记录 `JekyllNet` 面向外部可感知的版本变化，取法 `Keep a Changelog`，并尽量聚焦“用户真正会感受到的改动”。

## 🌟 [0.1.0] - 2026-03-25

首发里程碑版本。

### ✨ Added

- ✨ 新增 `build`、`watch`、`serve` 三条 CLI 工作流
- ✨ 新增 `docs` 与 `sample-site` 的站点构建回归测试
- ✨ 新增中英双语 docs 站点结构、博客栏目与新闻栏目
- ✨ 新增 `dotnet tool` 打包元数据
- ✨ 新增 GitHub Actions 构建与发布样例
- ✨ 新增 `winget` 模板与提交流程说明
- ✨ 新增 AI 辅助翻译基础管线
- ✨ 新增 OpenAI、DeepSeek、Ollama 与 OpenAI-compatible 第三方翻译接入

### 🛠️ Changed

- 🛠️ 补齐 `_config.yml` 常用站点级选项与 site 级 permalink fallback
- 🛠️ 强化 Liquid 控制流，补上 `capture`、`unless`、`case/when`、`contains`
- 🛠️ 强化 `for` 语义，支持 `limit / offset / reversed` 与 `forloop` 元数据
- 🛠️ 修正 `assign` 作用域、`include` 时机与嵌套块稳定性
- 🛠️ 接通 `drafts / future / unpublished`
- 🛠️ 强化 pagination，支持 `pagination.per_page`、`pagination.path` 与单页禁用
- 🛠️ 补齐一批高价值 filters：`relative_url`、`absolute_url`、`markdownify`、`where`、`sort`、`map`、`compact`、`jsonify`、`slugify`
- 🛠️ 引入静态文件 front matter / defaults 与 `excerpt_separator`
- 🛠️ 接入 AI 翻译缓存、增量翻译与 glossary

### 📚 Docs

- 📚 重写 `README.md`，补齐安装、开发、打包、AI 翻译与限制说明
- 📚 重构 `ROADMAP.md`，将 Phase 0 到 4 的状态收口
- 📚 建立 `jekyllnet.help` 对应的 docs 域名配置与 `CNAME`
- 📚 将 docs 页面扩写为首页、导航、快速开始、部署、兼容性、关于、博客、新闻的完整体系
- 📚 新增首发新闻与多篇专题博客

### ✅ Verified

- ✅ `dotnet test .\JekyllNet.slnx`
- ✅ `dotnet pack .\JekyllNet.Cli\JekyllNet.Cli.csproj -c Release`
- ✅ `serve` 本地预览链路可用

## 🔭 Next

- 💡 继续补齐更细的 Liquid / Pagination 边角兼容
- 💡 推进 VS Code / Visual Studio 预览与构建体验
- 💡 完善模板初始化、模板分发与更多部署配方
