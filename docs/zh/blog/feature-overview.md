---
title: "特性总览"
description: "综述 JekyllNet 当前之渲染、发布、分页、多语与回归能力。"
permalink: /zh/blog/feature-overview/
lang: "zh-CN"
nav_key: "blog"
---
# 特性总览

吾今已非徒为“验其可行”之器。代码之中，已具一批足以支撑真实文档站、主题风格站与简要博客之能力。

## 内容与发布之道

今构建管线，已通下列常用内容原语：

- `_config.yml` 加载与站点级 defaults
- 页面、文章、collections、静态文件之 YAML Front Matter
- Markdown 转 HTML
- posts、tags、categories 与 collection 输出
- `drafts`、`future`、`unpublished` 控制
- excerpt 与 `excerpt_separator`
- nested `index.md` permalink 处理

## Layout、Include 与 Liquid 行为

今渲染层亦已具多项常见主题所仰赖者：

- `_layouts` 与嵌套 layout
- `_includes`
- `_data`
- `if`、`unless`、`case/when`、`capture`、`contains`
- 增强之 `for`，兼 `limit`、`offset`、`reversed` 与 `forloop` 元数据
- 更稳之循环变量清理
- 更稳之嵌套控制块内 include 行为

## 主题最关心之 Filters

今已备：

- `relative_url`
- `absolute_url`
- `markdownify`
- `where`
- `sort`
- `map`
- `compact`
- `jsonify`
- `slugify`

## 分页、资源与稳定之守

站点构建层今亦已通：

- 基础分页：`paginate` 与 `paginate_path`
- 嵌套分页配置：`pagination.per_page` 与 `pagination.path`
- 按页禁用分页
- Sass / SCSS 编译
- 静态资源复制
- `docs` 与 `sample-site` 之基于 fixture 的构建回归测试

## 多语与 AI 辅助翻译

今吾亦可：

- 按 locale 应用 defaults
- 自动生成 translation links
- 调用 AI 辅助翻译
- 复用翻译缓存
- 增量翻译未变之内容
- 借 glossary 固定术语
- 接 OpenAI、DeepSeek、Ollama 与 OpenAI 兼容第三方端点
---
