---
title: 快速开始
description: 在本地运行 Jekyll.Net，并生成 sample-site 的静态输出。
permalink: /zh/getting-started/
lang: zh-CN
nav_key: docs
---
# 快速开始

`Jekyll.Net` 目前最直接的入口是 CLI 里的 `build` 命令。

## 运行命令

```powershell
dotnet run --project .\JekyllNet.Cli -- build --source .\sample-site
```

生成结果默认输出到 `sample-site\_site`。

## 你会看到什么

- `index.md` 会被渲染成首页。
- `_posts` 里的文章会生成日期型永久链接。
- `_layouts` 和 `_includes` 会组合成最终 HTML。
- `assets/scss` 或 `assets/css` 会被复制或编译到输出目录。

## 什么时候读这个页面

如果你想先确认项目是不是已经能支撑一个小型文档站，这页就是第一站。跑通一次以后，再去看 [兼容性说明](/Jekyll.Net/zh/compatibility/) 会更容易理解目前缺口在哪里。
