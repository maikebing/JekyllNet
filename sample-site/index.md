---
layout: default
title: 首页
---
# Jekyll.Net

这是一个用 **C# / .NET 10** 构建的 Jekyll 风格站点生成器。

- 支持 Front Matter
- 支持 Markdown
- 支持基础 Layout
- 支持 `_includes`
- 支持 `_posts`
- 支持 `collections`

{% include hero.html subtitle="逐步逼近 GitHub Pages" %}

> 作者：{{ site.data.authors.primary.name }} / {{ site.data.authors.primary.role }}

## 最新文章

{% for post in site.posts %}
- [{{ post.title }}]({{ post.url }}) - {{ post.date | date: "%Y-%m-%d" }} - 标签数 {{ post.tags | size }}
{% endfor %}

## 文档集合

{% for doc in site.collections.docs %}
- [{{ doc.title }}]({{ doc.url }})
{% endfor %}

## 标签

{% for post in site.tags.jekyll %}
- Jekyll 标签文章：{{ post.title }}
{% endfor %}

## 分类

{% for post in site.categories.guides %}
- Guides 分类文章：{{ post.title | upcase }}
{% endfor %}
