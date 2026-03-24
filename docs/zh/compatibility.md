---
title: 兼容性说明
description: Jekyll.Net 当前已实现与待补齐的范围。
permalink: /zh/compatibility/
lang: zh-CN
nav_key: docs
---
# 兼容性说明

项目目标不是重新发明一个随意的静态站点生成器，而是逐步向 GitHub Pages 常见行为靠拢。

## 当前已经覆盖

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
- Sass/SCSS 编译
- 静态资源复制到 `_site`

## 当前仍然有限制

- pagination
- 更完整的 Liquid 语法和 filters 覆盖
- excerpts / drafts / future / unpublished
- 更完整的 GitHub Pages 固定版本兼容行为
- 对真实 Jekyll 插件生态的兼容

## 怎么理解这个差距

如果你把 `Jekyll.Net` 当成一个“先满足项目文档站，再逐步抹平兼容细节”的实现，这个状态是合理的。它已经足够支撑小型项目站点，但还没有承诺和 GitHub Pages 做到完全一比一。
