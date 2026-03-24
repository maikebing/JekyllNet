---
title: 为什么要给 Jekyll.Net 单独写一套 Pages 文档
description: 为项目准备一套直接面向 GitHub Pages 的说明站点。
permalink: /zh/blog/design-goals/
lang: zh-CN
nav_key: blog
---
# 为什么要给 Jekyll.Net 单独写一套 Pages 文档

`Jekyll.Net` 这个项目本身就在朝 GitHub Pages 风格兼容靠拢，所以最自然的展示方式，其实就是给它自己也准备一套 Pages 文档站。

## 这样做的价值

- 项目本身更容易被第一次来到仓库的人理解。
- `docs` 目录可以直接成为项目对外展示面。
- 后续补兼容性时，也能顺手拿这套站点当回归样例。

## 为什么直接借参考站点外壳

这样可以快速得到一套成熟、清楚、视觉完成度高的文档壳，而不是花时间从零搭一套页面系统。真正值得重新编写的是内容本身，不是已经成熟的布局和样式。
