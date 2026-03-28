---
title: "CLI and Development Workflow"
description: "How build, watch, serve, packaging, and CI fit together in JekyllNet."
permalink: /en/blog/cli-workflow/
lang: "en"
nav_key: "blog"
---
# CLI and Development Workflow

JekyllNet now has a fuller command-line story. That matters because a generator becomes much easier to evaluate once local iteration, preview, packaging, and CI all fit together.

## The core commands

```powershell
dotnet run --project .\JekyllNet.Cli -- build --source .\sample-site
dotnet run --project .\JekyllNet.Cli -- watch --source .\docs
dotnet run --project .\JekyllNet.Cli -- serve --source .\docs --port 5055
```

- `build` is the deterministic generation step.
- `watch` is for content or template editing loops.
- `serve` is for local preview with a stable HTTP endpoint.

## Tooling and distribution

The repository now also includes:

- `dotnet tool` packaging metadata for the CLI
- a reusable GitHub Action for site builds
- GitHub Actions for CI, GitHub Pages, NuGet publishing, and release artifacts
- winget packaging templates
- README guidance for installation and upgrades

## Reusable GitHub Action

The standalone `JekyllNet/action` repository now exposes a build action, so another repository can call JekyllNet without copying the workflow steps by hand.

```yml
jobs:
  build:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v5
      - uses: JekyllNet/action@main
        with:
          source: ./docs
          destination: ./artifacts/docs-site
          upload-artifact: "true"
          artifact-name: docs-site
```

The repository does not publish a dedicated action tag yet, so the example uses `@main` for now. Once the first action release exists, pin to that tag instead.

The most useful inputs are `source`, `destination`, `drafts`, `future`, `unpublished`, `posts-per-page`, `dotnet-configuration`, and the optional artifact upload controls.

## NuGet publishing workflow

The repository also now carries `.github/workflows/publish-dotnet-tool.yml` for publishing the CLI as the `JekyllNet` global tool package to both NuGet.org and GitHub Packages.

That workflow:

- triggers on `v*` tags
- also supports manual dispatch with an explicit version input
- runs `dotnet test` before packing
- packs with the resolved version instead of relying on the static project file version
- pushes to NuGet.org with the repository secret `NUGET_API_KEY`
- also pushes the same package to `https://nuget.pkg.github.com/JekyllNet/index.json` with `GITHUB_TOKEN`

## A practical local routine

1. Run `watch` against the site you are editing.
2. Use `serve` when you want a stable preview URL.
3. Run `dotnet test .\JekyllNet.slnx` before landing changes.
---
