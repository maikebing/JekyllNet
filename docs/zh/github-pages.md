---
title: "站点部署"
description: "以 docs 为 GitHub Pages 源站，或于 CI 中行其同法。"
permalink: /zh/github-pages/
lang: "zh-CN"
nav_key: "docs"
---
# 站点部署

今仓库之 `docs`，已成一套可用于 GitHub Pages 之源站结构。其既为对外文档，亦为吾之真实构建 fixture 之一。

## docs 之分工

其内大略如下：

- `docs/_config.yml`：主站配置、locales、导航文辞与 defaults
- `docs/_layouts`、`docs/_includes`：页面外壳
- `docs/assets`：样式与品牌资源
- `docs/zh`、`docs/en`：中英文镜像内容

## 直用 GitHub Pages

若欲以 GitHub 直接发布此源站，可设为：

`Deploy from a branch` -> `main` -> `/docs`

若欲更正其域，可并设自定义域名 `jekyllnet.help`。今仓库中亦已置 `docs/CNAME` 以备之。

## 关于域名配置

今配置以：

```yml
url: https://jekyllnet.help
baseurl: ""
```

既用独立域名，则 `baseurl` 宜为空。若后日域名有迁，请同步改此二项，以免生成链接失其所归。

## 何时改用 Actions

若君欲令构建可审、可控、可固其版本，则宜用仓库中可复用之 JekyllNet build action。此法尤适于：

- 固定 CI 中之 .NET SDK 版本
- 发布生成之产物，而非原始源目录
- 使站点构建与打包发布共归一套流程

最小示例如下：

```yml
jobs:
  build:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4
      - uses: JekyllNet/JekyllNet@main
        with:
          source: ./docs
          destination: ./artifacts/docs-site
          upload-artifact: "true"
          artifact-name: docs-site
```

今仓库尚未另发 action 版本 tag，故示例暂用 `@main`；待 release tag 可用时，再改钉其定版。

## 仓库内置之 Pages workflow

今仓库亦已具 `.github/workflows/github-pages.yml`。

其所行者：

- 以本仓库内之 JekyllNet action 构建 `./docs`
- 将 `./artifacts/docs-site` 上传为 GitHub Pages artifact
- 于推送至 `main` 时正式发布
- 于 PR 中只做构建校验，不实际部署

其触发范围含 `docs/**`、`JekyllNet.Cli/**`、`JekyllNet.Core/**`、`action.yml`、`JekyllNet.slnx` 与工作流自身；故文档之改，或生成器之改，皆可使 Pages 站点随之更新。

更详之命令行与自动化说明，可参 [CLI 与开发工作流](/zh/blog/cli-workflow/)。
---
