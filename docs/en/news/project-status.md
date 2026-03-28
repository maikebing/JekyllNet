---
title: "JekyllNet launch: docs, workflows, and compatibility baseline"
description: "The first public release note for the JekyllNet docs site and current feature baseline."
permalink: /en/news/project-status/
lang: "en"
nav_key: "news"
---
# JekyllNet launch: docs, workflows, and compatibility baseline

On March 25, 2026, JekyllNet reached an important public milestone: the project now has a real bilingual documentation site, a fuller CLI workflow, stronger regression safety, and a much clearer compatibility story.

## What shipped in this milestone

- a proper `docs` source site in English and Chinese
- blog articles organized by feature set, configuration, CLI workflow, and AI translation
- `build`, `watch`, and `serve` CLI flows
- fixture-based build regression tests for both `docs` and `sample-site`
- broader theme-facing filter coverage
- stronger pagination and publishing semantics
- AI-assisted translation with cache, incremental reuse, glossary support, and OpenAI-compatible provider support

## What is next

The remaining work is now narrower and easier to prioritize:

- closer alignment on the last Liquid edge cases
- tighter pagination behavior in the remaining Jekyll corners
- continued `_config.yml` coverage expansion where it materially improves theme compatibility
- more examples, recipes, and deployment documentation over time
---
