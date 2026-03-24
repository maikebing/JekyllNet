# Jekyll.Net Roadmap

`Jekyll.Net` 的目标不是做一个“能生成 HTML 就算完成”的静态站点生成器，而是逐步补齐 **Jekyll / GitHub Pages 常用行为**，同时保持 .NET 代码库可维护、可测试、可迭代。

这次重新梳理路线图，核心不是再加更多想法，而是把事情分清楚：

1. 什么是必须先补齐的核心兼容闭环
2. 什么是能显著提升开发效率的工程化工作
3. 什么是值得保留、但不应该打断主线的远期探索

参考基线主要来自 Jekyll 官方文档中对 front matter、variables、collections、includes、rendering process 等行为定义：

- Front Matter Defaults: https://jekyllrb.com/docs/configuration/front-matter-defaults/
- Front Matter: https://jekyllrb.com/docs/front-matter/
- Variables: https://jekyllrb.com/docs/variables/
- Includes: https://jekyllrb.com/docs/includes/
- Collections: https://jekyllrb.com/docs/collections/
- Rendering Process: https://jekyllrb.com/docs/rendering-process/

## 当前基线

当前仓库已经验证可用的能力：

- `build` 命令可生成 `sample-site` 与 `docs`
- `_config.yml`
- YAML Front Matter
- Markdown 转 HTML
- `_layouts` 与嵌套 layout
- `_includes`
- `_data`
- `_posts`
- `collections`
- `tags/categories`
- 一批基础 Liquid 标签与 filters
- Sass/SCSS 编译
- 静态资源复制到 `_site`
- `_config.yml defaults` 的基础支持
  - 支持 `path`
  - 支持 `type`
  - 支持简单 `*` glob
  - front matter 显式值优先于 defaults

当前代码里已经预留、但还没有真正接到构建流程上的能力：

- `IncludeDrafts`
- `IncludeFuture`
- `IncludeUnpublished`
- `PostsPerPage`

## 这次路线图调整的结论

### 1. 主线要从“功能罗列”改成“兼容闭环”

接下来最重要的不是继续扩很多外围能力，而是先把下面这条主线做完整：

1. 建立回归基线
2. 补稳 Liquid 语义
3. 补齐站点内容语义
4. 扩展高价值 filters
5. 输出清晰的 GitHub Pages 兼容边界

### 2. 测试要前移，而不是放到最后

以前的路线图把测试放在比较后面，但从当前仓库状态看，后面几项核心工作都要改渲染器和构建流程。  
如果没有固定回归样本，越往后改，风险越大。

所以新的顺序应该是：

1. 先把 `docs` 和 `sample-site` 固定成回归样本
2. 再动 `assign` / `include` / `if` / `for` / permalink

### 3. 远期想法保留，但不进入核心关键路径

下面这些方向都值得保留：

- `serve/watch`
- `dotnet tool`
- GitHub Action
- `winget`
- VS Code / VS 预览体验
- `new -t` 模版初始化
- 模版商店
- 文档编辑器 + AI 翻译
- `md/pdf/word` 双向转换

但它们不应该打断当前“先把 Jekyll/GitHub Pages 兼容主干补稳”的主线。

## 核心路线图

### Phase 0: 回归基线先立住

目标：在继续补兼容细节前，先让后续改动变得可验证。

- [x] 增加自动化测试项目
- [x] 为 `sample-site` 增加 golden output / snapshot fixture
- [x] 为 `docs` 增加 golden output / snapshot fixture
- [x] 增加面向 Liquid 语义的小型 fixture
  - `assign`
  - 条件块中的 `include`
  - 嵌套 `if/for`
  - nested `index.md` permalink
- [ ] 增加固定回归命令与文档

完成标准：

- 每次改渲染器后，都能快速知道 `docs` / `sample-site` 有没有回归
- 后续 Phase 1 和 Phase 2 的改动可以放心推进

### Phase 1: Liquid 语义校正

目标：先把模板引擎从“能跑一部分模板”提升到“能稳定支撑文档站与简单主题”。

- [ ] 重做 `assign` 作用域
- [ ] 修正 `include` 在条件块中的展开顺序
- [x] 让 `if/for` 支持更稳定的嵌套块处理
- [x] 增加 `capture`
- [x] 增加 `unless`
- [x] 增加 `case/when`
- [x] 增加 `contains`

完成标准：

- `docs` 站点中的模板不再依赖规避性写法
- `include` 不会在不应渲染时提前展开
- 复杂块结构可以稳定通过 fixture

### Phase 2: 站点与内容语义补齐

目标：让真实站点更容易平移到 `Jekyll.Net`。

- [x] 修正 nested `index.md` 的默认 permalink 推导
- [x] 接通 `drafts`
- [x] 接通 `future`
- [x] 接通 `unpublished`
- [ ] 支持 excerpts / `excerpt_separator`
- [ ] 补齐 static files 的 front matter 与 defaults
- [ ] 补齐更多 `_config.yml` 站点级选项到构建流程
- [ ] 评估并接通 pagination

完成标准：

- 页面与文章的默认 URL 行为更接近 Jekyll
- 文章发布状态控制不再只是选项占位
- 内容建模能力足够覆盖中小型真实站点

### Phase 3: 高价值 Filters 补齐

目标：减少现有 Jekyll 模版迁移时的改动量。

- [x] `relative_url`
- [x] `absolute_url`
- [x] `markdownify`
- [x] `replace_first`
- [x] `where`
- [x] `sort`
- [x] `map`
- [x] `compact`
- [x] `jsonify`
- [x] `slugify`

建议顺序：

1. URL 相关 filters
2. 集合类 filters
3. 内容类 filters

完成标准：

- 现有模板里对 `prepend: site.baseurl` 之类的替代写法可以开始减少
- 一批常见主题片段可以更低成本迁移

### Phase 4: GitHub Pages 兼容层明确化

目标：把“兼容 GitHub Pages”从经验判断变成可说明、可验证的边界。

- [ ] 整理 GitHub Pages 常用特性兼容矩阵
- [ ] 标记“已兼容 / 部分兼容 / 未兼容”
- [ ] 增加更接近真实 Pages 站点的 fixture
- [ ] 对 `docs`、`sample-site`、最小 blog fixture 做固定回归
- [ ] 在文档中公开当前兼容边界与已知限制

完成标准：

- 外部使用者能快速判断项目是否适合自己的站点
- 我们自己也能明确知道下一步是在补什么缺口

## 第二优先级：工程化与开发体验

这一层很重要，但应该建立在核心兼容主线逐步稳定的基础上。

### DevEx 方向

- [ ] 增加 `serve/watch`
- [ ] 在 CLI 中暴露 `drafts / future / unpublished / pagination` 相关开关
- [ ] 增加固定回归命令
- [ ] 支持 `dotnet tool`
- [ ] 提供 GitHub Action 构建样例

### Distribution 方向

- [ ] 支持 `winget`
- [ ] 补安装与升级文档

## 远期探索 Backlog

这些方向可以继续保留，但建议明确标成“远期探索”，不要和核心兼容任务混在一个优先级里：

- [ ] VS Code 本地预览体验增强
- [ ] Visual Studio 预览/构建期集成
- [ ] `new -t` 模版初始化
- [ ] 模版仓库同步与模版商店
- [ ] 文档编辑器与 AI 翻译工作流
- [ ] `md -> pdf`
- [ ] `md -> word`
- [ ] `pdf -> md`
- [ ] `word -> md`
- [ ] 图片抽取与 OCR 支持

## 现在最该做的事

如果只看接下来一到两轮迭代，优先级建议明确收敛到这 5 件事：

1. 建立 `docs` + `sample-site` 的 golden output 回归基线
2. 重做 `assign` 作用域与 `include` 渲染顺序
3. 稳定 `if/for` 嵌套块处理，并补 `capture / unless / case / contains`
4. 修正 nested `index.md` permalink，并接通 `drafts / future / unpublished`
5. 补 `relative_url / absolute_url / markdownify`

一句话总结：

先把 **回归基线 + Liquid 语义 + 内容语义 + 核心 filters** 这条主干打通，再考虑安装分发、编辑器、模版生态和格式转换。
