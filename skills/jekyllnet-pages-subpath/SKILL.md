---
name: jekyllnet-pages-subpath
description: Ensure Jekyll/JekyllNet GitHub Pages deployments use correct second-level baseurl logic and not just workflow changes.
user-invocable: true
---

# JekyllNet Pages Subpath

## Purpose

Ensure a Jekyll/JekyllNet site is published correctly on GitHub Pages, with special handling for project pages under second-level paths such as `/minimal-mistakes/`.

This skill must not only adjust build workflow steps, but also decide whether the repository requires second-level directory deployment before editing config files.

## When to use

Use this skill when any of the following is requested:

- migrate a Jekyll site to JekyllNet build on GitHub Pages
- fix broken static asset links after publishing to `jekyllnet.github.io/<repo>/`
- update a theme repo for project-pages deployment
- verify whether `baseurl` should be empty or `/<repo>`

## Inputs you need

- GitHub owner and repository name
- target publishing host (for example `https://jekyllnet.github.io`)
- site source directory (`.` or `docs`)
- whether a custom domain (`CNAME`) is used

## Decision rules

### Rule 1: Determine page type

- If repository is `<owner>.github.io`:
  - Treat as user/org page (root page)
  - default `baseurl` should be `""`
- Otherwise:
  - Treat as project page
  - default `baseurl` should be `"/<repo>"`

### Rule 2: Determine URL

- If custom domain is used (`CNAME` exists and is intended for production):
  - `url` should be custom domain (`https://your.domain`)
  - `baseurl` usually `""` unless intentionally hosted under subpath on same domain
- If no custom domain and host is GitHub Pages:
  - `url` should be `https://<owner>.github.io`
  - `baseurl` should follow Rule 1

### Rule 3: Determine config file to patch

- If workflow builds from repo root (`source: .`): patch `_config.yml`
- If workflow builds from docs folder (`source: ./docs`): patch `docs/_config.yml`
- If both are used by different workflows, patch both

## Required change checklist

1. Build workflow

- Ensure workflow uses `JekyllNet/action@v2`
- Ensure `source` and `destination` are explicit and consistent
- For pages artifact workflow, upload correct destination folder

2. Site config

- Ensure `url` and `baseurl` follow decision rules
- Ensure `repository` is set to `<owner>/<repo>` where theme expects it
- Avoid conflicting duplicate definitions across root and docs config

3. Runtime compatibility check

- verify generated links contain expected prefix:
  - project page: `/<repo>/...`
  - root page: `/...`
- check CSS/JS/image URLs are not accidentally rooted to `/` when project page requires `/<repo>/`

## Validation procedure (must run)

1. Static build validation

- Run local build via JekyllNet for the same source path as CI
- Confirm output contains expected `index.html` and assets

2. Published URL validation

For each deployed site URL:

- check `https://<owner>.github.io/<repo>/` returns success
- inspect HTML for asset links with correct base prefix
- confirm no obvious 404 due to missing base path

3. Failure handling

If validation fails:

- first fix `url/baseurl/repository`
- then fix workflow source/destination mismatch
- re-run build and re-check published URL

## Quick examples

### Project page example

Repository: `JekyllNet/minimal-mistakes`

- `url: "https://jekyllnet.github.io"`
- `baseurl: "/minimal-mistakes"`

### Root page example

Repository: `JekyllNet/jekyllnet.github.io`

- `url: "https://jekyllnet.github.io"`
- `baseurl: ""`

## Anti-patterns to avoid

- Only changing workflow without changing `url/baseurl`
- Forcing `baseurl: ""` on project pages
- Hardcoding absolute root asset paths (`/assets/...`) on project pages without prefix handling
- Updating one config file while workflow reads another config file

## Output expectation

A completed run using this skill should produce:

- corrected workflow files
- corrected config file(s)
- deployment URL check result per repository
- a short summary of what was fixed and why
