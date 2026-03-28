---
title: "Deployment"
description: "Publish the docs directory as a GitHub Pages source site or mirror its workflow in CI."
permalink: /en/github-pages/
lang: "en"
nav_key: "docs"
---
# Deployment

The repository now includes a `docs` directory shaped to work as a GitHub Pages style source site. It also serves as one of the project's real build fixtures, which makes it a useful reference for your own documentation repositories.

## Directory role

Inside `docs` you will find:

- `docs/_config.yml` for site-level settings, locales, navigation labels, and shared defaults
- `docs/_layouts` and `docs/_includes` for the shell
- `docs/assets` for the visual layer
- `docs/zh` and `docs/en` for mirrored bilingual content

## Custom domain settings

The site is now configured for:

```yml
url: https://jekyllnet.help
baseurl: ""
```

Because the site uses a custom domain, `baseurl` is intentionally empty. If the domain changes later, update both values together.

The repo now also carries `docs/CNAME` with `jekyllnet.help`.

## Basic GitHub Pages publishing shape

If you want GitHub to publish the source folder directly, use:

`Deploy from a branch` -> `main` -> `/docs`

That keeps the repository easy to inspect because the source and the published Pages site live side by side.

## When to use Actions instead

If you want a more explicit build pipeline, the repository now also includes a reusable JekyllNet build action. That route is useful when you want to:

- build with a pinned .NET SDK in CI
- publish generated artifacts instead of raw source
- reuse a workflow for package or release automation

Minimal example:

```yml
jobs:
  build:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4
      - uses: JekyllNet/JekyllNet@main
        with:
          source: ./docs
          destination: ./artifacts/docs-site
          upload-artifact: "true"
          artifact-name: docs-site
```

The repository does not publish a dedicated action tag yet, so the example uses `@main` for now. Move to a release tag once one is available.

## Built-in Pages workflow in this repository

This repository now also includes `.github/workflows/github-pages.yml`.

That workflow:

- builds `./docs` with the local JekyllNet action
- uploads `./artifacts/docs-site` as the GitHub Pages artifact
- deploys on pushes to `main`
- runs on pull requests as a build check, without publishing

It is wired to changes in `docs/**`, `JekyllNet.Cli/**`, `JekyllNet.Core/**`, `action.yml`, `JekyllNet.slnx`, and the workflow file itself, so documentation and generator changes both refresh the published site.

The related workflow guidance is summarized in [CLI and Development Workflow](/en/blog/cli-workflow/).
---
