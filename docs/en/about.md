---
title: "About This Site"
description: "How the JekyllNet docs site is organized and why the content is split across docs, blog, and news."
permalink: /en/about/
lang: "en"
nav_key: "about"
---
# About This Site

This `docs` directory is now a real project surface, not a placeholder.

## Why the information architecture looks like this

The site is intentionally split into three layers:

- **Docs** for orientation, setup, compatibility boundaries, and deployment shape
- **Blog** for detailed writing organized by feature area, configuration, workflow, and localization
- **News** for release-oriented updates such as the first public launch

That keeps entry pages short while still letting the project accumulate serious documentation over time.

## Why it matters technically

The docs site also acts as a fixture for the generator itself. It exercises:

- bilingual routing and translation links
- layouts, includes, navigation, and footers
- `_config.yml` defaults and localized UI labels
- fixture-based build regression tests for generated output

## What to read next

If you are here to understand the product rather than the docs shell, move to:

- [Documentation Map](/en/navigation/)
- [Feature Overview](/en/blog/feature-overview/)
- [Launch Announcement](/en/news/project-status/)
---
