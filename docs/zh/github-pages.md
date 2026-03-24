---
title: GitHub Pages 配置
description: 把当前仓库 docs 目录作为 GitHub Pages 站点源。
permalink: /zh/github-pages/
lang: zh-CN
nav_key: docs
---
# GitHub Pages 配置

当前仓库已经新增了一个 `docs` 目录，里面放的是 GitHub Pages 可直接消费的 Jekyll 站点源文件。

## 目录作用

- `docs/_config.yml` 定义站点标题、语言和 `baseurl`。
- `docs/_layouts`、`docs/_includes` 和 `docs/assets` 组成页面外壳。
- `docs/zh` 与 `docs/en` 放的是实际文档内容。

## 推荐的仓库设置

在 GitHub 仓库设置里打开 Pages，然后把来源改成：

`Deploy from a branch` -> `main` -> `/docs`

## 关于 baseurl

当前配置把 `baseurl` 设成了 `/Jekyll.Net`，这是按当前仓库名准备的。如果仓库名后面改了，记得同步修改 `docs/_config.yml`。

## 为什么这样做

这次实现的目标是“模板样式直接拿过来，内容重写”，所以我保留了参考站点的布局组织和视觉风格，但把所有导航、文案和项目链接切换成了 `Jekyll.Net` 自己的内容。
