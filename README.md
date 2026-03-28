<p align="center">
  <img src="docs/assets/brand/jekyll-net-lockup.svg" alt="JekyllNet" width="360">
</p>

# JekyllNet ✨

一个用 C# 编写的 Jekyll 风格静态站点生成器，目标是逐步靠近 GitHub Pages 行为。

路线图见：

`ROADMAP.md`

变更记录见：

`CHANGELOG.md`

GitHub 头像建议使用：

`docs/assets/brand/jekyll-net-avatar.svg`

## 🚀 当前能力

- ✨ `.NET 10`
- ✨ `build` 命令
- ✨ `_config.yml`
- ✨ YAML Front Matter
- ✨ Markdown 转 HTML
- ✨ `_layouts` 与嵌套 layout
- ✨ `_includes`
- ✨ `_data`
- ✨ `_posts`
- ✨ `collections`
- ✨ `tags/categories`
- ✨ 基础 Liquid 标签与常见 filters
- ✨ `drafts / future / unpublished` 开关
- ✨ `excerpt_separator`
- ✨ static files 的 front matter / defaults
- ✨ basic pagination
- ✨ `_config.yml defaults` 基础支持
- ✨ Sass/SCSS 编译
- ✨ 静态资源复制到 `_site`
- ✨ AI 驱动的多语言内容翻译基础管线
- ✨ OpenAI / DeepSeek / Ollama 的 OpenAI-compatible 翻译接入

## ⚡ 快速开始

在仓库根目录执行：

`dotnet run --project .\JekyllNet.Cli -- build --source .\sample-site`

如需把草稿、未来文章和未发布内容一起包含进来：

`dotnet run --project .\JekyllNet.Cli -- build --source .\sample-site --drafts --future --unpublished`

生成结果位于：

`sample-site\_site`

## 🛠️ 本地开发

启动本地静态站点服务并自动监听改动：

`dotnet run --project .\JekyllNet.Cli -- serve --source .\docs`

只监听改动并持续重建：

`dotnet run --project .\JekyllNet.Cli -- watch --source .\sample-site`

常用 CLI 开关：

- 🔧 `--drafts`
- 🔧 `--future`
- 🔧 `--unpublished`
- 🔧 `--posts-per-page <number>`
- 🔧 `serve` 命令额外支持 `--host <host>`、`--port <port>`、`--no-watch`

示例：

`dotnet run --project .\JekyllNet.Cli -- serve --source .\docs --port 5000`

## ✅ 回归命令

运行完整测试与 snapshot 回归：

`dotnet test .\JekyllNet.slnx`

## 📦 dotnet tool

仓库已经带上 `dotnet tool` 打包元数据，命令名为：

`jekyllnet`

本地打包：

`dotnet pack .\JekyllNet.Cli\JekyllNet.Cli.csproj -c Release`

从本地包目录全局安装：

`dotnet tool install --global JekyllNet.Tool --add-source .\artifacts\nupkg`

升级：

`dotnet tool update --global JekyllNet.Tool --add-source .\artifacts\nupkg`

卸载：

`dotnet tool uninstall --global JekyllNet.Tool`

安装后可直接执行：

`jekyllnet build --source .\sample-site`

## 🤖 GitHub Actions 与分发

仓库现在同时提供：

- 📄 根目录 `action.yml`，可直接作为可复用 GitHub Action 使用
- 📄 `.github/workflows/ci.yml`
- 📄 `.github/workflows/github-pages.yml`
- 📄 `.github/workflows/publish-dotnet-tool.yml`
- 📄 `.github/workflows/release-artifacts.yml`

用途：

- ⚙️ `action.yml`：在任意仓库中构建 JekyllNet 站点，并可选上传构建产物
- ⚙️ `ci.yml`：测试、通过 action 构建 `docs` / `sample-site`、打包 dotnet tool
- ⚙️ `github-pages.yml`：当 `docs` 或站点生成器相关代码变化时，构建 `docs` 并发布到 GitHub Pages
- ⚙️ `publish-dotnet-tool.yml`：将 `JekyllNet.Tool` 作为 dotnet tool 包发布到 NuGet
- ⚙️ `release-artifacts.yml`：生成 `nupkg` 与 Windows portable zip，便于 Release 和 `winget`

最小 workflow 示例：

```yml
name: build-site

on:
  push:
  pull_request:
  workflow_dispatch:

jobs:
  build:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - name: Build docs with JekyllNet
        uses: IoTSharp/JekyllNet@main
        with:
          source: ./docs
          destination: ./artifacts/docs-site
          upload-artifact: "true"
          artifact-name: docs-site
```

当前仓库尚未打出专门的 action tag，因此示例先使用 `@main`；待首个 action release 发布后，建议改为固定版本 tag。

如果是在本仓库发布 `docs` 到 GitHub Pages，可直接启用：

- `.github/workflows/github-pages.yml`

该 workflow 会：

- 在 `main` 分支收到 `docs/**`、`JekyllNet.Cli/**`、`JekyllNet.Core/**`、`action.yml` 或工作流自身变更时自动触发
- 在 PR 中执行构建校验，但不实际部署
- 在推送到 `main` 后把 `./artifacts/docs-site` 发布到 GitHub Pages

常用输入：

- `source`
- `destination`
- `drafts`
- `future`
- `unpublished`
- `posts-per-page`
- `upload-artifact`
- `artifact-name`

发布 dotnet tool 到 NuGet 可直接使用：

- `.github/workflows/publish-dotnet-tool.yml`

该 workflow 会：

- 在推送 `v*` tag 时自动触发，并以 tag 去掉前导 `v` 后的值作为包版本
- 在手动触发时使用输入的 `version`
- 先执行 `dotnet test`
- 再执行 `dotnet pack`，并同时发布到 `https://api.nuget.org/v3/index.json` 与 GitHub Packages
- 向 NuGet.org 发布时使用仓库 secret `NUGET_API_KEY`
- 向 GitHub Packages 发布时使用 `GITHUB_TOKEN`

示例：

- 推送 tag `v0.1.1`
- workflow 会把 `JekyllNet.Tool` `0.1.1` 同时发布到 NuGet.org 和 `https://nuget.pkg.github.com/<owner>/index.json`

## 🪄 winget

仓库已补上 `winget` 模板与提交流程说明：

- 📄 `packaging/winget/README.md`

当前状态：

- 📦 仓库已经具备 `winget` 清单模板和 Windows portable 发布物流程
- 📝 真正提交社区源前，还需要把 Release 资产 URL 和 SHA256 填入模板

## 🌍 AI 翻译配置

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
    glossary: _i18n/glossary.yml
    cache_path: .jekyllnet/translation-cache.json
```

内置快捷 provider：

- 🤖 `openai`
- 🤖 `deepseek`
- 🤖 `ollama`

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

- 🌐 只自动翻译 Markdown 内容文件
- 🌐 自动翻译 `title`，可通过 `ai.translate.front_matter_keys` 扩展
- 🌐 输出 URL 会自动按目标语言前缀生成，例如 `/fr/.../`
- 🌐 页脚法务标签会按页面语言切换；对非中英文目标语言，会优先尝试用 AI 自动翻译标签
- 🌐 会自动为同一路径的多语言页面生成 `translation_links`
- 🌐 默认启用翻译缓存，未变化的文本会直接复用，避免每次 build 全量请求模型
- 🌐 `ai.translate.cache_path` 可覆盖缓存文件位置，`ai.translate.cache: false` 可关闭缓存
- 🌐 `ai.translate.glossary` 可提供术语表，保证品牌词、专有名词、多语言固定译法更稳定

glossary 文件示例：

```yml
terms:
  JekyllNet: JekyllNet
  GitHub Pages:
    fr: Pages GitHub
    ja: GitHub Pages
```

## 🧭 当前限制

当前还是增强中的兼容层，暂未完整支持：

- ⏳ 更完整的 pagination 兼容行为
- ⏳ 更完整 Liquid 语法与 filters 全覆盖
- ⏳ `assign` 作用域 / include 渲染顺序等更完整 Liquid 语义
- ⏳ data JSON 结构化解析增强
- ⏳ Sass 管道与 Jekyll 细节完全对齐
- ⏳ GitHub Pages 固定版本与插件行为 1:1 兼容
