# JekyllNet

![JekyllNet social preview](docs/assets/brand/social-preview.png)

![JekyllNet lockup](docs/assets/brand/jekyll-net-lockup.svg)

**Jekyll-style Static Site Generator for .NET**  
**用 C# 重建 Jekyll / GitHub Pages 风格站点生成体验**

JekyllNet is a .NET 10 static site generator for teams that want Jekyll-shaped workflows without leaving the C# ecosystem.  
JekyllNet 是一个基于 .NET 10 的静态站点生成器，面向希望保留 Jekyll / GitHub Pages 使用心智、同时在 C# 生态内完成构建与发布的团队。

[![Repo](https://img.shields.io/badge/repo-JekyllNet%2FJekyllNet-111827?style=flat-square)](https://github.com/JekyllNet/JekyllNet)
[![Website](https://img.shields.io/badge/site-jekyllnet.help-0f766e?style=flat-square)](https://jekyllnet.help)
[![NuGet](https://img.shields.io/nuget/v/JekyllNet?style=flat-square&label=NuGet)](https://www.nuget.org/packages/JekyllNet)
[![Downloads](https://img.shields.io/nuget/dt/JekyllNet?style=flat-square&label=Downloads)](https://www.nuget.org/packages/JekyllNet)
[![Action v2.5](https://img.shields.io/badge/action-v2.5-2563eb?style=flat-square)](https://github.com/JekyllNet/action/tree/v2.5)
[![CI](https://img.shields.io/github/actions/workflow/status/JekyllNet/JekyllNet/ci.yml?branch=main&label=CI&style=flat-square)](https://github.com/JekyllNet/JekyllNet/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/JekyllNet/JekyllNet?style=flat-square)](LICENSE)

## Intro | 简介

**EN**  
JekyllNet rebuilds practical Jekyll and GitHub Pages behaviors in C#: front matter, layouts, includes, collections, posts, pagination, Sass, multilingual docs, local preview, dotnet tool distribution, and reusable GitHub Actions.

**中文**  
JekyllNet 用 C# 重建 Jekyll / GitHub Pages 的常用行为，覆盖 front matter、layout、include、collections、posts、pagination、Sass、多语文档、本地预览、dotnet tool 分发与可复用 GitHub Action。

## Quick Facts | 当前信息

| Item | Value |
| --- | --- |
| Website | [jekyllnet.help](https://jekyllnet.help) |
| Docs | [docs/](docs/) |
| Roadmap | [ROADMAP.md](ROADMAP.md) |
| Changelog | [CHANGELOG.md](CHANGELOG.md) |
| NuGet latest | [`0.2.5`](https://www.nuget.org/packages/JekyllNet/0.2.5) |
| GitHub Action | [`JekyllNet/action@v2.5`](https://github.com/JekyllNet/action/tree/v2.5) |
| Default Action CLI version | `0.2.5` |
| Runtime | `.NET 10` |

## Highlights | 亮点

- `build`、`watch`、`serve` 三条 CLI 工作流
- `_config.yml`、YAML Front Matter、Markdown、layouts、includes、collections、posts、tags、categories
- Pagination、excerpt、draft/future/unpublished、static file front matter / defaults
- Sass / SCSS 编译
- AI 多语言翻译管线，支持 OpenAI、DeepSeek、Ollama 与 OpenAI-compatible 服务
- `dotnet tool` 包分发
- 可复用的 `JekyllNet/action@v2.5`
- 中英文文档站、博客与新闻栏目

## Tested Themes | 已测试主题

以下主题已进入我们当前的构建与兼容性验证范围：

- [just-the-docs](https://github.com/JekyllNet/just-the-docs)
- [minimal-mistakes](https://github.com/JekyllNet/minimal-mistakes)
- [al-folio](https://github.com/JekyllNet/al-folio)
- [jekyll-theme-chirpy](https://github.com/JekyllNet/jekyll-theme-chirpy)
- [jekyll-TeXt-theme](https://github.com/kitian616/jekyll-TeXt-theme)
- [jekyll-theme-lumen](https://github.com/JekyllNet/jekyll-theme-lumen)

## Quick Start | 快速开始

### Option 1: Use the NuGet tool | 方式一：直接使用 NuGet 工具

```bash
# install
 dotnet tool install --global JekyllNet --version 0.2.5

# build
 jekyllnet build --source ./my-site

# preview
 jekyllnet serve --source ./my-site --port 5055
```

### Option 2: Run from this repo | 方式二：从仓库源码直接运行

```powershell
dotnet run --project .\JekyllNet.Cli -- build --source .\sample-site
dotnet run --project .\JekyllNet.Cli -- watch --source .\docs
dotnet run --project .\JekyllNet.Cli -- serve --source .\docs --port 5055
```

## Documentation | 文档入口

- [docs/zh/](docs/zh/)：中文文档首页
- [docs/en/](docs/en/)：English docs home
- [docs/zh/blog/complete-usage-guide.md](docs/zh/blog/complete-usage-guide.md)：完整使用说明
- [docs/en/blog/complete-usage-guide.md](docs/en/blog/complete-usage-guide.md)：Complete usage guide
- [docs/zh/news/v0-2-5.md](docs/zh/news/v0-2-5.md)：v0.2.5 发布稿
- [docs/en/news/v0-2-5.md](docs/en/news/v0-2-5.md)：v0.2.5 release note

## Action | GitHub Action

`JekyllNet/action@v2.5` 已发布，默认安装 NuGet 上的 `JekyllNet 0.2.5`。

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
      - uses: actions/checkout@v5
      - uses: JekyllNet/action@v2.5
        with:
          source: ./docs
          destination: ./artifacts/docs-site
          upload-artifact: "true"
          artifact-name: docs-site
```

## AI Translation | AI 翻译

在 `_config.yml` 中配置 `ai.translate.targets` 后，构建时会自动生成目标语言页面，并支持缓存、术语表与增量复用。

```yml
lang: zh-CN
ai:
  provider: openai
  model: gpt-5-mini
  api_key_env: OPENAI_API_KEY
  translate:
    targets: [en, fr]
    front_matter_keys: [title, description]
    glossary: _i18n/glossary.yml
    cache_path: .jekyllnet/translation-cache.json
```

## Development | 开发与验证

常用命令：

```powershell
dotnet test .\JekyllNet.slnx
dotnet pack .\JekyllNet.Cli\JekyllNet.Cli.csproj -c Release
dotnet run --project .\scripts\JekyllNet.ReleaseTool -- test-theme-matrix --max-parallelism 5
```

说明：

- 主题矩阵会覆盖 `just-the-docs`、`minimal-mistakes`、`al-folio`、`jekyll-theme-chirpy`、`jekyll-TeXt-theme`、`jekyll-theme-lumen`
- `serve` / `watch` 已支持更易读的结构化日志输出
- 文档站 `docs/` 同时也是构建回归 fixture 之一

## Distribution | 分发

仓库当前提供：

- `.github/workflows/ci.yml`
- `.github/workflows/github-pages.yml`
- `.github/workflows/publish-dotnet-tool.yml`
- `.github/workflows/release-artifacts.yml`
- [packaging/winget/README.md](packaging/winget/README.md)

这些工作流用于测试、Pages 发布、NuGet 发布、Release 资产打包与 winget 清单生成。

## Limits | 当前限制

当前仍在持续增强的范围包括：

- 更细的 Liquid 边角语义对齐
- 更多 pagination 兼容细节
- 更广的 GitHub Pages / Jekyll 插件生态兼容
- 更完整的数据解析与主题细节对齐

## Collaboration | 协作说明

本仓库内自动化与工具实现遵循以下约束：

- 仅使用 `C#` 与 `.NET 10`
- 不使用 PowerShell、Python、Node.js、Shell 作为仓库工具实现语言
- 如需偏离约束，先在 PR 或文档中说明原因并获得维护者批准

## Links | 相关链接

- Source: [JekyllNet/JekyllNet](https://github.com/JekyllNet/JekyllNet)
- Action: [JekyllNet/action](https://github.com/JekyllNet/action)
- Website: [jekyllnet.help](https://jekyllnet.help)
- NuGet: [JekyllNet](https://www.nuget.org/packages/JekyllNet)

Built with .NET 10 · GitHub Pages style workflows · MIT License
