# Jekyll.Net

一个用 C# 编写的 Jekyll 风格静态站点生成器，目标是逐步靠近 GitHub Pages 行为。

路线图见：

`ROADMAP.md`

## 当前能力

- `.NET 10`
- `build` 命令
- `_config.yml`
- YAML Front Matter
- Markdown 转 HTML
- `_layouts` 与嵌套 layout
- `_includes`
- `_data`
- `_posts`
- `collections`
- `tags/categories`
- 基础 Liquid 标签与常见 filters
- `drafts / future / unpublished` 开关
- `_config.yml defaults` 基础支持
- Sass/SCSS 编译
- 静态资源复制到 `_site`

## 快速开始

在仓库根目录执行：

`dotnet run --project .\JekyllNet.Cli -- build --source .\sample-site`

如需把草稿、未来文章和未发布内容一起包含进来：

`dotnet run --project .\JekyllNet.Cli -- build --source .\sample-site --drafts --future --unpublished`

生成结果位于：

`sample-site\_site`

## 回归命令

运行完整测试与 snapshot 回归：

`dotnet test .\JekyllNet.slnx`

## 当前限制

当前还是增强中的兼容层，暂未完整支持：

- pagination
- 更完整 Liquid 语法与 filters 全覆盖
- `assign` 作用域 / include 渲染顺序等更完整 Liquid 语义
- excerpts
- data JSON 结构化解析增强
- Sass 管道与 Jekyll 细节完全对齐
- GitHub Pages 固定版本与插件行为 1:1 兼容
