using JekyllNet.Core.Translation;

namespace JekyllNet.Tests;

public sealed class SiteBuilderBehaviorTests
{
    [Fact]
    public async Task Build_ExcludesDraftsFutureAndUnpublished_ByDefault()
    {
        var sourceDirectory = CreateContentSiteFixture();
        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);

        Assert.True(File.Exists(Path.Combine(outputDirectory, "index.html")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "blog", "index.html")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "2000", "01", "02", "published", "index.html")));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "2099", "01", "01", "future", "index.html")));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "2000", "01", "03", "unpublished", "index.html")));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "2000", "01", "04", "draft-entry", "index.html")));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "_config.yml")));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "_posts", "2099-01-01-future.md")));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "_drafts", "draft-entry.md")));
    }

    [Fact]
    public async Task Build_IncludesDraftsFutureAndUnpublished_WhenEnabled()
    {
        var sourceDirectory = CreateContentSiteFixture();
        var outputDirectory = await TestInfrastructure.BuildSiteAsync(
            sourceDirectory,
            includeDrafts: true,
            includeFuture: true,
            includeUnpublished: true);

        Assert.True(File.Exists(Path.Combine(outputDirectory, "2099", "01", "01", "future", "index.html")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "2000", "01", "03", "unpublished", "index.html")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "2000", "01", "04", "draft-entry", "index.html")));
    }

    [Fact]
    public async Task Build_RespectsExcerptSeparator()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                excerpt_separator: <!--more-->
                show_excerpts: true
                """,
            ["_layouts/default.html"] = """
                {{ content }}
                """,
            ["_posts/2000-01-02-excerpted.md"] = """
                ---
                layout: default
                title: Excerpted
                ---
                Intro paragraph.
                <!--more-->
                Rest of the article.
                """,
            ["index.html"] = """
                ---
                layout: default
                ---
                {{ site.posts | map: "excerpt" | first }}
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);
        var output = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index.html"));

        Assert.Contains("Intro paragraph.", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Rest of the article.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_RendersLiquidBeforeMarkdown_ForMarkdownPages()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_layouts/default.html"] = """
                {{ content }}
                """,
            ["_includes/feature_row.html"] = """
                <div class="feature__wrapper">{{ page.title }}</div>
                """,
            ["index.md"] = """
                ---
                layout: default
                title: Home
                excerpt: Front matter summary
                ---

                {% include feature_row.html %}
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);
        var output = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index.html"));

        Assert.Contains("""<div class="feature__wrapper">Home</div>""", output, StringComparison.Ordinal);
        Assert.DoesNotContain("<p><div", output, StringComparison.Ordinal);
        Assert.DoesNotContain("<pre><code>&lt;div", output, StringComparison.Ordinal);
        Assert.DoesNotContain("""content="<p>{% include""", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_AppliesFrontMatterAndDefaultsToStaticFiles()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                defaults:
                  - scope:
                      path: assets
                    values:
                      custom_label: from-default
                """,
            ["index.html"] = """
                ---
                ---
                {{ site.static_files | where: "name", "app.js" | map: "custom_label" | first }}
                """,
            ["assets/app.js"] = """
                ---
                title: App Asset
                ---
                console.log("{{ page.title }} {{ page.custom_label }}");
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);
        var indexOutput = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index.html"));
        var scriptOutput = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "assets", "app.js"));

        Assert.Contains("from-default", indexOutput, StringComparison.Ordinal);
        Assert.Contains("console.log(\"App Asset from-default\");", scriptOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("---", scriptOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_GeneratesPaginationPages()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                paginate: 2
                paginate_path: /blog/page:num/
                """,
            ["_layouts/default.html"] = """
                {{ content }}
                """,
            ["blog/index.html"] = """
                ---
                layout: default
                permalink: /blog/
                ---
                page={{ paginator.page }}/{{ paginator.total_pages }}
                {% for post in paginator.posts %}
                {{ post.title }}
                {% endfor %}
                next={{ paginator.next_page_path }}
                prev={{ paginator.previous_page_path }}
                """,
            ["_posts/2000-01-01-first.md"] = """
                ---
                layout: default
                title: First
                ---
                First
                """,
            ["_posts/2000-01-02-second.md"] = """
                ---
                layout: default
                title: Second
                ---
                Second
                """,
            ["_posts/2000-01-03-third.md"] = """
                ---
                layout: default
                title: Third
                ---
                Third
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);
        var page1 = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "blog", "index.html"));
        var page2 = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "blog", "page2", "index.html"));

        Assert.Contains("page=1/2", page1, StringComparison.Ordinal);
        Assert.Contains("Third", page1, StringComparison.Ordinal);
        Assert.Contains("Second", page1, StringComparison.Ordinal);
        Assert.DoesNotContain("First", page1, StringComparison.Ordinal);
        Assert.Contains("next=/blog/page2/", page1, StringComparison.Ordinal);

        Assert.Contains("page=2/2", page2, StringComparison.Ordinal);
        Assert.Contains("First", page2, StringComparison.Ordinal);
        Assert.Contains("prev=/blog/", page2, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_SupportsNestedPaginationConfigurationKeys()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                pagination:
                  per_page: 2
                  path: /updates/page:num/
                """,
            ["_layouts/default.html"] = """
                {{ content }}
                """,
            ["updates/index.html"] = """
                ---
                layout: default
                permalink: /updates/
                ---
                page={{ paginator.page }}/{{ paginator.total_pages }}
                {% for post in paginator.posts %}
                {{ post.title }}
                {% endfor %}
                next={{ paginator.next_page_path }}
                prev={{ paginator.previous_page_path | default: "none" }}
                """,
            ["_posts/2000-01-01-first.md"] = """
                ---
                layout: default
                title: First
                ---
                First
                """,
            ["_posts/2000-01-02-second.md"] = """
                ---
                layout: default
                title: Second
                ---
                Second
                """,
            ["_posts/2000-01-03-third.md"] = """
                ---
                layout: default
                title: Third
                ---
                Third
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);
        var page1 = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "updates", "index.html"));
        var page2 = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "updates", "page2", "index.html"));

        Assert.Contains("page=1/2", page1, StringComparison.Ordinal);
        Assert.Contains("next=/updates/page2/", page1, StringComparison.Ordinal);
        Assert.Contains("prev=none", page1, StringComparison.Ordinal);
        Assert.Contains("page=2/2", page2, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_PaginatesOnlyHtmlIndexPages_AndSkipsHiddenPosts()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                paginate: 2
                paginate_path: /blog/page:num/
                """,
            ["_layouts/default.html"] = """
                <html>
                <body>
                {{ content }}
                </body>
                </html>
                """,
            ["blog/index.html"] = """
                ---
                layout: default
                permalink: /blog/
                ---
                page={{ paginator.page }}/{{ paginator.total_pages }}
                {% for post in paginator.posts %}
                {{ post.title }}
                {% endfor %}
                """,
            ["notes/index.md"] = """
                ---
                layout: default
                permalink: /notes/
                ---
                page={{ paginator.page | default: "none" }}
                """,
            ["_posts/2000-01-01-first.md"] = """
                ---
                layout: default
                title: First
                ---
                First
                """,
            ["_posts/2000-01-02-second.md"] = """
                ---
                layout: default
                title: Second
                ---
                Second
                """,
            ["_posts/2000-01-03-third.md"] = """
                ---
                layout: default
                title: Third
                ---
                Third
                """,
            ["_posts/2000-01-04-hidden.md"] = """
                ---
                layout: default
                title: Hidden
                hidden: true
                ---
                Hidden
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);
        var blogPage1 = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "blog", "index.html"));
        var blogPage2 = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "blog", "page2", "index.html"));
        var notesPage = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "notes", "index.html"));

        Assert.Contains("page=1/2", blogPage1, StringComparison.Ordinal);
        Assert.Contains("Third", blogPage1, StringComparison.Ordinal);
        Assert.Contains("Second", blogPage1, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden", blogPage1, StringComparison.Ordinal);

        Assert.Contains("page=2/2", blogPage2, StringComparison.Ordinal);
        Assert.Contains("First", blogPage2, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden", blogPage2, StringComparison.Ordinal);

        Assert.Contains("<html>", notesPage, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(outputDirectory, "notes", "page2", "index.html")));
    }

    [Fact]
    public async Task Build_AllowsDisablingPaginationForSpecificPage()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                paginate: 2
                paginate_path: /blog/page:num/
                """,
            ["_layouts/default.html"] = """
                {{ content }}
                """,
            ["blog/index.html"] = """
                ---
                layout: default
                permalink: /blog/
                ---
                {{ paginator.page | default: "none" }}
                """,
            ["news/index.html"] = """
                ---
                layout: default
                permalink: /news/
                pagination:
                  enabled: false
                ---
                {{ paginator.page | default: "none" }}
                """,
            ["_posts/2000-01-01-first.md"] = """
                ---
                layout: default
                title: First
                ---
                First
                """,
            ["_posts/2000-01-02-second.md"] = """
                ---
                layout: default
                title: Second
                ---
                Second
                """,
            ["_posts/2000-01-03-third.md"] = """
                ---
                layout: default
                title: Third
                ---
                Third
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);
        var newsOutput = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "news", "index.html"));

        Assert.True(File.Exists(Path.Combine(outputDirectory, "blog", "page2", "index.html")));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "news", "page2", "index.html")));
        Assert.Contains("none", newsOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_InsertsConfiguredFooterMetadataAndPolicyLinks()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                baseurl: /portal
                备案号: 京ICP备11021163号-6
                公安备案号: 京公网安备 11010502033607号
                footer:
                  copyright: "© 2011-2024 Umeng.com , All Rights Reserved"
                  telecom_license: 京ICP证120439号
                  terms_url: /terms/
                  privacy_url: /privacy/
                  report_phone: 4009901848
                  report_email: Umeng_Legal@service.umeng.com
                """,
            ["_layouts/default.html"] = """
                <html>
                <body>
                <main>{{ content }}</main>
                </body>
                </html>
                """,
            ["index.md"] = """
                ---
                layout: default
                ---
                Home
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);
        var output = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index.html"));

        Assert.Contains("data-jekyllnet-auto-footer=\"true\"", output, StringComparison.Ordinal);
        Assert.Contains("Umeng.com , All Rights Reserved", output, StringComparison.Ordinal);
        Assert.Contains("href=\"https://beian.miit.gov.cn/\"", output, StringComparison.Ordinal);
        Assert.Contains("京ICP备11021163号-6", output, StringComparison.Ordinal);
        Assert.Contains("href=\"https://beian.mps.gov.cn/#/query/webSearch?code=11010502033607\"", output, StringComparison.Ordinal);
        Assert.Contains("京公网安备 11010502033607号", output, StringComparison.Ordinal);
        Assert.Contains("增值电信业务经营许可证：京ICP证120439号", output, StringComparison.Ordinal);
        Assert.Contains("href=\"/portal/terms/\"", output, StringComparison.Ordinal);
        Assert.Contains(">服务条款<", output, StringComparison.Ordinal);
        Assert.Contains("href=\"/portal/privacy/\"", output, StringComparison.Ordinal);
        Assert.Contains(">隐私政策<", output, StringComparison.Ordinal);
        Assert.Contains("违法和不良举报电话：4009901848", output, StringComparison.Ordinal);
        Assert.Contains("mailto:Umeng_Legal@service.umeng.com", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_LocalizesFooterLabelsPerPageLanguage()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                备案号: 京ICP备11021163号-6
                公安备案号: 京公网安备 11010502033607号
                footer:
                  telecom_license: 京ICP证120439号
                  terms_url: /terms/
                  privacy_url: /privacy/
                  report_phone: 4009901848
                  report_email: legal@example.com
                """,
            ["_layouts/default.html"] = """
                <html>
                <body>
                {{ content }}
                </body>
                </html>
                """,
            ["en/index.html"] = """
                ---
                layout: default
                lang: en
                ---
                Home
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);
        var output = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "en", "index.html"));

        Assert.Contains("ICP Filing No.: 京ICP备11021163号-6", output, StringComparison.Ordinal);
        Assert.Contains("Public Security Filing No.: 京公网安备 11010502033607号", output, StringComparison.Ordinal);
        Assert.Contains("Value-added Telecom License: 京ICP证120439号", output, StringComparison.Ordinal);
        Assert.Contains(">Terms of Service<", output, StringComparison.Ordinal);
        Assert.Contains(">Privacy Policy<", output, StringComparison.Ordinal);
        Assert.Contains("Report Phone: 4009901848", output, StringComparison.Ordinal);
        Assert.Contains("Report Email:", output, StringComparison.Ordinal);
        Assert.DoesNotContain("服务条款", output, StringComparison.Ordinal);
        Assert.DoesNotContain("隐私政策", output, StringComparison.Ordinal);
        Assert.DoesNotContain("举报邮箱", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_InsertsConfiguredAnalyticsSnippets()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                analytics:
                  google: G-TEST12345
                  baidu: baidu-hm-id
                  cnzz: 30086500
                  "51la": LA-TRACK-001
                """,
            ["_layouts/default.html"] = """
                <html>
                <body>
                {{ content }}
                </body>
                </html>
                """,
            ["index.md"] = """
                ---
                layout: default
                ---
                Home
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);
        var output = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index.html"));

        Assert.Contains("https://www.googletagmanager.com/gtag/js?id=G-TEST12345", output, StringComparison.Ordinal);
        Assert.Contains("gtag('config', 'G-TEST12345');", output, StringComparison.Ordinal);
        Assert.Contains("https://hm.baidu.com/hm.js?baidu-hm-id", output, StringComparison.Ordinal);
        Assert.Contains("_czc.push([\"_setAccount\", \"30086500\"]);", output, StringComparison.Ordinal);
        Assert.Contains("https://w.cnzz.com/c.php?id=30086500&async=1", output, StringComparison.Ordinal);
        Assert.Contains("https://sdk.51.la/js-sdk-pro.min.js", output, StringComparison.Ordinal);
        Assert.Contains("LA.init({ id: \"LA-TRACK-001\", ck: \"LA-TRACK-001\" });", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_PreservesNestedAnalyticsConfiguration_AndUsesUniversalSnippet()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                analytics:
                  provider: google-universal
                  google:
                    tracking_id: UA-TEST123
                    anonymize_ip: true
                """,
            ["_layouts/default.html"] = """
                <html>
                <body>
                {{ site.analytics.provider }}|{{ site.analytics.google.tracking_id }}
                {{ content }}
                </body>
                </html>
                """,
            ["index.md"] = """
                ---
                layout: default
                ---
                Home
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);
        var output = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index.html"));

        Assert.Contains("google-universal|UA-TEST123", output, StringComparison.Ordinal);
        Assert.Contains("https://www.google-analytics.com/analytics.js", output, StringComparison.Ordinal);
        Assert.Contains("ga('create','UA-TEST123','auto');", output, StringComparison.Ordinal);
        Assert.Contains("ga('set', 'anonymizeIp', true);", output, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Collections.Generic.Dictionary", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_CompilesSassEntryFilesWithFrontMatterAndLiquidImports()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                theme_skin: sunrise
                """,
            ["_sass/_theme.scss"] = """
                body { color: $brand; }
                """,
            ["_sass/skins/_sunrise.scss"] = """
                $brand: #123456;
                """,
            ["assets/css/main.scss"] = """
                ---
                search: false
                ---

                @import "skins/{{ site.theme_skin }}";
                @import "theme";
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);
        var output = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "assets", "css", "main.css"));

        Assert.Contains("body", output, StringComparison.Ordinal);
        Assert.Contains("#123456", output, StringComparison.Ordinal);
        Assert.DoesNotContain("@import", output, StringComparison.Ordinal);
        Assert.DoesNotContain("{{ site.theme_skin }}", output, StringComparison.Ordinal);
        Assert.DoesNotContain("---", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_CompilesSassEntryFilesWithCommentOnlyFrontMatter()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["assets/css/main.scss"] = """
                ---
                # Only the main Sass file needs front matter
                ---

                body { color: #123456; }
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);
        var output = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "assets", "css", "main.css"));

        Assert.Contains("#123456", output, StringComparison.Ordinal);
        Assert.DoesNotContain("---", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_CompilesSassImportsRenderedFromIncludes()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_includes/imports.scss.liquid"] = "@import \"./support/support\";",
            ["_sass/support/support.scss"] = "body { color: #abcdef; }",
            ["assets/css/main.scss"] = """
                ---
                ---
                {% include imports.scss.liquid %}
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);
        var output = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "assets", "css", "main.css"));

        Assert.Contains("#abcdef", output, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Build_InvalidSass_ThrowsWithFileContext()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["assets/css/main.scss"] = """
                ---
                ---
                $brand: #123456
                body { color: $brand; }
                """
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => TestInfrastructure.BuildSiteAsync(sourceDirectory));

        Assert.Contains("assets/css/main.scss", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Failed to compile Sass entry", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_AutoTranslatesMarkdownContent_WhenAiConfigured()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                lang: zh-CN
                备案号: 京ICP备11021163号-6
                footer:
                  terms_url: /terms/
                  privacy_url: /privacy/
                  report_phone: 4009901848
                  report_email: legal@example.com
                ai:
                  provider: openai
                  model: gpt-5-mini
                  translate:
                    targets:
                      - fr
                    front_matter_keys:
                      - title
                """,
            ["_layouts/default.html"] = """
                <html>
                <body>
                <h1>{{ page.title }}</h1>
                {{ content }}
                </body>
                </html>
                """,
            ["index.md"] = """
                ---
                layout: default
                title: 欢迎
                lang: zh-CN
                ---
                你好，世界。
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(
            sourceDirectory,
            aiTranslationClient: new FakeAiTranslationClient());
        var translatedOutput = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "fr", "index.html"));

        Assert.Contains("<h1>fr::欢迎</h1>", translatedOutput, StringComparison.Ordinal);
        Assert.Contains("fr::你好，世界。", translatedOutput, StringComparison.Ordinal);
        Assert.Contains("fr::Terms of Service", translatedOutput, StringComparison.Ordinal);
        Assert.Contains("fr::Privacy Policy", translatedOutput, StringComparison.Ordinal);
        Assert.Contains("fr::Report Email", translatedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_AppliesLocaleSpecificThemeDefaults_BeyondZhAndEn()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                locales:
                  - code: fr
                    root: /fr/
                    label: FR
                  - code: en
                    root: /en/
                    label: EN
                defaults:
                  - scope:
                      path: ""
                    values:
                      layout: default
                      ui_home_url: /en/
                      ui_docs_url: /en/docs/
                      ui_switch_label: Language
                      ui_docs_label: Docs
                  - scope:
                      path: fr
                    values:
                      ui_home_url: /fr/
                      ui_docs_url: /fr/docs/
                      ui_switch_label: Langue
                      ui_docs_label: Documentation
                """,
            ["_layouts/default.html"] = """
                <html>
                <body>
                {% assign current_locale = page.locale_code | default: page.lang | split: "-" | first %}
                <nav aria-label="{{ page.ui_switch_label }}">
                {% for locale in site.locales %}
                <a href="{{ locale.root }}" class="{% if current_locale == locale.code %}is-active{% endif %}">{{ locale.label }}</a>
                {% endfor %}
                </nav>
                <a href="{{ page.ui_docs_url }}">{{ page.ui_docs_label }}</a>
                {{ content }}
                </body>
                </html>
                """,
            ["fr/index.md"] = """
                ---
                layout: default
                lang: fr-FR
                ---
                Bonjour
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);
        var output = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "fr", "index.html"));

        Assert.Contains("aria-label=\"Langue\"", output, StringComparison.Ordinal);
        Assert.Contains("href=\"/fr/docs/\">Documentation</a>", output, StringComparison.Ordinal);
        Assert.Contains("href=\"/fr/\" class=\"is-active\">FR</a>", output, StringComparison.Ordinal);
        Assert.Contains("href=\"/en/\" class=\"\">EN</a>", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_GeneratesTranslationLinksAcrossLocaleVariants()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                locales:
                  - code: zh
                    root: /zh/
                    label: 中文
                  - code: en
                    root: /en/
                    label: English
                  - code: fr
                    root: /fr/
                    label: Français
                """,
            ["_layouts/default.html"] = """
                <html>
                <body>
                {% for link in page.translation_links %}
                {{ link.label }}={{ link.url }};
                {% endfor %}
                {{ content }}
                </body>
                </html>
                """,
            ["zh/page.md"] = """
                ---
                layout: default
                lang: zh-CN
                permalink: /zh/page/
                ---
                中文页
                """,
            ["en/page.md"] = """
                ---
                layout: default
                lang: en
                permalink: /en/page/
                ---
                English page
                """,
            ["fr/page.md"] = """
                ---
                layout: default
                lang: fr
                permalink: /fr/page/
                ---
                Page francaise
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);
        var zhOutput = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "zh", "page", "index.html"));

        Assert.Contains("English=/en/page/;", zhOutput, StringComparison.Ordinal);
        Assert.Contains("Français=/fr/page/;", zhOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("中文=/zh/page/;", zhOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_AcceptsOpenAiCompatibleThirdPartyProviderConfig()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                lang: en
                ai:
                  provider: siliconflow
                  base_url: https://api.siliconflow.example
                  model: qwen-translator
                  api_key: test-key
                  translate:
                    targets:
                      - fr
                """,
            ["_layouts/default.html"] = """
                <html>
                <body>
                <h1>{{ page.title }}</h1>
                {{ content }}
                </body>
                </html>
                """,
            ["index.md"] = """
                ---
                layout: default
                title: Hello
                lang: en
                ---
                Welcome
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(
            sourceDirectory,
            aiTranslationClient: new FakeAiTranslationClient());
        var translatedOutput = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "fr", "index.html"));

        Assert.Contains("fr::Hello", translatedOutput, StringComparison.Ordinal);
        Assert.Contains("fr::Welcome", translatedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_ReusesAiTranslationCacheAcrossBuilds()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                lang: en
                ai:
                  provider: openai
                  model: gpt-5-mini
                  api_key: test-key
                  translate:
                    targets:
                      - fr
                    front_matter_keys:
                      - title
                """,
            ["_layouts/default.html"] = """
                <html>
                <body>
                <h1>{{ page.title }}</h1>
                {{ content }}
                </body>
                </html>
                """,
            ["index.md"] = """
                ---
                layout: default
                title: Hello
                lang: en
                ---
                Welcome
                """
        });

        var client = new FakeAiTranslationClient();

        await TestInfrastructure.BuildSiteAsync(sourceDirectory, aiTranslationClient: client);
        var initialCallCount = client.CallCount;
        await TestInfrastructure.BuildSiteAsync(sourceDirectory, aiTranslationClient: client);

        Assert.Equal(9, initialCallCount);
        Assert.Equal(initialCallCount, client.CallCount);
        Assert.True(File.Exists(Path.Combine(sourceDirectory, ".jekyllnet", "translation-cache.json")));
    }

    [Fact]
    public async Task Build_OnlyRetranslatesChangedContent_WhenAiTranslationCacheExists()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                lang: en
                ai:
                  provider: openai
                  model: gpt-5-mini
                  api_key: test-key
                  translate:
                    targets:
                      - fr
                    front_matter_keys:
                      - title
                """,
            ["_layouts/default.html"] = """
                <html>
                <body>
                <h1>{{ page.title }}</h1>
                {{ content }}
                </body>
                </html>
                """,
            ["index.md"] = """
                ---
                layout: default
                title: Hello
                lang: en
                ---
                Welcome
                """
        });

        var client = new FakeAiTranslationClient();

        await TestInfrastructure.BuildSiteAsync(sourceDirectory, aiTranslationClient: client);
        var initialCallCount = client.CallCount;

        File.WriteAllText(
            Path.Combine(sourceDirectory, "index.md"),
            """
            ---
            layout: default
            title: Hello
            lang: en
            ---
            Updated welcome
            """);

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory, aiTranslationClient: client);
        var translatedOutput = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "fr", "index.html"));

        Assert.Equal(9, initialCallCount);
        Assert.Equal(initialCallCount + 1, client.CallCount);
        Assert.Contains("fr::Hello", translatedOutput, StringComparison.Ordinal);
        Assert.Contains("fr::Updated welcome", translatedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_LoadsGlossaryEntriesForAiTranslationRequests()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                lang: en
                ai:
                  provider: openai
                  model: gpt-5-mini
                  api_key: test-key
                  translate:
                    targets:
                      - fr
                    front_matter_keys:
                      - title
                    glossary: _i18n/glossary.yml
                """,
            ["_layouts/default.html"] = """
                <html>
                <body>
                <h1>{{ page.title }}</h1>
                {{ content }}
                </body>
                </html>
                """,
            ["_i18n/glossary.yml"] = """
                terms:
                  GitHub Pages:
                    fr: Pages GitHub
                  JekyllNet: JekyllNet
                """,
            ["index.md"] = """
                ---
                layout: default
                title: GitHub Pages
                lang: en
                ---
                JekyllNet works with GitHub Pages.
                """
        });

        var client = new FakeAiTranslationClient();

        await TestInfrastructure.BuildSiteAsync(sourceDirectory, aiTranslationClient: client);

        Assert.Contains(
            client.Requests,
            request => request.TargetLanguage == "fr"
                && request.GlossaryEntries is { Count: 2 }
                && request.GlossaryEntries.Any(entry => entry.Source == "GitHub Pages" && entry.Target == "Pages GitHub")
                && request.GlossaryEntries.Any(entry => entry.Source == "JekyllNet" && entry.Target == "JekyllNet"));
    }

    [Fact]
    public async Task Build_RespectsConfigExcludeAndInclude()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                exclude:
                  - ignored.md
                include:
                  - .well-known
                """,
            ["index.html"] = """
                ---
                ---
                home
                """,
            ["ignored.md"] = """
                ---
                ---
                ignored
                """,
            [".well-known/security.txt"] = "contact: team@example.com"
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);

        Assert.True(File.Exists(Path.Combine(outputDirectory, "index.html")));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "ignored", "index.html")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, ".well-known", "security.txt")));
    }

    [Fact]
    public async Task Build_UsesConfigPermalinkPatternForPosts()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                permalink: /blog/:year/:month/:day/:title/
                """,
            ["_layouts/default.html"] = """
                {{ content }}
                """,
            ["_posts/2000-01-02-custom-link.md"] = """
                ---
                layout: default
                title: Custom Link
                ---
                custom link
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);

        Assert.True(File.Exists(Path.Combine(outputDirectory, "blog", "2000", "01", "02", "custom-link", "index.html")));
    }

    [Fact]
    public async Task Build_UsesCategoriesTokenInPostPermalinks()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                permalink: /:categories/:title/
                """,
            ["_layouts/default.html"] = """
                {{ content }}
                """,
            ["_posts/2000-01-02-categorized-post.md"] = """
                ---
                layout: default
                title: Categorized Post
                categories:
                  - release notes
                  - v1
                ---
                categorized
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);

        Assert.True(File.Exists(Path.Combine(outputDirectory, "release-notes", "v1", "categorized-post", "index.html")));
    }

    [Fact]
    public async Task Build_UsesCollectionPermalinkPatternWithPathToken()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                collections:
                  docs:
                    output: true
                    permalink: /:collection/:path/
                """,
            ["_layouts/default.html"] = """
                {{ content }}
                """,
            ["_docs/getting-started.md"] = """
                ---
                layout: default
                ---
                docs content
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);

        Assert.True(File.Exists(Path.Combine(outputDirectory, "docs", "getting-started", "index.html")));
    }

    [Fact]
    public async Task Build_UsesInheritedThemeArtifactsForNestedSourceDirectory()
    {
        var rootDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_layouts/default.html"] = """
                <html>
                <head>{% include head.html %}</head>
                <body>{{ content }}</body>
                </html>
                """,
            ["_includes/head.html"] = """
                <title>{{ site.title }}</title>
                """,
            ["_sass/_theme.scss"] = """
                body { color: red; }
                """,
            ["assets/css/main.scss"] = """
                ---
                ---
                @import "theme";
                """,
            ["docs/_config.yml"] = """
                title: Nested Docs
                """,
            ["docs/index.md"] = """
                ---
                layout: default
                ---
                Hello nested docs
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(Path.Combine(rootDirectory, "docs"));
        var html = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index.html"));
        var css = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "assets", "css", "main.css"));

        Assert.Contains("<title>Nested Docs</title>", html, StringComparison.Ordinal);
        Assert.Contains("Hello nested docs", html, StringComparison.Ordinal);
        Assert.Contains("color: red", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_HidesPostExcerptsFromSitePosts_WhenShowExcerptsIsFalse()
    {
        var sourceDirectory = TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                show_excerpts: false
                excerpt_separator: <!--more-->
                """,
            ["_layouts/default.html"] = """
                {{ content }}
                """,
            ["_posts/2000-01-02-excerpted.md"] = """
                ---
                layout: default
                title: Excerpted
                ---
                Intro paragraph.
                <!--more-->
                Rest of the article.
                """,
            ["index.html"] = """
                ---
                layout: default
                ---
                {{ site.posts | map: "excerpt" | first | default: "none" }}
                """
        });

        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);
        var output = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index.html"));

        Assert.Contains("none", output, StringComparison.Ordinal);
    }

    private static string CreateContentSiteFixture()
    {
        return TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                title: Feature Test Site
                """,
            ["_layouts/default.html"] = """
                <html>
                <body>
                {{ content }}
                </body>
                </html>
                """,
            ["index.md"] = """
                ---
                layout: default
                title: Home
                ---
                # Home
                """,
            ["blog/index.md"] = """
                ---
                layout: default
                title: Blog
                ---
                # Blog
                """,
            ["_posts/2000-01-02-published.md"] = """
                ---
                layout: default
                title: Published
                ---
                Published
                """,
            ["_posts/2099-01-01-future.md"] = """
                ---
                layout: default
                title: Future
                ---
                Future
                """,
            ["_posts/2000-01-03-unpublished.md"] = """
                ---
                layout: default
                title: Unpublished
                published: false
                ---
                Unpublished
                """,
            ["_drafts/draft-entry.md"] = """
                ---
                layout: default
                title: Draft
                date: 2000-01-04
                ---
                Draft
                """
        });
    }

    private sealed class FakeAiTranslationClient : IAiTranslationClient
    {
        public int CallCount { get; private set; }

        public List<AiTranslationRequest> Requests { get; } = [];

        public Task<string> TranslateAsync(
            AiTranslationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Requests.Add(request with
            {
                GlossaryEntries = request.GlossaryEntries?.ToList()
            });

            return Task.FromResult($"{request.TargetLanguage}::{request.Text}");
        }
    }
}
