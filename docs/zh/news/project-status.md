---
title: "JekyllNet 首发：文档站、工作流与兼容性基线"
description: "JekyllNet 文档站与当前能力基线之第一篇正式首发公告。"
permalink: /zh/news/project-status/
lang: "zh-CN"
nav_key: "news"
---
# JekyllNet 首发：文档站、工作流与兼容性基线

时在 2026 年 3 月 25 日，吾至一可正式告世之里程碑：今已有可用之中英双语文档站、较完备之 CLI 工作流、更强之回归保护，与更清晰之兼容边界。

## 今次所成

- 正式之 `docs` 中英双语源站
- 按特性、配置、CLI 工作流、AI 翻译而分之博客文章
- `build`、`watch`、`serve` 三条 CLI 工作流
- `docs` 与 `sample-site` 之站点构建回归测试
- 更完整之主题侧 Filters
- 更稳之分页与发布语义
- 具缓存、增量复用、glossary 与 OpenAI 兼容 provider 支持之 AI 翻译

## 此次何以为重

今之 JekyllNet，较前更近人所实用之产品形态：

- 不复只有源码与 README，乃有正式文档入口
- 不复只有单次 build，乃有本地预览与持续迭代之流
- 不复专赖人工回归，乃有 fixture 化构建回归测试之守
- 不复唯言方向，乃有更明之当前支持边界

## 后续所务

后续之工，今已较聚焦，主要在：

- 余下 Liquid 边角语义之继续对齐
- Pagination 于更多 Jekyll 边界之细节一致
- 继续扩 `_config.yml` 覆盖，但专取真能益于主题兼容者
- 续补示例、配方与部署文稿
---
