---
title: "Why JekyllNet now has its own Pages-style docs site"
description: "The docs site is both the public handbook and a real compatibility fixture."
permalink: /en/blog/design-goals/
lang: "en"
nav_key: "blog"
---
# Why JekyllNet now has its own Pages-style docs site

JekyllNet is moving toward practical GitHub Pages compatibility, so the most honest way to present it is to let the project stand on a Pages-style docs site of its own.

## The docs site is not just marketing

This repository-level docs source now does three jobs at once:

- it gives first-time readers a clean project entry point
- it provides a stable place for feature, configuration, and workflow documentation
- it acts as a real build fixture covered by regression tests

That combination is much more valuable than a detached design mock or a README-only explanation.

## Why compatibility is the guiding idea

The point is not to invent an exotic site generator personality. The point is to make common Jekyll-shaped sites feel familiar in .NET:

- familiar content structure
- familiar front matter and permalink expectations
- familiar layout and include model
- familiar theme-facing filters and pagination behavior

That focus makes adoption easier for teams already living in GitHub Pages conventions.
---
