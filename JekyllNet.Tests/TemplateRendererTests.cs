using JekyllNet.Core.Rendering;

namespace JekyllNet.Tests;

public sealed class TemplateRendererTests
{
    private readonly TemplateRenderer _renderer = new();

    [Fact]
    public void AssignInsideConditionalBranch_IsVisibleLaterInSameTemplate()
    {
        const string template = """
            {% if page.is_en %}
            {% assign label = 'English' %}
            {% else %}
            {% assign label = '中文' %}
            {% endif %}
            {{ label }}
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["page"] = new Dictionary<string, object?>
            {
                ["is_en"] = false
            }
        });

        Assert.Equal("中文", output.Trim());
    }

    [Fact]
    public void IncludeInsideFalseBranch_IsNotExpanded()
    {
        const string template = """
            {% if false %}
              {% include hidden.html %}
            {% endif %}
            visible
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>(), new Dictionary<string, string>
        {
            ["hidden.html"] = "hidden"
        });

        Assert.Equal("visible", output.Trim());
    }

    [Fact]
    public void NestedForAndIf_RenderStably()
    {
        const string template = """
            {% for item in page.items %}
            {% if item.visible %}
            {{ item.title }}
            {% endif %}
            {% endfor %}
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["page"] = new Dictionary<string, object?>
            {
                ["items"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["title"] = "alpha", ["visible"] = true },
                    new Dictionary<string, object?> { ["title"] = "beta", ["visible"] = false },
                    new Dictionary<string, object?> { ["title"] = "gamma", ["visible"] = true }
                }
            }
        });

        Assert.Equal("alpha\ngamma", string.Join('\n', output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)));
    }

    [Fact]
    public void AssignInsideForLoop_PersistsAfterLoop_WhileLoopVariableDoesNotLeak()
    {
        const string template = """
            {% assign selected = 'none' %}
            {% for item in page.items %}
            {% assign selected = item.title %}
            {% endfor %}
            {{ selected }}|{{ item | default: 'missing' }}
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["page"] = new Dictionary<string, object?>
            {
                ["items"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["title"] = "alpha" },
                    new Dictionary<string, object?> { ["title"] = "omega" }
                }
            }
        });

        Assert.Equal("omega|missing", output.Trim());
    }

    [Fact]
    public void IncludeInsideForLoop_UsesCurrentIterationValue()
    {
        const string template = """
            {% for item in page.items %}
            {% include row.html title=item.title %}
            {% endfor %}
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["page"] = new Dictionary<string, object?>
            {
                ["items"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["title"] = "alpha" },
                    new Dictionary<string, object?> { ["title"] = "beta" }
                }
            }
        }, new Dictionary<string, string>
        {
            ["row.html"] = "[{{ include.title }}]"
        });

        Assert.Equal("[alpha][beta]", string.Concat(output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)));
    }

    [Fact]
    public void ForLoop_SupportsLimitOffsetAndMetadata()
    {
        const string template = """
            {% for item in page.items offset:1 limit:2 %}
            {{ forloop.index }}-{{ forloop.length }}-{{ forloop.first }}-{{ forloop.last }}:{{ item.title }}
            {% endfor %}
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["page"] = new Dictionary<string, object?>
            {
                ["items"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["title"] = "alpha" },
                    new Dictionary<string, object?> { ["title"] = "beta" },
                    new Dictionary<string, object?> { ["title"] = "gamma" },
                    new Dictionary<string, object?> { ["title"] = "delta" }
                }
            }
        });

        Assert.Equal(
            "1-2-True-False:beta\n2-2-False-True:gamma",
            string.Join('\n', output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)));
    }

    [Fact]
    public void ForLoop_Reversed_RendersItemsInReverseOrder()
    {
        const string template = """
            {% for item in page.items reversed %}
            {{ item.title }}
            {% endfor %}
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["page"] = new Dictionary<string, object?>
            {
                ["items"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["title"] = "alpha" },
                    new Dictionary<string, object?> { ["title"] = "beta" },
                    new Dictionary<string, object?> { ["title"] = "gamma" }
                }
            }
        });

        Assert.Equal("gamma\nbeta\nalpha", string.Join('\n', output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)));
    }

    [Fact]
    public void RelativeAbsoluteAndMarkdownifyFilters_RenderExpectedOutput()
    {
        const string template = """
            {{ '/docs/' | relative_url }}
            {{ '/docs/' | absolute_url }}
            {{ '# Heading' | markdownify }}
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["site"] = new Dictionary<string, object?>
            {
                ["baseurl"] = "/JekyllNet",
                ["url"] = "https://example.com"
            }
        });

        Assert.Contains("/JekyllNet/docs/", output, StringComparison.Ordinal);
        Assert.Contains("https://example.com/JekyllNet/docs/", output, StringComparison.Ordinal);
        Assert.Contains("<h1", output, StringComparison.Ordinal);
        Assert.Contains("Heading</h1>", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RelativeAbsoluteFilters_ResolveMixedCaseNestedSiteKeys()
    {
        const string template = """
            {{ '/docs/' | relative_url }}
            {{ '/docs/' | absolute_url }}
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["Site"] = new Dictionary<string, object?>
            {
                ["BaseUrl"] = "/JekyllNet",
                ["Url"] = "https://example.com"
            }
        });

        Assert.Contains("/JekyllNet/docs/", output, StringComparison.Ordinal);
        Assert.Contains("https://example.com/JekyllNet/docs/", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_RendersInnerTemplateAndStoresResult()
    {
        const string template = """
            {% assign label = 'Docs' %}
            {% capture summary %}Open {{ label | downcase }}{% endcapture %}
            {{ summary }}
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>());

        Assert.Equal("Open docs", output.Trim());
    }

    [Fact]
    public void CaseWhen_SelectsMatchingBranch()
    {
        const string template = """
            {% case page.lang %}
            {% when 'en' %}
            English
            {% when 'zh' %}
            中文
            {% else %}
            Unknown
            {% endcase %}
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["page"] = new Dictionary<string, object?>
            {
                ["lang"] = "zh"
            }
        });

        Assert.Equal("中文", output.Trim());
    }

    [Fact]
    public void CaseWhen_UsesElseWhenNoBranchMatches()
    {
        const string template = """
            {% case page.section %}
            {% when 'docs' %}
            Docs
            {% else %}
            Other
            {% endcase %}
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["page"] = new Dictionary<string, object?>
            {
                ["section"] = "blog"
            }
        });

        Assert.Equal("Other", output.Trim());
    }

    [Fact]
    public void WhereMapAndCompact_FilterCollectionPipeline()
    {
        const string template = """
            {% assign names = site.pages | where: "lang", "en" | map: "title" | compact %}
            {% for name in names %}
            {{ name }}
            {% endfor %}
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["site"] = new Dictionary<string, object?>
            {
                ["pages"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["title"] = "Home", ["lang"] = "en" },
                    new Dictionary<string, object?> { ["title"] = null, ["lang"] = "en" },
                    new Dictionary<string, object?> { ["title"] = "首页", ["lang"] = "zh" }
                }
            }
        });

        Assert.Equal("Home", output.Trim());
    }

    [Fact]
    public void Sort_OrdersCollectionByProperty()
    {
        const string template = """
            {% assign pages = site.pages | sort: "order" %}
            {% for page_item in pages %}
            {{ page_item.title }}
            {% endfor %}
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["site"] = new Dictionary<string, object?>
            {
                ["pages"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["title"] = "Third", ["order"] = 3 },
                    new Dictionary<string, object?> { ["title"] = "First", ["order"] = 1 },
                    new Dictionary<string, object?> { ["title"] = "Second", ["order"] = 2 }
                }
            }
        });

        Assert.Equal("First\nSecond\nThird", string.Join('\n', output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)));
    }

    [Fact]
    public void JsonifyAndSlugify_RenderExpectedOutput()
    {
        const string template = """
            {{ page.title | slugify }}
            {{ site.data | jsonify }}
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["page"] = new Dictionary<string, object?>
            {
                ["title"] = "Hello, Jekyll Net!"
            },
            ["site"] = new Dictionary<string, object?>
            {
                ["data"] = new Dictionary<string, object?>
                {
                    ["name"] = "JekyllNet",
                    ["count"] = 2
                }
            }
        });

        Assert.Contains("hello-jekyll-net", output, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"JekyllNet\"", output, StringComparison.Ordinal);
        Assert.Contains("\"count\":2", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceFilter_DoesNotThrowOnDanglingQuoteArgument()
    {
        const string template = """
            {{ page.title | replace: "'", "" }}
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["page"] = new Dictionary<string, object?>
            {
                ["title"] = "It's working"
            }
        });

        Assert.Contains("Its working", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CommentBlock_SkipsNestedLiquidContent()
    {
        const string template = """
            {% comment %}
            {% include hidden.html %}
            {{ page.title }}
            {% endcomment %}
            visible
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["page"] = new Dictionary<string, object?>
            {
                ["title"] = "Hidden"
            }
        }, new Dictionary<string, string>
        {
            ["hidden.html"] = "should-not-render"
        });

        Assert.Equal("visible", output.Trim());
    }

    [Fact]
    public void Where_WithSinglePropertyArgument_FiltersTruthyItems()
    {
        const string template = """
            {% assign visible_pages = site.pages | where: "published" %}
            {% for page_item in visible_pages %}
            {{ page_item.title }}
            {% endfor %}
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["site"] = new Dictionary<string, object?>
            {
                ["pages"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["title"] = "Public", ["published"] = true },
                    new Dictionary<string, object?> { ["title"] = "Hidden", ["published"] = false },
                    new Dictionary<string, object?> { ["title"] = "Unset" }
                }
            }
        });

        Assert.Equal("Public", output.Trim());
    }

    [Fact]
    public void Map_SupportsNestedPropertyPaths()
    {
        const string template = """
            {% assign names = site.pages | map: "author.name" %}
            {% for name in names %}
            {{ name }}
            {% endfor %}
            """;

        var output = _renderer.Render(template, new Dictionary<string, object?>
        {
            ["site"] = new Dictionary<string, object?>
            {
                ["pages"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["author"] = new Dictionary<string, object?> { ["name"] = "Ava" } },
                    new Dictionary<string, object?> { ["author"] = new Dictionary<string, object?> { ["name"] = "Bo" } }
                }
            }
        });

        Assert.Equal("Ava\nBo", string.Join('\n', output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)));
    }
}
