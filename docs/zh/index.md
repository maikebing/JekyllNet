---
title: "Jekyll.Net 首页"
description: "一个用 C# 编写的 Jekyll 风格静态站点生成器，目标是逐步靠近 GitHub Pages 行为。"
permalink: /zh/
layout: structured
lang: "zh-CN"
nav_key: "docs"
hero:
  eyebrow: "GitHub Pages · Jekyll-compatible · .NET 10"
  title: "用 .NET 写一个越来越像 GitHub Pages 的静态站点生成器"
  lead: "Jekyll.Net 以 C# 和 .NET 10 为基础，正在补齐 GitHub Pages 常见工作流需要的 Front Matter、Layout、Include、Collection、Tag、Category 和 Sass 能力。"
  primary_label: "开始阅读"
  primary_url: /zh/navigation/
  secondary_label: "查看 sample-site"
  secondary_url: https://github.com/IoTSharp/Jekyll.Net/tree/main/sample-site
  tertiary_label: "阅读 README"
  tertiary_url: https://github.com/IoTSharp/Jekyll.Net/blob/main/README.md
  tags:
    - ".NET 10"
    - "GitHub Pages"
    - "Jekyll-style"
    - "Markdown + Sass"
  code_title: "Quick Start"
  code: |
    dotnet run --project .\JekyllNet.Cli -- build --source .\sample-site
  note_title: "一句话理解"
  note_text: "这个项目不是在包装现成的 Jekyll，而是在用 C# 逐步重建一条能服务 GitHub Pages 风格站点的静态生成链路。"
stats:
  - label: "运行时"
    value: ".NET 10"
  - label: "当前入口"
    value: "CLI build"
  - label: "站点特性"
    value: "Markdown + Liquid 子集"
  - label: "目标方向"
    value: "靠近 GitHub Pages"
sections:
  - title: "当前已经具备什么"
    description: "仓库当前实现已经覆盖一个 GitHub Pages 风格站点最核心的一批静态生成能力。"
    variant: "cards"
    columns: 3
    section_class: ""
    items:
      - title: "内容解析"
        description: "支持 _config.yml、YAML Front Matter、Markdown 到 HTML，以及基础的标签和过滤器。"
      - title: "模版能力"
        description: "支持 _layouts、嵌套 layout、_includes、_data，以及带输出的 collections。"
      - title: "站点资源"
        description: "支持 Sass/SCSS 编译，并把静态资源复制到 _site 输出目录。"
  - title: "推荐阅读入口"
    description: "如果你是第一次接触这个仓库，下面这几个入口最省时间。"
    variant: "quick-links"
    columns: 4
    section_class: ""
    items:
      - title: "文档导航"
        description: "按用途挑选你应该先读的页面。"
        url: /Jekyll.Net/zh/navigation/
      - title: "快速开始"
        description: "用 sample-site 跑出第一版静态输出。"
        url: /Jekyll.Net/zh/getting-started/
      - title: "GitHub Pages"
        description: "查看如何把当前 docs 目录直接挂到 GitHub Pages。"
        url: /Jekyll.Net/zh/github-pages/
      - title: "兼容性说明"
        description: "先看清现在已经支持什么、还没支持什么。"
        url: /Jekyll.Net/zh/compatibility/
  - title: "一条最短路径"
    description: "如果你只想用最短时间感受项目当前状态，可以按照这条路径走。"
    variant: "list"
    columns: 1
    section_class: "panel panel-section"
    items:
      - label: "先读 README，确认项目目标与当前限制"
        description: "仓库首页已经列出能力边界。"
        url: https://github.com/IoTSharp/Jekyll.Net/blob/main/README.md
      - label: "用 sample-site 执行一次 build"
        description: "直接观察 _site 产物和生成结果。"
        url: /Jekyll.Net/zh/getting-started/
      - label: "打开文档导航，找到与你目标最接近的入口"
        description: "开发文档、兼容性、Pages 配置都从这里开始。"
        url: /Jekyll.Net/zh/navigation/
---
