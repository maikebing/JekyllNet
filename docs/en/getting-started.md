---
title: Getting Started
description: Run Jekyll.Net locally and generate the sample-site output.
permalink: /en/getting-started/
lang: en
nav_key: docs
---
# Getting Started

The most direct entry point today is the CLI `build` command.

## Run the command

```powershell
dotnet run --project .\JekyllNet.Cli -- build --source .\sample-site
```

The generated output goes to `sample-site\_site` by default.

## What you will see

- `index.md` becomes the home page.
- Posts under `_posts` get date-based permalinks.
- `_layouts` and `_includes` combine into final HTML.
- `assets/scss` and `assets/css` are compiled or copied into the output.

## When to read this page

Use this page first if you want to know whether the project is already good enough for a small documentation site. After one successful run, the [compatibility page](/Jekyll.Net/en/compatibility/) will make much more sense.
