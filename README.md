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
- `excerpt_separator`
- static files 的 front matter / defaults
- basic pagination
- `_config.yml defaults` 基础支持
- Sass/SCSS 编译
- 静态资源复制到 `_site`
- AI 驱动的多语言内容翻译基础管线
- OpenAI / DeepSeek / Ollama 的 OpenAI-compatible 翻译接入

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

## AI 翻译配置

在 `_config.yml` 中配置 `ai.translate.targets` 后，构建时会为 Markdown 内容自动生成目标语言页面。

```yml
lang: zh-CN
ai:
  provider: openai
  model: gpt-5-mini
  api_key_env: OPENAI_API_KEY
  translate:
    targets:
      - en
      - fr
    front_matter_keys:
      - title
      - description
```

内置快捷 provider：

- `openai`
- `deepseek`
- `ollama`

也支持任意 OpenAI-compatible 第三方服务商，只要配置：

```yml
ai:
  provider: siliconflow
  base_url: https://api.example.com
  model: your-model
  api_key_env: THIRD_PARTY_API_KEY
  translate:
    targets: [fr, ja]
```

默认行为：

- 只自动翻译 Markdown 内容文件
- 自动翻译 `title`，可通过 `ai.translate.front_matter_keys` 扩展
- 输出 URL 会自动按目标语言前缀生成，例如 `/fr/.../`
- 页脚法务标签会按页面语言切换；对非中英文目标语言，会优先尝试用 AI 自动翻译标签
- 会自动为同一路径的多语言页面生成 `translation_links`

## 当前限制

当前还是增强中的兼容层，暂未完整支持：

- 更完整的 pagination 兼容行为
- 更完整 Liquid 语法与 filters 全覆盖
- `assign` 作用域 / include 渲染顺序等更完整 Liquid 语义
- data JSON 结构化解析增强
- Sass 管道与 Jekyll 细节完全对齐
- GitHub Pages 固定版本与插件行为 1:1 兼容
