---
title: "Compatibility Notes"
description: "What JekyllNet already implements, what is partial, and what still remains."
permalink: /en/compatibility/
lang: "en"
nav_key: "docs"
---
# Compatibility Notes

JekyllNet is not aiming to be a generic static site generator with a little Liquid syntax on top. The direction is more specific: make common Jekyll and GitHub Pages style sites work in a way that stays understandable, testable, and maintainable in .NET.

## Compatibility Matrix

| Area | Status | Notes |
| --- | --- | --- |
| `_config.yml` loading | Done | Common site configuration, defaults, include or exclude rules, site-level permalink fallback, footer metadata, analytics options, and custom-domain values are wired into the build. |
| Front matter | Done | YAML front matter, defaults, page variables, static file front matter, excerpts, and `excerpt_separator` are supported. |
| Markdown and layouts | Done | Markdown to HTML, nested layouts, includes, collections, posts, tags, categories, and Sass are in the normal build pipeline. |
| Liquid control flow | Partial | `if`, `unless`, `case/when`, `capture`, `contains`, and stronger `for` support are in place. The remaining gap is mostly edge-case polish around `assign` scope and include timing. |
| Filters | Done | High-value theme filters such as `relative_url`, `absolute_url`, `markdownify`, `where`, `sort`, `map`, `compact`, `jsonify`, and `slugify` are available. |
| Publishing semantics | Done | `drafts`, `future`, `unpublished`, nested index permalink handling, and excerpt behavior are connected to real build output. |
| Pagination | Partial | Baseline pagination works, including `paginate`, `paginate_path`, `pagination.per_page`, `pagination.path`, and per-page disable. Some Jekyll edge cases still need closer alignment. |
| Multilingual docs | Done | Locale-aware defaults, automatic translation links, and AI-assisted translation workflows are available. |
| Site build regression | Done | `sample-site` and `docs` are covered by fixture-based build regression tests. |
| Plugin ecosystem | Not yet | Third-party Jekyll plugin compatibility is still outside the supported boundary. |

## What is already a good fit

JekyllNet is already a reasonable choice for:

- project documentation sites
- bilingual or multilingual docs that want AI-assisted translation help
- small and medium content sites that stay close to common Jekyll conventions
- GitHub Pages style themes that rely on mainstream Liquid and configuration behavior

## What is not claimed yet

JekyllNet is not yet claiming:

- strict one-to-one parity with a specific GitHub Pages release
- broad plugin compatibility
- complete Liquid language coverage
- pagination parity with every Jekyll plugin variation

## Where to go deeper

Use these pages to turn the matrix into implementation detail:

- [Feature Overview](/en/blog/feature-overview/)
- [Configuration Guide](/en/blog/configuration-guide/)
- [AI Translation Workflow](/en/blog/ai-translation/)
---
