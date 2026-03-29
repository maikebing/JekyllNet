---
title: "Documentation Map"
description: "Jump into the JekyllNet docs based on your goal."
permalink: /en/navigation/
layout: structured
lang: "en"
nav_key: "docs"
intro:
  eyebrow: "Documentation Map"
  title: "Documentation Map"
  description: "Use the docs pages for orientation and setup, then move into the blog for deeper writing on features, configuration, CLI workflows, and AI-powered localization."
sections:
  - title: "First stops"
    description: "These pages are the fastest route from zero context to a successful local run."
    variant: "quick-links"
    columns: 4
    section_class: ""
    items:
      - title: "Home"
        description: "Project identity, current capability range, and recommended reading paths."
        url: /en/
      - title: "Getting started"
        description: "Run sample-site or docs locally and inspect the generated output."
        url: /en/getting-started/
      - title: "Deployment"
        description: "Publish the docs folder or align a workflow around the generated site."
        url: /en/github-pages/
      - title: "Compatibility"
        description: "See the implemented, partial, and intentionally out-of-scope areas."
        url: /en/compatibility/
  - title: "Choose by topic"
    description: "The blog now carries the detailed documentation."
    variant: "quick-links"
    columns: 4
    section_class: ""
    items:
      - title: "Complete usage"
        description: "The end-to-end handbook from setup and preview to release and troubleshooting."
        url: /en/blog/complete-usage-guide/
      - title: "Features"
        description: "Rendering, publishing semantics, filters, pagination, and site-building behavior."
        url: /en/blog/feature-overview/
      - title: "Configuration"
        description: "Site options, defaults, include or exclude rules, footer metadata, analytics, and pagination config."
        url: /en/blog/configuration-guide/
      - title: "CLI workflow"
        description: "Build, watch, serve, dotnet tool packaging, GitHub Actions, and local preview."
        url: /en/blog/cli-workflow/
      - title: "AI translation"
        description: "Locales, translation links, provider selection, cache, incremental translation, and glossary."
        url: /en/blog/ai-translation/
  - title: "Choose by function"
    description: "A fast route based on team role or delivery phase."
    variant: "quick-links"
    columns: 4
    section_class: ""
    items:
      - title: "Setup and onboarding"
        description: "For first-time contributors and quick local validation."
        url: /en/getting-started/
      - title: "Authoring and preview"
        description: "For daily content and template development loops."
        url: /en/blog/cli-workflow/
      - title: "Deployment and CI"
        description: "For GitHub Pages and automation owners."
        url: /en/github-pages/
      - title: "Release updates"
        description: "For version tracking and external communication."
        url: /en/news/
  - title: "Choose by intent"
    description: "Different contributors usually start with different questions."
    variant: "cards"
    columns: 3
    section_class: ""
    items:
      - title: "I want to try it today"
        description: "Start with getting started, then keep feature overview open while comparing the generated output to sample-site."
      - title: "I want theme compatibility"
        description: "Read compatibility first, then move to the feature overview and configuration guide for the details behind that matrix."
      - title: "I want multilingual docs"
        description: "Read the AI translation article and inspect how the docs folder mirrors English and Chinese routes."
  - title: "Release context"
    description: "If you want to understand the current maturity quickly, read these next."
    variant: "list"
    columns: 1
    section_class: "panel panel-section"
    items:
      - label: "v0.2.5 release"
        description: "The latest release announcement with theme support, config, and release pipeline updates."
        url: /en/news/v0-2-5/
      - label: "Launch announcement"
        description: "The first-release news post summarizes what shipped on March 25, 2026."
        url: /en/news/project-status/
      - label: "Design goals"
        description: "Why JekyllNet now has a Pages-style documentation site of its own."
        url: /en/blog/design-goals/
      - label: "About this site"
        description: "How this docs source was organized and why docs, blog, and news are separated."
        url: /en/about/
---
