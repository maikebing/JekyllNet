# CHANGELOG 📝

此文记录 `JekyllNet` 面向外部可感知的版本变化，取法 `Keep a Changelog`，并尽量聚焦“用户真正会感受到的改动”。

## 🌟 [0.1.0] - 2026-03-25

## 🌟 [0.2.5] - 2026-03-29

此版本基于 `v0.2.0` 之后的所有变更整理，重点在于主题兼容面扩张、配置解析增强、插件与主题支持提升，以及发布链路版本统一。

### ✨ Added

- ✨ 新增 `jekyll-theme-lumen` 主题子模块，并纳入主题矩阵与兼容性范围
- ✨ 新增 `jekyllnet-pages-subpath` 技能文档，沉淀 Pages 子路径构建经验
- ✨ 新增 `v0.2.5` 中英双语发布新闻稿

### 🛠️ Changed

- 🛠️ 增强主题支持与插件架构，提升跨主题渲染与构建稳定性
- 🛠️ `_config.yml` 加载后新增 `{{version}}` 占位符解析，兼容如 al-folio 等主题在 CDN URL 中的版本变量写法
- 🛠️ 更新多个主题子模块引用，收敛 Pages 子路径与工作流清理后的兼容行为
- 🛠️ 将 GitHub Action 默认 `tool-version` 提升到 `0.2.5`
- 🛠️ 将文档与主题工作流中的 `JekyllNet/action` 引用统一提升到 `v2.5`
- 🛠️ 将 CLI 包版本同步到 `0.2.5`

### 📚 Docs

- 📚 更新 `README.md` 与 profile 文档中的 NuGet / Action 版本信息
- 📚 更新 GitHub Pages、CLI 工作流、完整使用说明中的 Action 示例版本
- 📚 更新 docs 首页、导航、新闻索引中的“最新发布”入口，切换到 `v0.2.5`

### ✅ Verified

- ✅ `dotnet build .\JekyllNet.slnx`
- ✅ 核心测试集 64 项全部通过
- ✅ 主题子模块工作流中的 `JekyllNet/action` 版本引用已统一提升

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

## � [0.1.0-post] - 修复与改进

### 🛠️ Changed

- 🛠️ **Sass/SCSS 编译要求**：Sass 入口文件现在 **必须** 包含 YAML Front Matter（`---\n---`）头，以启用编译。这确保了与 Jekyll 的一致性。参考：[sample-site/assets/scss/site.scss](sample-site/assets/scss/site.scss)
- 🛠️ **CLI 日志体验改进**：所有 CLI 命令（`build`、`watch`、`serve`）现在输出更结构化、易读的日志：
  - 带 emoji 标记的 status 指示（✅ 成功、❌ 失败、🚀 启动、👀 监听、📝 更改）
  - 智能时间格式化（`ms` / `s` / `mm:ss`）
  - 多行格式化输出，便于快速扫描

示例输出：
```
✅ Build complete: D:\source\JekyllNet\artifacts\sites\sample-site (elapsed 00:00:02.542)
👀 Watching for changes in D:\source\JekyllNet\sample-site
📝 Change detected: _posts\2024-01-01-test.md
```

### ✅ Verified

- ✅ 所有 64 个单元测试通过
- ✅ `dotnet test .\JekyllNet.slnx` 无错误无警告
- ✅ 5 主题矩阵构建成功：`dotnet run --project .\scripts\JekyllNet.ReleaseTool -- test-theme-matrix --max-parallelism 5`

## �🔭 Next

- 💡 继续补齐更细的 Liquid / Pagination 边角兼容
- 💡 推进 VS Code / Visual Studio 预览与构建体验
- 💡 完善模板初始化、模板分发与更多部署配方
