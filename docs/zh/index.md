---
title: "JekyllNet 首页"
description: "吾乃以 C# 编之静态站点生成器，志在渐近 GitHub Pages 常见之法。"
permalink: /zh/
layout: structured
lang: "zh-CN"
nav_key: "docs"
hero:
  eyebrow: ".NET 静态站点生成"
  title: "吾以 C# 造 Jekyll 风格之站"
  lead: "吾名 JekyllNet，以 C# 与 .NET 10 为器，务在补齐文档站与内容站常用之道：Front Matter、布局与 Include、Collections、文章、分页、Sass、多语文档，以及诸多贴近 GitHub Pages 之行为。"
  primary_label: "览文档导航"
  primary_url: /zh/navigation/
  secondary_label: "观 sample-site"
  secondary_url: https://github.com/JekyllNet/JekyllNet/tree/main/sample-site
  tertiary_label: "读首发新闻"
  tertiary_url: /zh/news/project-status/
  tags:
    - ".NET 10"
    - "GitHub Pages 风格"
    - "Markdown + Liquid"
    - "CLI build/watch/serve"
  code_title: "起步二令"
  code: |
    dotnet run --project .\JekyllNet.Cli -- build --source .\sample-site
    dotnet run --project .\JekyllNet.Cli -- serve --source .\docs --port 5055
  note_title: "一言以蔽之"
  note_text: "吾非包裹既有 Jekyll 者，乃欲以 .NET 重铸一条近乎 GitHub Pages 心法之生成路径。"
stats:
  - label: "所用运行时"
    value: ".NET 10"
  - label: "今之工作流"
    value: "build / watch / serve"
  - label: "文档形制"
    value: "中英双语"
  - label: "回归之守"
    value: "站点构建回归测试"
sections:
  - title: "今已可为何事"
    description: "吾之实现，今已足以支撑真实文档站、小型内容站，以及主题兼容之验证。"
    variant: "cards"
    columns: 3
    section_class: ""
    items:
      - title: "内容之道"
        description: "已通 YAML Front Matter、Markdown、Collections、Posts、Tags、Categories、摘要、Draft/Future/Unpublished 诸语义。"
      - title: "主题之道"
        description: "已通嵌套 Layout、Include、常见 Liquid 控制语法、高价值 Filters、defaults、静态文件 Front Matter 与 Sass。"
      - title: "工程之道"
        description: "CLI、站点构建回归测试、GitHub Actions 示例、dotnet tool 打包元数据与 winget 模板，今皆在库中。"
  - title: "按志而入"
    description: "来者所求不同，其所当先观者亦异。"
    variant: "quick-links"
    columns: 4
    section_class: ""
    items:
      - title: "完整使用说明"
        description: "一篇跑通安装、构建、预览、翻译、发布与排错。"
        url: /zh/blog/complete-usage-guide/
      - title: "先试其行"
        description: "先本地构建一次，再观输出与预览。"
        url: /zh/getting-started/
      - title: "审其兼容"
        description: "先明何者已成，何者尚待雕琢。"
        url: /zh/compatibility/
      - title: "究其配置"
        description: "观 _config.yml 今已可倚赖之项。"
        url: /zh/blog/configuration-guide/
      - title: "广其语言"
        description: "观 locales、自动 translation links 与 AI 翻译之法。"
        url: /zh/blog/ai-translation/
  - title: "宜续观者"
    description: "若欲速得其全貌，可循此数篇。"
    variant: "list"
    columns: 1
    section_class: "panel panel-section"
    items:
      - label: "完整使用说明"
        description: "以实战路径总览今之工具链。"
        url: /zh/blog/complete-usage-guide/
      - label: "特性总览"
        description: "综述渲染、发布语义、分页、Filters 与回归测试。"
        url: /zh/blog/feature-overview/
      - label: "配置指南"
        description: "分门别类而述 _config.yml 诸要。"
        url: /zh/blog/configuration-guide/
      - label: "CLI 与开发工作流"
        description: "述 build、watch、serve、CI 与打包分发之衔接。"
        url: /zh/blog/cli-workflow/
      - label: "AI 翻译工作流"
        description: "述 provider、缓存、增量翻译、术语表与兼容端点。"
        url: /zh/blog/ai-translation/
      - label: "首发新闻"
        description: "观 2026 年 3 月 25 日里程碑所成。"
        url: /zh/news/project-status/
      - label: "v0.2.5 发布新闻"
        description: "观本轮主题兼容增强与发布链路统一之发布稿。"
        url: /zh/news/v0-2-5/
---
