---
title: GitHub Pages Setup
description: Publish the current docs directory as a GitHub Pages source site.
permalink: /en/github-pages/
lang: en
nav_key: docs
---
# GitHub Pages Setup

The repository now contains a `docs` directory that is meant to act as a GitHub Pages friendly Jekyll source site.

## Directory purpose

- `docs/_config.yml` defines site title, language, and `baseurl`.
- `docs/_layouts`, `docs/_includes`, and `docs/assets` make up the shell.
- `docs/zh` and `docs/en` contain the project-specific content.

## Recommended repository setting

In GitHub Pages settings, switch the source to:

`Deploy from a branch` -> `main` -> `/docs`

## About baseurl

The current config sets `baseurl` to `/Jekyll.Net`, matching the current repository name. If the repository name changes later, update `docs/_config.yml` as well.

## Why this shape

The request here was to reuse the reference documentation shell and visual style directly, while rewriting all content for Jekyll.Net. That is exactly what this site does.
