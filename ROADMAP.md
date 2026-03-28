# JekyllNet Roadmap 🗺️

`JekyllNet` 的目标，不是做一个“能生成 HTML 就算完成”的静态站点生成器，而是逐步补齐 **Jekyll / GitHub Pages 常用行为**，同时保持 .NET 代码库可维护、可测试、可迭代。

这份路线图现在只保留两类信息：

1. ✨ Phase 0 到 4 的收口状态
2. 🔭 后续不再阻塞主线的持续改进方向

参考基线：

- 📚 Front Matter Defaults: https://jekyllrb.com/docs/configuration/front-matter-defaults/
- 📚 Front Matter: https://jekyllrb.com/docs/front-matter/
- 📚 Variables: https://jekyllrb.com/docs/variables/
- 📚 Includes: https://jekyllrb.com/docs/includes/
- 📚 Collections: https://jekyllrb.com/docs/collections/
- 📚 Rendering Process: https://jekyllrb.com/docs/rendering-process/

## 🚀 当前基线

截至 2026-03-25，当前仓库已经稳定具备：

- ✨ `build` 命令可生成 `sample-site` 与 `docs`
- ✨ `_config.yml`
- ✨ YAML Front Matter
- ✨ Markdown 转 HTML
- ✨ `_layouts` 与嵌套 layout
- ✨ `_includes`
- ✨ `_data`
- ✨ `_posts`
- ✨ `collections`
- ✨ `tags/categories`
- ✨ 一批基础 Liquid 标签与常见 filters
- ✨ Sass/SCSS 编译
- ✨ 静态资源复制到 `_site`
- ✨ `_config.yml defaults`
- ✨ `drafts / future / unpublished`
- ✨ `excerpt_separator`
- ✨ static files front matter / defaults
- ✨ pagination 基线能力与结构化 `pagination.*` 配置
- ✨ site 级 permalink fallback
- ✨ `relative_url / absolute_url / markdownify / where / sort / map / compact / jsonify / slugify`
- ✨ `docs` 与 `sample-site` golden output / snapshot 回归
- ✨ 多语言页面 `translation_links` 自动生成
- ✨ AI 翻译基础管线
- ✨ OpenAI / DeepSeek / Ollama / 任意 OpenAI-compatible 第三方 provider
- ✨ AI 翻译缓存、增量翻译、glossary
- ✨ 兼容矩阵与公开边界说明页

验证命令：

`dotnet test .\JekyllNet.slnx`

## 🧪 Phase 0：回归基线

状态：`已完成并关闭`

- [x] ✅ 自动化测试项目
- [x] ✅ `sample-site` golden output / snapshot fixture
- [x] ✅ `docs` golden output / snapshot fixture
- [x] ✅ 面向 Liquid 语义的小型 fixture
- [x] ✅ 固定回归命令与文档

结论：

- ✅ Phase 0 已收口。
- ✅ 后续只按需要补 fixture，不再单独作为一个阶段维护。

## 💧 Phase 1：Liquid 语义校正

状态：`已完成并关闭`

- [x] ✅ `assign` 作用域基础收紧
- [x] ✅ `include` 在未命中分支中不会提前展开
- [x] ✅ `include` 在循环中按当前迭代值渲染
- [x] ✅ `if/for` 嵌套块处理更稳定
- [x] ✅ `capture`
- [x] ✅ `unless`
- [x] ✅ `case/when`
- [x] ✅ `contains`
- [x] ✅ `forloop` 元数据
- [x] ✅ `for` 的 `limit / offset / reversed`

结论：

- ✅ Phase 1 的主线兼容缺口已补上。
- ✅ 后续如有 Liquid 边角差异，按“具体语义 + fixture”方式零散进入 backlog。

## 🧱 Phase 2：站点与内容语义

状态：`已完成并关闭`

- [x] ✅ nested `index.md` 默认 permalink 推导
- [x] ✅ `drafts`
- [x] ✅ `future`
- [x] ✅ `unpublished`
- [x] ✅ excerpts / `excerpt_separator`
- [x] ✅ static files front matter / defaults
- [x] ✅ pagination 基线接通
- [x] ✅ `pagination.per_page`
- [x] ✅ `pagination.path`
- [x] ✅ 单页禁用 pagination
- [x] ✅ include / exclude 进入 skip 逻辑
- [x] ✅ site 级 permalink fallback
- [x] ✅ 一批高价值 `_config.yml` 站点选项进入真实构建流程

结论：

- ✅ Phase 2 不再保留“继续补更多 `_config.yml` 选项”这种无限扩张表述。
- ✅ 后续只有在出现明确、可验证、值得支持的站点语义缺口时，才继续增补。

## 🧰 Phase 3：高价值 Filters

状态：`已完成并关闭`

- [x] ✅ `relative_url`
- [x] ✅ `absolute_url`
- [x] ✅ `markdownify`
- [x] ✅ `replace_first`
- [x] ✅ `where`
- [x] ✅ `sort`
- [x] ✅ `map`
- [x] ✅ `compact`
- [x] ✅ `jsonify`
- [x] ✅ `slugify`

结论：

- ✅ Phase 3 已收口。
- ✅ 后续 filter 只作为主题兼容补丁零散进入，不再维持一个独立大阶段。

## 🌐 Phase 4：GitHub Pages 兼容边界明确化

状态：`已完成并关闭`

- [x] ✅ 整理 GitHub Pages 常用特性的兼容矩阵
- [x] ✅ 明确标记“已兼容 / 部分兼容 / 未兼容”
- [x] ✅ 公开当前兼容边界与已知限制
- [x] ✅ 用 `docs`、`sample-site` 与聚焦 fixture 形成可回归验证基线

当前产物：

- 📄 `docs/en/compatibility/`
- 📄 `docs/zh/compatibility/`
- 📄 `README.md` 与 docs 之间统一后的兼容口径

结论：

- ✅ Phase 4 的第一版边界说明已经落地。
- ✅ 后续只需要持续维护矩阵，不再把“先把边界写出来”当作待完成阶段。

## 🗃️ 已废弃的写法

以下内容不是“功能废弃”，而是“路线图写法废弃”：

- [x] ✅ “补更多 `_config.yml` 选项”这种无限扩张项
- [x] ✅ 把已经完成的 snapshot 回归在多个 phase 里重复记账
- [x] ✅ 把 DevEx、分发、编辑器生态与核心兼容主线混在一个优先级里

## 🧭 不再阻塞主线的事项

这些方向仍然有价值，但不再属于 Phase 0 到 4 的闭环 gate。

### 🛠️ DevEx / Distribution

- [x] ✅ `serve/watch`
- [x] ✅ 在 CLI 中显式暴露更多开关
- [x] ✅ `dotnet tool`
- [x] ✅ GitHub Action 构建样例
- [x] ✅ `winget`
- [x] ✅ 安装与升级文档

### 🔭 长期探索

- [ ] 💡 VS Code 中新增一个扩展， 辅助项目使用jekyllnet ，并能支持本地预览体验
- [ ] 实现GitHub Action 构建， 发布Github Action 
- [ ]  弄一些模版， 测试生成。 
- [ ] 💡 Visual Studio 预览 / 构建期集成
- [ ] 💡 `new -t` 模板初始化
- [ ] 💡 模板仓库同步与模板市场
- [ ] 💡 文档编辑器与 AI 翻译工作流
- [ ] 💡 `md/pdf/word` 双向转换
- [ ] 💡 图片抽取与 OCR 支持

### 📌 建议执行顺序

为了避免方向过多而失焦，建议按下面顺序推进：

1. 先做 `GitHub Action` 产品化：把现有 workflow 样例沉淀成可复用的构建 / 发布 action。
2. 再做 `new -t` 模板初始化：先把“如何快速起一个规范站点”标准化，再谈更丰富生态。
3. 然后做 VS Code 本地预览体验：围绕标准模板把预览、构建、调试链路串顺。
4. 接着做 Visual Studio 预览 / 构建期集成：复用前面已经稳定的命令与模板能力。
5. 再推进模板仓库同步、模板市场、文档编辑器与 AI 翻译工作流：这些都更依赖前面的模板与预览基础。
6. `md/pdf/word` 双向转换与 OCR 保持在后置探索位：它们更像内容工具链扩展，不应早于主产品入口建设。

执行原则：

- 兼容性工作继续保持“具体缺口 + fixture / test + docs 同步”模式，不再开新的大 phase。
- 产品化工作优先交付“一个清晰入口 + 一套可复用样板 + 一篇可跟随文档”，避免只做底层能力。

## 🪜 下一阶段怎么推进

接下来不再以 “Phase 0 到 4 还没做完” 的方式推进，而是按下面的节奏继续：

1. ✨ 只补明确、可回归的兼容缺口
2. 📄 对 `README`、docs compatibility page、snapshot fixture 保持同步
3. 🛠️ 把 DevEx、分发和编辑器体验作为独立产品化方向推进

一句话总结：

**Phase 0 到 4 的主线闭环已经打通。后续工作不再是“把旧 phase 补完”，而是围绕兼容补丁、工程体验和分发能力继续演进。**
