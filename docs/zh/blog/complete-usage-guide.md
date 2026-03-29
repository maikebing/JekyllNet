---
title: "JekyllNet 完整使用说明（实战版）"
description: "从安装、构建、预览、翻译、发布到排错，一篇讲清 JekyllNet 当前工具链。"
permalink: /zh/blog/complete-usage-guide/
lang: "zh-CN"
nav_key: "blog"
---
若君只欲一篇而尽知 JekyllNet 今可如何用，此文即其总册。

## 项目入口

- 仓库地址：[https://github.com/JekyllNet/JekyllNet](https://github.com/JekyllNet/JekyllNet)
- 文档网站：[https://jekyllnet.help](https://jekyllnet.help)
- GitHub Pages 站点入口（仓库 Pages）：[https://jekyllnet.github.io/JekyllNet/](https://jekyllnet.github.io/JekyllNet/)

## 一、安装与运行环境

今以 `.NET 10` 为基。

先验环境：

```powershell
dotnet --version
```

克隆并进入仓库后，先跑一次测试以确认环境可用：

```powershell
dotnet test .\JekyllNet.slnx
```

## 二、最小可用路径（5 分钟跑通）

### 1) 构建示例站点

```powershell
dotnet run --project .\JekyllNet.Cli -- build --source .\sample-site
```

输出目录默认为 `sample-site\_site`。

### 2) 构建文档站

```powershell
dotnet run --project .\JekyllNet.Cli -- build --source .\docs --destination .\artifacts\docs-site
```

### 3) 本地预览

```powershell
dotnet run --project .\JekyllNet.Cli -- serve --source .\docs --port 5055
```

浏览器访问 `http://localhost:5055`。

## 三、日常编辑工作流

### 1) 连续编辑时

```powershell
dotnet run --project .\JekyllNet.Cli -- watch --source .\docs
```

`watch` 适于改 Markdown、布局、include、样式并实时重建。

### 2) 稳定预览时

```powershell
dotnet run --project .\JekyllNet.Cli -- serve --source .\docs --port 5055 --no-watch
```

`serve --no-watch` 适于演示或对照验证。

### 3) 含草稿与未来文章的预览

```powershell
dotnet run --project .\JekyllNet.Cli -- serve --source .\sample-site --drafts --future --unpublished
```

## 四、配置与内容组织（推荐顺序）

1. 先配置 `_config.yml`：站点信息、URL、分页、多语。
2. 再配置 `_layouts` 与 `_includes`：统一页面壳层。
3. 再整理 `_posts`、`_docs`、集合与 front matter。
4. 最后补 Sass/SCSS 与静态资源结构。

建议先读：

- [配置指南](/zh/blog/configuration-guide/)
- [特性总览](/zh/blog/feature-overview/)
- [兼容性说明](/zh/compatibility/)

## 五、多语与 AI 翻译

JekyllNet 已具多语路线，可结合 AI 翻译做增量更新。

你可在 `_config.yml` 中配置翻译 provider、目标语言、缓存与术语表策略，再将中文源内容批量生成英文或其他语种页面。

详见：

- [AI 翻译工作流](/zh/blog/ai-translation/)

## 六、发布与自动化

### 1) GitHub Pages 直接发布 `docs`

仓库设置中选择：

- Deploy from a branch
- Branch: `main`
- Folder: `/docs`

### 2) 用 GitHub Actions 构建产物

可复用 `JekyllNet/action@v2.5` 在 CI 中构建并上传 `docs-site` artifact。

详见：

- [站点部署](/zh/github-pages/)
- [CLI 与开发工作流](/zh/blog/cli-workflow/)

### 3) dotnet tool 打包

```powershell
dotnet pack .\JekyllNet.Cli\JekyllNet.Cli.csproj -c Release
```

## 七、常见排错

### 1) 样式未编译

检查 Sass/SCSS 入口文件是否带 YAML Front Matter。

### 2) 链接异常

检查 `_config.yml` 的 `url` 与 `baseurl` 是否匹配当前部署方式。

### 3) 本地能过，CI 失败

优先对齐：

- `dotnet` 版本
- 构建输入目录
- 是否遗漏 `docs` 与生成器代码共同变更触发条件

## 八、按角色速查

- 内容编辑：先看 [快速开始](/zh/getting-started/) 与 [CLI 与开发工作流](/zh/blog/cli-workflow/)
- 主题适配：先看 [兼容性说明](/zh/compatibility/) 与 [特性总览](/zh/blog/feature-overview/)
- 发布运维：先看 [站点部署](/zh/github-pages/) 与 [项目新闻](/zh/news/)
- 多语运营：先看 [AI 翻译工作流](/zh/blog/ai-translation/)

## 九、一句话建议

先以 `sample-site` 验核心能力，再以 `docs` 验真实发布链路；本地跑通 `build + serve` 后，再接入 CI 与多语翻译，成功率最高。
