---
title: Why Jekyll.Net should have its own Pages docs
description: Give the project a documentation site that directly targets the way it wants to be used.
permalink: /en/blog/design-goals/
lang: en
nav_key: blog
---
# Why Jekyll.Net should have its own Pages docs

Jekyll.Net itself is moving toward GitHub Pages style compatibility, so the most natural way to present it is to let it have a Pages-style documentation site of its own.

## Why that matters

- The repository becomes easier to understand for first-time readers.
- The `docs` directory can become the outward-facing project surface immediately.
- Future compatibility work can use this site as a living regression sample.

## Why reuse the reference shell directly

That approach gives the project a mature, clear, visually complete documentation shell right away. The part worth rewriting from scratch is the content, not a shell that is already doing its job well.
