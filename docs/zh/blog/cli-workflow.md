---
title: "CLI 与开发工作流"
description: "述 build、watch、serve、打包与 CI 于 JekyllNet 中之衔接。"
permalink: /zh/blog/cli-workflow/
lang: "zh-CN"
nav_key: "blog"
---
# CLI 与开发工作流

吾今已有较完备之命令行工作流。盖静态生成器若仅能 build，而不能便于迭代、预览、打包与自动化，则其用未广。

## 核心三令

```powershell
dotnet run --project .\JekyllNet.Cli -- build --source .\sample-site
dotnet run --project .\JekyllNet.Cli -- watch --source .\docs
dotnet run --project .\JekyllNet.Cli -- serve --source .\docs --port 5055
```

- `build`：定式生成之步
- `watch`：适于文稿与模板反复修订
- `serve`：适于稳定本地预览

## 打包与分发

仓库今亦已具：

- `dotnet tool` 打包元数据
- 可复用之 GitHub Action 构建入口
- 用于 CI、GitHub Pages、NuGet 发布与 release artifacts 之 GitHub Actions 工作流
- winget 模板
- README 中之安装与升级说明

## 可复用之 GitHub Action

今仓库根目录已可直接作为构建 action 为他仓所用，不必每次手写同样之 workflow 细节。

```yml
jobs:
  build:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4
      - uses: IoTSharp/JekyllNet@main
        with:
          source: ./docs
          destination: ./artifacts/docs-site
          upload-artifact: "true"
          artifact-name: docs-site
```

今仓库尚未另发 action 版本 tag，故示例暂用 `@main`；待首个 action release 既成，宜改钉其固定 tag。

常用输入者，有 `source`、`destination`、`drafts`、`future`、`unpublished`、`posts-per-page`，以及可选之 artifact 上传配置。

## NuGet 发布工作流

仓库今亦已具 `.github/workflows/publish-dotnet-tool.yml`，可将 CLI 以 `JekyllNet.Tool` 之 dotnet tool 包同时发布到 NuGet.org 与 GitHub Packages。

其所行者：

- 于 `v*` tag 触发
- 亦可手动触发，并显式输入版本号
- 先行 `dotnet test`
- 再以解析后之版本执行 `dotnet pack`，不专赖项目文件中之固定版本字样
- 以仓库 secret `NUGET_API_KEY` 推送至 NuGet.org
- 并以 `GITHUB_TOKEN` 推送至 `https://nuget.pkg.github.com/<owner>/index.json`

## 一条实用之例行

1. 修站点时，常开 `watch`。
2. 欲得稳定预览地址时，再开 `serve`。
3. 提交前，行 `dotnet test .\JekyllNet.slnx`。
---
