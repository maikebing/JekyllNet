using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using DartSassHost;
using JavaScriptEngineSwitcher.Core;
using JavaScriptEngineSwitcher.Jint;
using Markdig;
using JekyllNet.Core.Models;
using JekyllNet.Core.Parsers;
using JekyllNet.Core.Plugins;
using JekyllNet.Core.Plugins.Loading;
using JekyllNet.Core.Rendering;
using JekyllNet.Core.Translation;
using YamlDotNet.Serialization;

namespace JekyllNet.Core.Services;

public sealed partial class JekyllSiteBuilder
{
    private static string ApplyAutomaticSiteEnhancements(
        string html,
        string outputRelativePath,
        IReadOnlyDictionary<string, object?> siteConfig,
        IReadOnlyDictionary<string, object?>? pageData)
    {
        if (!IsHtmlOutputPath(outputRelativePath) || string.IsNullOrWhiteSpace(html))
        {
            return html;
        }

        var footerHtml = BuildAutomaticFooterHtml(siteConfig, pageData);
        var analyticsHtml = BuildAnalyticsHtml(siteConfig, pageData);
        if (string.IsNullOrWhiteSpace(footerHtml) && string.IsNullOrWhiteSpace(analyticsHtml))
        {
            return html;
        }

        var combined = string.Concat(
            string.IsNullOrWhiteSpace(footerHtml) ? string.Empty : Environment.NewLine + footerHtml,
            string.IsNullOrWhiteSpace(analyticsHtml) ? string.Empty : Environment.NewLine + analyticsHtml,
            Environment.NewLine);

        return InsertBeforeBodyEnd(html, combined);
    }

    private static string BuildAutomaticFooterHtml(
        IReadOnlyDictionary<string, object?> siteConfig,
        IReadOnlyDictionary<string, object?>? pageData)
    {
        var labels = ResolveFooterLabels(siteConfig, pageData);
        var icpLabel = ReadConfigString(siteConfig, "footer.icp_label", "footer.beian_label") ?? labels.IcpLabel;
        var publicSecurityLabel = ReadConfigString(siteConfig, "footer.public_security_label", "footer.public_security_beian_label", "footer.gongan_beian_label") ?? labels.PublicSecurityLabel;
        var telecomLicenseLabel = ReadConfigString(siteConfig, "footer.telecom_license_label", "footer.value_added_telecom_license_label") ?? labels.TelecomLicenseLabel;
        var reportPhoneLabel = ReadConfigString(siteConfig, "footer.report_phone_label") ?? labels.ReportPhoneLabel;
        var reportEmailLabel = ReadConfigString(siteConfig, "footer.report_email_label") ?? labels.ReportEmailLabel;
        var lineOneSegments = new List<string>();
        var lineTwoSegments = new List<string>();

        var copyright = ReadConfigString(siteConfig, "footer.copyright", "copyright");
        if (!string.IsNullOrWhiteSpace(copyright))
        {
            lineOneSegments.Add(HtmlEncode(copyright));
        }

        var icp = ReadConfigString(siteConfig, "footer.icp", "footer.beian", "icp", "\u5907\u6848\u53f7");
        if (!string.IsNullOrWhiteSpace(icp))
        {
            lineOneSegments.Add(BuildExternalLink("https://beian.miit.gov.cn/", PrefixLabeledValue(icp, icpLabel, labels.ValueSeparator)));
        }

        var publicSecurityBeian = ReadConfigString(
            siteConfig,
            "footer.public_security_beian",
            "footer.gongan_beian",
            "footer.police_beian",
            "public_security_beian",
            "gongan_beian",
            "police_beian",
            "\u516c\u5b89\u5907\u6848\u53f7");
        if (!string.IsNullOrWhiteSpace(publicSecurityBeian))
        {
            var publicSecurityUrl = BuildPublicSecurityRecordUrl(publicSecurityBeian);
            var publicSecurityText = PrefixLabeledValue(publicSecurityBeian, publicSecurityLabel, labels.ValueSeparator);
            lineOneSegments.Add(string.IsNullOrWhiteSpace(publicSecurityUrl)
                ? HtmlEncode(publicSecurityText)
                : BuildExternalLink(publicSecurityUrl, publicSecurityText));
        }

        var telecomLicense = ReadConfigString(
            siteConfig,
            "footer.telecom_license",
            "footer.value_added_telecom_license",
            "telecom_license",
            "value_added_telecom_license",
            "\u589e\u503c\u7535\u4fe1\u4e1a\u52a1\u7ecf\u8425\u8bb8\u53ef\u8bc1");
        if (!string.IsNullOrWhiteSpace(telecomLicense))
        {
            lineOneSegments.Add(HtmlEncode(PrefixLabeledValue(telecomLicense, telecomLicenseLabel, labels.ValueSeparator)));
        }

        var termsLink = BuildConfiguredLink(
            siteConfig,
            ReadConfigString(siteConfig, "footer.terms_label", "footer.service_terms_label") ?? labels.TermsLabel,
            "footer.terms_url",
            "footer.service_terms_url",
            "terms_url",
            "service_terms_url");
        if (!string.IsNullOrWhiteSpace(termsLink))
        {
            lineOneSegments.Add(termsLink);
        }

        var privacyLink = BuildConfiguredLink(
            siteConfig,
            ReadConfigString(siteConfig, "footer.privacy_label", "footer.privacy_policy_label") ?? labels.PrivacyLabel,
            "footer.privacy_url",
            "footer.privacy_policy_url",
            "privacy_url",
            "privacy_policy_url");
        if (!string.IsNullOrWhiteSpace(privacyLink))
        {
            lineOneSegments.Add(privacyLink);
        }

        var reportPhone = ReadConfigString(
            siteConfig,
            "footer.report_phone",
            "report_phone",
            "\u8fdd\u6cd5\u548c\u4e0d\u826f\u4e3e\u62a5\u7535\u8bdd");
        if (!string.IsNullOrWhiteSpace(reportPhone))
        {
            lineTwoSegments.Add(HtmlEncode(FormatLabeledValue(reportPhoneLabel, reportPhone, labels.ValueSeparator)));
        }

        var reportEmail = ReadConfigString(
            siteConfig,
            "footer.report_email",
            "report_email",
            "\u4e3e\u62a5\u90ae\u7bb1");
        if (!string.IsNullOrWhiteSpace(reportEmail))
        {
            lineTwoSegments.Add($"{HtmlEncode(FormatLabeledValue(reportEmailLabel, string.Empty, labels.ValueSeparator))}<a href=\"mailto:{HtmlEncode(reportEmail)}\">{HtmlEncode(reportEmail)}</a>");
        }

        if (lineOneSegments.Count == 0 && lineTwoSegments.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>();
        if (lineOneSegments.Count > 0)
        {
            lines.Add($"<p>{string.Join(" | ", lineOneSegments)}</p>");
        }

        if (lineTwoSegments.Count > 0)
        {
            lines.Add($"<p>{string.Join(" | ", lineTwoSegments)}</p>");
        }

        return $"""
<footer class="jekyllnet-site-footer" data-jekyllnet-auto-footer="true">
  {string.Join(Environment.NewLine + "  ", lines)}
</footer>
""";
    }

private static string BuildAnalyticsHtml(
        IReadOnlyDictionary<string, object?> siteConfig,
        IReadOnlyDictionary<string, object?>? pageData)
    {
        var snippets = new List<string>();

        if (pageData is not null && ReadBooleanValue(pageData, "analytics") is false)
        {
            return string.Empty;
        }

        var analyticsProvider = ReadScalarConfigString(siteConfig, "analytics.provider");
        var googleAnalyticsId = ReadGoogleAnalyticsTrackingId(siteConfig);
        var googleAnonymizeIp = ReadConfigBoolean(
            siteConfig,
            "analytics.google.anonymize_ip",
            "analytics.google.anonymizeIp");

        if (!string.IsNullOrWhiteSpace(googleAnalyticsId))
        {
            var escapedId = EscapeJavaScriptString(googleAnalyticsId);
            if (string.Equals(analyticsProvider, "google-universal", StringComparison.OrdinalIgnoreCase))
            {
                snippets.Add($$"""
<script>
window.ga=function(){ga.q.push(arguments)};ga.q=[];ga.l=+new Date;
ga('create','{{escapedId}}','auto');
ga('set', 'anonymizeIp', {{(googleAnonymizeIp ?? false).ToString().ToLowerInvariant()}});
ga('send','pageview');
</script>
<script src="https://www.google-analytics.com/analytics.js" async></script>
""");
            }
            else if (string.Equals(analyticsProvider, "google", StringComparison.OrdinalIgnoreCase))
            {
                snippets.Add($$"""
<script>
var _gaq = window._gaq || [];
window._gaq = _gaq;
_gaq.push(['_setAccount', '{{escapedId}}']);
{{(googleAnonymizeIp is true ? "_gaq.push(['_gat._anonymizeIp']);" : string.Empty)}}
_gaq.push(['_trackPageview']);

(function() {
  var ga = document.createElement('script');
  ga.type = 'text/javascript';
  ga.async = true;
  ga.src = ('https:' == document.location.protocol ? 'https://ssl' : 'http://www') + '.google-analytics.com/ga.js';
  var s = document.getElementsByTagName('script')[0];
  s.parentNode.insertBefore(ga, s);
})();
</script>
""");
            }
            else
            {
                var configArguments = googleAnonymizeIp is null
                    ? $"'{escapedId}'"
                    : $"'{escapedId}', {{ 'anonymize_ip': {(googleAnonymizeIp.Value).ToString().ToLowerInvariant()} }}";
                snippets.Add($$"""
<script async src="https://www.googletagmanager.com/gtag/js?id={{HtmlEncode(googleAnalyticsId)}}"></script>
<script>
window.dataLayer = window.dataLayer || [];
function gtag(){dataLayer.push(arguments);}
gtag('js', new Date());
gtag('config', {{configArguments}});
</script>
""");
            }
        }

        var baiduAnalyticsId = ReadScalarConfigString(
            siteConfig,
            "analytics.baidu",
            "analytics.baidu_tongji",
            "baidu",
            "baidu_tongji");
        if (!string.IsNullOrWhiteSpace(baiduAnalyticsId))
        {
            snippets.Add($$"""
<script>
var _hmt = window._hmt || [];
window._hmt = _hmt;
(function() {
  var hm = document.createElement("script");
  hm.src = "https://hm.baidu.com/hm.js?{{EscapeJavaScriptString(baiduAnalyticsId)}}";
  hm.async = true;
  var s = document.getElementsByTagName("script")[0];
  s.parentNode.insertBefore(hm, s);
})();
</script>
""");
        }

        var cnzzAnalyticsId = ReadScalarConfigString(
            siteConfig,
            "analytics.cnzz",
            "analytics.umeng",
            "cnzz",
            "umeng");
        if (!string.IsNullOrWhiteSpace(cnzzAnalyticsId))
        {
            var escapedId = EscapeJavaScriptString(cnzzAnalyticsId);
            snippets.Add($$"""
<script>
var _czc = window._czc || [];
window._czc = _czc;
_czc.push(["_setAccount", "{{escapedId}}"]);
(function() {
  var cnzz = document.createElement("script");
  cnzz.type = "text/javascript";
  cnzz.async = true;
  cnzz.charset = "utf-8";
  cnzz.src = "https://w.cnzz.com/c.php?id={{escapedId}}&async=1";
  var root = document.getElementsByTagName("script")[0];
  root.parentNode.insertBefore(cnzz, root);
})();
</script>
""");
        }

        var laConfig = Build51LaConfiguration(siteConfig);
        if (laConfig.Count > 0
            && laConfig.TryGetValue("id", out var laIdValue)
            && laIdValue?.ToString() is { Length: > 0 } laId)
        {
            var laCk = laConfig.TryGetValue("ck", out var laCkValue) && !string.IsNullOrWhiteSpace(laCkValue?.ToString())
                ? laCkValue!.ToString()!
                : laId;

            var options = new List<string>
            {
                $"id: \"{EscapeJavaScriptString(laId)}\"",
                $"ck: \"{EscapeJavaScriptString(laCk)}\""
            };

            if (laConfig.TryGetValue("autoTrack", out var autoTrackValue) && autoTrackValue is bool autoTrack)
            {
                options.Add($"autoTrack: {autoTrack.ToString().ToLowerInvariant()}");
            }

            if (laConfig.TryGetValue("hashMode", out var hashModeValue) && hashModeValue is bool hashMode)
            {
                options.Add($"hashMode: {hashMode.ToString().ToLowerInvariant()}");
            }

            if (laConfig.TryGetValue("screenRecord", out var screenRecordValue) && screenRecordValue is bool screenRecord)
            {
                options.Add($"screenRecord: {screenRecord.ToString().ToLowerInvariant()}");
            }

            snippets.Add($$"""
<script charset="UTF-8" id="LA_COLLECT" src="https://sdk.51.la/js-sdk-pro.min.js"></script>
<script>LA.init({ {{string.Join(", ", options)}} });</script>
""");
        }

        return string.Join(Environment.NewLine, snippets);
    }

    private static Dictionary<string, object?> BuildFooterSiteObject(IReadOnlyDictionary<string, object?> siteConfig)
    {
        var result = ReadConfigDictionary(siteConfig, "footer");

        SetIfPresent(result, "copyright", ReadConfigString(siteConfig, "footer.copyright", "copyright"));
        SetIfPresent(result, "icp", ReadConfigString(siteConfig, "footer.icp", "footer.beian", "icp", "\u5907\u6848\u53f7"));
        SetIfPresent(
            result,
            "public_security_beian",
            ReadConfigString(
                siteConfig,
                "footer.public_security_beian",
                "footer.gongan_beian",
                "footer.police_beian",
                "public_security_beian",
                "gongan_beian",
                "police_beian",
                "\u516c\u5b89\u5907\u6848\u53f7"));
        SetIfPresent(
            result,
            "telecom_license",
            ReadConfigString(
                siteConfig,
                "footer.telecom_license",
                "footer.value_added_telecom_license",
                "telecom_license",
                "value_added_telecom_license",
                "\u589e\u503c\u7535\u4fe1\u4e1a\u52a1\u7ecf\u8425\u8bb8\u53ef\u8bc1"));
        SetIfPresent(result, "terms_url", ReadConfigString(siteConfig, "footer.terms_url", "footer.service_terms_url", "terms_url", "service_terms_url"));
        SetIfPresent(result, "privacy_url", ReadConfigString(siteConfig, "footer.privacy_url", "footer.privacy_policy_url", "privacy_url", "privacy_policy_url"));
        SetIfPresent(result, "report_phone", ReadConfigString(siteConfig, "footer.report_phone", "report_phone", "\u8fdd\u6cd5\u548c\u4e0d\u826f\u4e3e\u62a5\u7535\u8bdd"));
        SetIfPresent(result, "report_email", ReadConfigString(siteConfig, "footer.report_email", "report_email", "\u4e3e\u62a5\u90ae\u7bb1"));

        return result;
    }

    private static Dictionary<string, object?> BuildAnalyticsSiteObject(IReadOnlyDictionary<string, object?> siteConfig)
    {
        var result = ReadConfigDictionary(siteConfig, "analytics");

        if (!result.ContainsKey("google"))
        {
            SetIfPresent(result, "google", ReadScalarConfigString(siteConfig, "analytics.google", "analytics.google_analytics", "google_analytics", "google"));
        }

        if (!result.ContainsKey("baidu"))
        {
            SetIfPresent(result, "baidu", ReadScalarConfigString(siteConfig, "analytics.baidu", "analytics.baidu_tongji", "baidu", "baidu_tongji"));
        }

        if (!result.ContainsKey("cnzz"))
        {
            SetIfPresent(result, "cnzz", ReadScalarConfigString(siteConfig, "analytics.cnzz", "analytics.umeng", "cnzz", "umeng"));
        }

        var la = Build51LaConfiguration(siteConfig);
        if (la.Count > 0)
        {
            result["51la"] = la;
        }

        return result;
    }

    private static Dictionary<string, object?> Build51LaConfiguration(IReadOnlyDictionary<string, object?> siteConfig)
    {
        var analytics = ReadConfigDictionary(siteConfig, "analytics");
        var result = analytics.TryGetValue("51la", out var nested51La) && nested51La is Dictionary<string, object?> nestedDictionary
            ? new Dictionary<string, object?>(nestedDictionary, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var simpleValue = ReadConfigValue(siteConfig, "analytics.51la", "analytics.51_la", "51la", "51_la");
        if (simpleValue is Dictionary<string, object?> configDictionary)
        {
            foreach (var pair in configDictionary)
            {
                result[pair.Key] = pair.Value;
            }
        }
        else if (simpleValue?.ToString() is { Length: > 0 } simpleId)
        {
            result["id"] = simpleId;
            result["ck"] = simpleId;
        }

        SetIfPresent(result, "id", ReadConfigString(siteConfig, "analytics.51la.id", "analytics.51_la.id"));
        SetIfPresent(result, "ck", ReadConfigString(siteConfig, "analytics.51la.ck", "analytics.51_la.ck"));

        SetBooleanIfPresent(result, "autoTrack", ReadConfigBoolean(siteConfig, "analytics.51la.autoTrack", "analytics.51la.auto_track", "analytics.51_la.autoTrack", "analytics.51_la.auto_track"));
        SetBooleanIfPresent(result, "hashMode", ReadConfigBoolean(siteConfig, "analytics.51la.hashMode", "analytics.51la.hash_mode", "analytics.51_la.hashMode", "analytics.51_la.hash_mode"));
        SetBooleanIfPresent(result, "screenRecord", ReadConfigBoolean(siteConfig, "analytics.51la.screenRecord", "analytics.51la.screen_record", "analytics.51_la.screenRecord", "analytics.51_la.screen_record"));

        return result;
    }

    private static string? ReadGoogleAnalyticsTrackingId(IReadOnlyDictionary<string, object?> siteConfig)
        => ReadScalarConfigString(
            siteConfig,
            "analytics.google.tracking_id",
            "analytics.google.measurement_id",
            "analytics.google_analytics",
            "google_analytics",
            "analytics.google",
            "google");

    private static string? ReadScalarConfigString(IReadOnlyDictionary<string, object?> siteConfig, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryResolveObject(siteConfig, key, out var value)
                || value is null
                || value is IEnumerable<KeyValuePair<string, object?>>
                || value is System.Collections.IDictionary
                || value is System.Collections.IEnumerable and not string)
            {
                continue;
            }

            var text = value.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static object? ReadConfigValue(IReadOnlyDictionary<string, object?> siteConfig, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryResolveObject(siteConfig, key, out var value) && value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static string? ReadConfigString(IReadOnlyDictionary<string, object?> siteConfig, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryResolveObject(siteConfig, key, out var value) || value is null)
            {
                continue;
            }

            var text = value.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static bool? ReadConfigBoolean(IReadOnlyDictionary<string, object?> siteConfig, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryResolveObject(siteConfig, key, out var value) || value is null)
            {
                continue;
            }

            var parsed = TryConvertToBoolean(value);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return null;
    }

    private static Dictionary<string, object?> ReadConfigDictionary(IReadOnlyDictionary<string, object?> siteConfig, string key)
    {
        if (TryResolveObject(siteConfig, key, out var value))
        {
            switch (value)
            {
                case Dictionary<string, object?> dictionary:
                    return new Dictionary<string, object?>(dictionary, StringComparer.OrdinalIgnoreCase);
                case IReadOnlyDictionary<string, object?> readOnlyDictionary:
                    return new Dictionary<string, object?>(readOnlyDictionary, StringComparer.OrdinalIgnoreCase);
            }
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

}
