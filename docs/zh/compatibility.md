---
title: "兼容性说明"
description: "吾已实现、部分实现，与尚待补齐之边界。"
permalink: /zh/compatibility/
lang: "zh-CN"
nav_key: "docs"
---
# 兼容性说明

吾之志，不在为“略通些许 Liquid 语法”之泛用静态生成器；吾所务者，乃使常见 Jekyll / GitHub Pages 风格站点，于 .NET 中亦可安然而行。

## 兼容之矩

| 领域 | 状态 | 说明 |
| --- | --- | --- |
| `_config.yml` 加载 | 已成 | 常见站点配置、defaults、include/exclude、站点级 permalink fallback、页脚元数据、统计配置与自定义域名设置皆已接入。 |
| Front Matter | 已成 | 已通 YAML front matter、defaults、页面变量、静态文件 front matter、摘要与 `excerpt_separator`。 |
| Markdown 与布局 | 已成 | 已通 Markdown 转 HTML、嵌套 layout、include、collections、posts、tags、categories 与 Sass。 |
| Liquid 控制语法 | 半成 | `if`、`unless`、`case/when`、`capture`、`contains` 与增强 `for` 已可用，余者多在 `assign` 作用域与 include 时机之边角。 |
| Filters | 已成 | 已具 `relative_url`、`absolute_url`、`markdownify`、`where`、`sort`、`map`、`compact`、`jsonify`、`slugify` 等高价值 filters。 |
| 发布语义 | 已成 | `drafts`、`future`、`unpublished`、嵌套 index permalink 与 excerpt 行为，今皆接入真实构建。 |
| Pagination | 半成 | 已通 `paginate`、`paginate_path`、`pagination.per_page`、`pagination.path` 与按页禁用；然更多 Jekyll 边角，尚待细磨。 |
| 多语文档 | 已成 | locale 级 defaults、自动 translation links 与 AI 辅助翻译工作流，今皆可用。 |
| 站点构建回归 | 已成 | `docs` 与 `sample-site` 皆有基于 fixture 的构建回归测试。 |
| 插件生态 | 未取 | 第三方 Jekyll 插件兼容，今尚不在支持之界。 |

## 今已宜于何用

吾今较宜用于：

- 项目文档站
- 中英乃至更多语种之多语文档站
- 结构近于常见 Jekyll 约定之小中型内容站
- 不依重插件，而依主流 Liquid 与配置行为之 GitHub Pages 风格主题

## 今未敢遽言者

吾今尚未宣称：

- 与某一特定 GitHub Pages 发行版逐项尽同
- 广泛兼容第三方 Jekyll 插件
- 覆尽 Liquid 语言诸义
- 对齐一切分页插件之变体

## 欲求其详

可续观：

- [特性总览](/zh/blog/feature-overview/)
- [配置指南](/zh/blog/configuration-guide/)
- [AI 翻译工作流](/zh/blog/ai-translation/)
---
