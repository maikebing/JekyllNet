---
title: "JekyllNet Complete Usage Guide"
description: "A practical end-to-end guide covering setup, build, preview, translation, publishing, and troubleshooting."
permalink: /en/blog/complete-usage-guide/
lang: "en"
nav_key: "blog"
---
If you want one page that covers the full current toolchain, this is the handbook.

## Project entry points

- Repository: [https://github.com/JekyllNet/JekyllNet](https://github.com/JekyllNet/JekyllNet)
- Website: [https://jekyllnet.help](https://jekyllnet.help)
- GitHub Pages entry for this repo: [https://jekyllnet.github.io/JekyllNet/](https://jekyllnet.github.io/JekyllNet/)

## 1. Prerequisites

JekyllNet currently targets `.NET 10`.

Check your environment:

```powershell
dotnet --version
```

After cloning the repo, run tests once to verify the environment:

```powershell
dotnet test .\JekyllNet.slnx
```

## 2. Fastest path to first success

### 1) Build the sample site

```powershell
dotnet run --project .\JekyllNet.Cli -- build --source .\sample-site
```

Output is generated under `sample-site\_site` by default.

### 2) Build the docs site

```powershell
dotnet run --project .\JekyllNet.Cli -- build --source .\docs --destination .\artifacts\docs-site
```

### 3) Start local preview

```powershell
dotnet run --project .\JekyllNet.Cli -- serve --source .\docs --port 5055
```

Open `http://localhost:5055`.

## 3. Daily authoring workflow

### 1) While actively editing

```powershell
dotnet run --project .\JekyllNet.Cli -- watch --source .\docs
```

Use `watch` for fast feedback loops while editing Markdown, layouts, includes, and styles.

### 2) For a stable preview endpoint

```powershell
dotnet run --project .\JekyllNet.Cli -- serve --source .\docs --port 5055 --no-watch
```

### 3) Preview with drafts and future content

```powershell
dotnet run --project .\JekyllNet.Cli -- serve --source .\sample-site --drafts --future --unpublished
```

## 4. Configuration and content structure

Recommended order:

1. Define site-level settings in `_config.yml`.
2. Set shared shell behavior in `_layouts` and `_includes`.
3. Organize posts, collections, and front matter.
4. Finalize Sass/SCSS and static asset structure.

Read next:

- [Configuration Guide](/en/blog/configuration-guide/)
- [Feature Overview](/en/blog/feature-overview/)
- [Compatibility Notes](/en/compatibility/)

## 5. Multilingual workflow and AI translation

JekyllNet supports locale-aware routes and AI-assisted translation with incremental reuse patterns.

Configure provider, targets, cache, and glossary behavior in `_config.yml`, then generate localized pages from your source language.

Details:

- [AI Translation Workflow](/en/blog/ai-translation/)

## 6. Publishing and automation

### 1) Publish `docs` directly with GitHub Pages

Use:

- Deploy from a branch
- Branch: `main`
- Folder: `/docs`

### 2) Build artifacts with GitHub Actions

Use `JekyllNet/action@v2.5` to build and upload docs artifacts in CI.

See:

- [Deployment](/en/github-pages/)
- [CLI and Development Workflow](/en/blog/cli-workflow/)

### 3) Pack as a dotnet tool

```powershell
dotnet pack .\JekyllNet.Cli\JekyllNet.Cli.csproj -c Release
```

## 7. Troubleshooting quick list

### 1) Styles are not compiled

Make sure Sass or SCSS entry files include YAML front matter.

### 2) Generated links are wrong

Check whether `_config.yml` `url` and `baseurl` match your deployment shape.

### 3) Works locally but fails in CI

Verify:

- `.NET` SDK version alignment
- correct build source path
- workflow trigger coverage for docs and generator changes

## 8. Read by role

- Content author: [Getting Started](/en/getting-started/) and [CLI and Development Workflow](/en/blog/cli-workflow/)
- Theme compatibility focus: [Compatibility Notes](/en/compatibility/) and [Feature Overview](/en/blog/feature-overview/)
- Release or operations: [Deployment](/en/github-pages/) and [Project News](/en/news/)
- Localization owner: [AI Translation Workflow](/en/blog/ai-translation/)

## 9. One practical recommendation

Validate core behavior with `sample-site` first, then validate real publishing flow with `docs`; after `build + serve` is stable locally, connect CI and multilingual translation.
