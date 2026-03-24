---
title: Compatibility Notes
description: What Jekyll.Net already implements and what still remains.
permalink: /en/compatibility/
lang: en
nav_key: docs
---
# Compatibility Notes

The project goal is not to build an arbitrary static site generator. The direction is to move closer to common GitHub Pages behavior over time.

## Already covered

- `_config.yml`
- YAML Front Matter
- Markdown to HTML
- `_layouts` and nested layouts
- `_includes`
- `_data`
- `_posts`
- `collections`
- `tags/categories`
- basic Liquid tags and common filters
- Sass or SCSS compilation
- static asset copying into `_site`

## Still limited

- pagination
- broader Liquid syntax and filter coverage
- excerpts / drafts / future / unpublished
- stricter GitHub Pages version-level compatibility
- compatibility with the wider Jekyll plugin ecosystem

## How to think about the gap

If you treat Jekyll.Net as a project that first needs to serve real documentation sites and then close compatibility gaps over time, the current status is coherent. It is already useful for small sites, but it is not promising one-to-one parity yet.
