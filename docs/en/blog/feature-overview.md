---
title: "Feature Overview"
description: "A practical tour of the current rendering, publishing, and site-building capabilities in JekyllNet."
permalink: /en/blog/feature-overview/
lang: "en"
nav_key: "blog"
---
# Feature Overview

JekyllNet has moved past the stage where it only proves a concept. The current codebase now covers a meaningful slice of the behavior needed by documentation sites, theme-style sites, and simple blogs.

## Content and publishing pipeline

The build pipeline already covers the content primitives that most Jekyll-shaped sites expect:

- `_config.yml` loading with defaults and site-level options
- YAML front matter for pages, posts, collections, and static files
- Markdown to HTML rendering
- posts, tags, categories, and collection output
- `drafts`, `future`, and `unpublished` switches
- excerpt generation and `excerpt_separator`
- nested `index.md` permalink handling

## Layout, includes, and Liquid behavior

The rendering layer already supports the common layout and template flow that many themes rely on:

- `_layouts` and nested layouts
- `_includes`
- `_data`
- `if`, `unless`, `case/when`, `capture`, and `contains`
- stronger `for` support including `limit`, `offset`, `reversed`, and `forloop` metadata
- improved loop cleanup so loop variables do not leak unexpectedly
- more stable include behavior inside nested control-flow blocks

## Filters that matter for themes

Recent work focused on high-value filters because that lifts theme compatibility quickly. JekyllNet now includes practical support for:

- `relative_url`
- `absolute_url`
- `markdownify`
- `where`
- `sort`
- `map`
- `compact`
- `jsonify`
- `slugify`

## Pagination, assets, and output stability

The site-building layer also covers:

- baseline pagination with `paginate` and `paginate_path`
- nested config keys such as `pagination.per_page` and `pagination.path`
- per-page pagination disable
- Sass and SCSS compilation
- static asset copying
- fixture-based build regression tests for both `sample-site` and `docs`

## Multilingual and AI-assisted docs

JekyllNet now supports:

- locale-aware defaults
- automatic translation links across locale variants
- AI-assisted translation
- translation cache
- incremental translation reuse
- glossary support
- provider support for OpenAI, DeepSeek, Ollama, and OpenAI-compatible third-party endpoints
---
