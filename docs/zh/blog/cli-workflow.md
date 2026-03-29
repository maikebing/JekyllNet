---
title: "CLI 与开发工作流"
description: "说明 build、watch、serve、打包与 CI 如何在 JekyllNet 中协同工作。"
permalink: /zh/blog/cli-workflow/
lang: "zh-CN"
nav_key: "blog"
---
JekyllNet 现在已经具备更完整的命令行工作流。对一个静态站点生成器而言，这很重要，因为只有本地迭代、预览、打包和 CI 能顺畅衔接，整个工具链才真正可用。

## 核心命令

```powershell
dotnet run --project .\JekyllNet.Cli -- build --source .\sample-site
dotnet run --project .\JekyllNet.Cli -- watch --source .\docs
dotnet run --project .\JekyllNet.Cli -- serve --source .\docs --port 5055
```

- `build` 是确定性的生成步骤。
- `watch` 适合内容和模板的持续迭代。
- `serve` 适合提供稳定的本地 HTTP 预览地址。

## 结构化日志输出

最近的改进让 CLI 输出更易读：

- **Emoji 状态标识**：`✅` 表示成功，`❌` 表示错误，`🚀` 表示启动，`👀` 表示监听，`📝` 表示变更
- **智能时间格式**：短耗时显示 `ms`，秒级显示 `s`，更长耗时显示 `mm:ss`
- **多行布局**：关键信息更容易快速扫读

输出示例：

```text
✅ Build complete: D:\projects\my-site (elapsed 00:00:02.542)
👀 Watching for changes in D:\projects\my-site
📝 Change detected: _posts\2024-01-01-post.md
✅ Rebuild complete (elapsed 00:00:01.234)
```

## 工具链与分发

仓库现在还包含：

- CLI 的 `dotnet tool` 打包元数据
- 可复用的 GitHub Action 构建入口
- 用于 CI、GitHub Pages、NuGet 发布和 release artifacts 的 GitHub Actions 工作流
- winget 打包模板
- README 中的安装与升级说明

## 可复用 GitHub Action

现在，其他仓库可以直接使用独立的 `JekyllNet/action` 构建 action，而不必重复手写相同的 workflow 步骤。

```yml
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

该 action 现在发布 `v2.5` 标签，并默认安装 JekyllNet `0.2.5`。

最常用的输入包括 `source`、`destination`、`drafts`、`future`、`unpublished`、`posts-per-page`、`dotnet-configuration`，以及可选的 artifact 上传控制项。

## NuGet 发布工作流

仓库中还包含 `.github/workflows/publish-dotnet-tool.yml`，用于将 CLI 作为 `JekyllNet` 全局工具包同时发布到 NuGet.org 和 GitHub Packages。

该工作流：

- 在 `v*` tag 上触发
- 也支持手动触发，并显式传入版本号
- 在打包前先执行 `dotnet test`
- 使用解析后的版本执行 `dotnet pack`，而不是依赖项目文件中的静态版本号
- 使用仓库 secret `NUGET_API_KEY` 推送到 NuGet.org
- 同时使用 `GITHUB_TOKEN` 推送相同包到 `https://nuget.pkg.github.com/JekyllNet/index.json`

## 一条实用的本地例行流程

1. 编辑站点时，先运行 `watch`。
2. 需要稳定预览地址时，再运行 `serve`。
3. 合入改动前，执行 `dotnet test .\JekyllNet.slnx`。
