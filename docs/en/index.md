---
title: "Jekyll.Net Home"
description: "A Jekyll-style static site generator written in C# and aimed at GitHub Pages compatibility."
permalink: /en/
layout: structured
lang: "en"
nav_key: "docs"
hero:
  eyebrow: "GitHub Pages · Jekyll-compatible · .NET 10"
  title: "A .NET static site generator moving toward GitHub Pages behavior"
  lead: "Jekyll.Net is built with C# and .NET 10 and is gradually filling in the core pieces most project documentation sites need: Front Matter, Layouts, Includes, Collections, Tags, Categories, and Sass."
  primary_label: "Start reading"
  primary_url: /en/navigation/
  secondary_label: "Browse sample-site"
  secondary_url: https://github.com/IoTSharp/Jekyll.Net/tree/main/sample-site
  tertiary_label: "Read README"
  tertiary_url: https://github.com/IoTSharp/Jekyll.Net/blob/main/README.md
  tags:
    - ".NET 10"
    - "GitHub Pages"
    - "Jekyll-style"
    - "Markdown + Sass"
  code_title: "Quick Start"
  code: |
    dotnet run --project .\JekyllNet.Cli -- build --source .\sample-site
  note_title: "In one sentence"
  note_text: "This project is not wrapping Jekyll itself. It is rebuilding a GitHub-Pages-style static generation path in C# piece by piece."
stats:
  - label: "Runtime"
    value: ".NET 10"
  - label: "Current entry"
    value: "CLI build"
  - label: "Site features"
    value: "Markdown + Liquid subset"
  - label: "Direction"
    value: "Closer to GitHub Pages"
sections:
  - title: "What already works"
    description: "The repository already covers the core pieces needed by a small GitHub Pages style documentation site."
    variant: "cards"
    columns: 3
    section_class: ""
    items:
      - title: "Content parsing"
        description: "Supports _config.yml, YAML Front Matter, Markdown to HTML, and a basic set of tags and filters."
      - title: "Templating"
        description: "Supports _layouts, nested layouts, _includes, _data, and output-enabled collections."
      - title: "Site assets"
        description: "Supports Sass or SCSS compilation and copies static assets into the _site output directory."
  - title: "Recommended entry points"
    description: "If this is your first time in the repository, these are the fastest useful pages to open."
    variant: "quick-links"
    columns: 4
    section_class: ""
    items:
      - title: "Documentation map"
        description: "Pick the page that matches your goal."
        url: /Jekyll.Net/en/navigation/
      - title: "Getting started"
        description: "Build the sample site once and inspect the output."
        url: /Jekyll.Net/en/getting-started/
      - title: "GitHub Pages"
        description: "See how the new docs folder is meant to be published."
        url: /Jekyll.Net/en/github-pages/
      - title: "Compatibility"
        description: "Understand what is already implemented and what is still missing."
        url: /Jekyll.Net/en/compatibility/
  - title: "The shortest path"
    description: "If you only want a quick read of the current project state, use this path."
    variant: "list"
    columns: 1
    section_class: "panel panel-section"
    items:
      - label: "Start with README to confirm the project goals and limits"
        description: "The repository front page already lists the main capability boundaries."
        url: https://github.com/IoTSharp/Jekyll.Net/blob/main/README.md
      - label: "Run one build with sample-site"
        description: "Inspect the generated _site output directly."
        url: /Jekyll.Net/en/getting-started/
      - label: "Open the documentation map and follow the page that matches your goal"
        description: "Docs, compatibility, and Pages setup all branch from there."
        url: /Jekyll.Net/en/navigation/
---
