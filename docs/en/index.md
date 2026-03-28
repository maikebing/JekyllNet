---
title: "JekyllNet Home"
description: "A Jekyll-style static site generator written in C# and aimed at practical GitHub Pages compatibility."
permalink: /en/
layout: structured
lang: "en"
nav_key: "docs"
hero:
  eyebrow: ".NET static publishing for Jekyll-shaped sites"
  title: "Build Jekyll-style sites in C# without leaving the GitHub Pages mental model"
  lead: "JekyllNet is a C# and .NET 10 static site generator focused on the workflows real documentation and content sites use first: Front Matter, layouts, includes, collections, posts, pagination, Sass, multilingual docs, and practical GitHub Pages style behavior."
  primary_label: "Open the docs map"
  primary_url: /en/navigation/
  secondary_label: "Browse sample-site"
  secondary_url: https://github.com/JekyllNet/JekyllNet/tree/main/sample-site
  tertiary_label: "Read launch news"
  tertiary_url: /en/news/project-status/
  tags:
    - ".NET 10"
    - "GitHub Pages style"
    - "Markdown + Liquid"
    - "CLI build/watch/serve"
  code_title: "Quick Start"
  code: |
    dotnet run --project .\JekyllNet.Cli -- build --source .\sample-site
    dotnet run --project .\JekyllNet.Cli -- serve --source .\docs --port 5055
  note_title: "What this site is for"
  note_text: "This docs site is now the public handbook for JekyllNet: use docs for orientation, then the blog for feature, configuration, CLI, and AI translation deep dives."
stats:
  - label: "Runtime"
    value: ".NET 10"
  - label: "Primary workflows"
    value: "build / watch / serve"
  - label: "Primary host"
    value: "jekyllnet.help"
  - label: "Regression safety"
    value: "Build regression tests"
sections:
  - title: "What you can use today"
    description: "The current implementation is already broad enough for real docs sites and smaller content sites."
    variant: "cards"
    columns: 3
    section_class: ""
    items:
      - title: "Content pipeline"
        description: "YAML front matter, Markdown, collections, posts, tags, categories, excerpts, drafts, future posts, and unpublished content switches are wired into the build."
      - title: "Theme compatibility"
        description: "Nested layouts, includes, common Liquid control flow, high-value filters, permalink fallback, defaults, static file front matter, and Sass all work together."
      - title: "Site operations"
        description: "CLI commands, site build regression tests, GitHub Actions examples, dotnet tool packaging metadata, and winget templates are now part of the repo story."
  - title: "Read by goal"
    description: "Pick the next page based on the job you want to finish."
    variant: "quick-links"
    columns: 4
    section_class: ""
    items:
      - title: "Get unblocked fast"
        description: "Run your first build, inspect output, and move to local preview."
        url: /en/getting-started/
      - title: "Check compatibility"
        description: "Understand what already aligns with common Jekyll and GitHub Pages behavior."
        url: /en/compatibility/
      - title: "Study configuration"
        description: "See which _config.yml options are already worth relying on."
        url: /en/blog/configuration-guide/
      - title: "Set up multilingual docs"
        description: "Use locales, automatic translation links, and AI-assisted translation workflows."
        url: /en/blog/ai-translation/
  - title: "Fresh reading"
    description: "The blog now carries the richer feature and workflow documentation that used to be missing."
    variant: "list"
    columns: 1
    section_class: "panel panel-section"
    items:
      - label: "Feature overview"
        description: "A practical tour of rendering, publishing, filters, pagination, and output stability."
        url: /en/blog/feature-overview/
      - label: "Configuration guide"
        description: "A grouped explanation of the _config.yml options that matter most today."
        url: /en/blog/configuration-guide/
      - label: "CLI and development workflow"
        description: "How build, watch, serve, packaging, and CI fit together."
        url: /en/blog/cli-workflow/
      - label: "AI translation workflow"
        description: "Provider support, cache, incremental translation, and glossary behavior."
        url: /en/blog/ai-translation/
      - label: "Launch announcement"
        description: "What shipped in the first public milestone on March 25, 2026."
        url: /en/news/project-status/
---
