using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using TeachWell.Domain;

namespace TeachWell.Infrastructure.Canvas;

public static partial class CanvasText
{
    private const int EvidenceItemLimit = 200, EvidenceTextLimit = 200;

    public static string FromHtml(string? html) => Normalize(html, null).Text;

    // Produces inert plain text plus structural evidence from the same sanitized document. Original markup is never persisted;
    // the visual preview is a separate, allowlisted re-serialization (Preview below).
    public static (string Text, ContentEvidence Evidence) Normalize(string? html, Uri? origin, AssignmentEvidence? assignment = null)
    {
        if (string.IsNullOrEmpty(html)) return ("", new(ContentEvidence.CurrentVersion, [], [], [], [], assignment));
        if (html.Length > 60000) throw new CanvasReadException(TeachWell.Application.CanvasError.LimitExceeded);
        // HtmlParser has no browsing context, resource loader or JavaScript engine.
        using var document = new HtmlParser(new HtmlParserOptions { IsScripting = false }).ParseDocument(html);
        foreach (var node in document.QuerySelectorAll("script,style,iframe,object,embed,template,noscript,form,input,button,link,meta,svg,math")) node.Remove();
        var result = new StringBuilder();
        if (document.Body is null) return ("", new(ContentEvidence.CurrentVersion, [], [], [], [], assignment));
        Walk(document.Body, result);
        return (result.ToString().Trim(), Evidence(document.Body, origin, assignment));
    }

    private const int PreviewNodeLimit = 20000, PreviewLengthLimit = 200000, PreviewDepthLimit = 48;
    private static readonly Dictionary<string, string> PreviewTags = new()
    {
        ["P"] = "p", ["H1"] = "h1", ["H2"] = "h2", ["H3"] = "h3", ["H4"] = "h4", ["H5"] = "h5", ["H6"] = "h6", ["UL"] = "ul", ["OL"] = "ol", ["LI"] = "li",
        ["STRONG"] = "strong", ["B"] = "strong", ["EM"] = "em", ["I"] = "em", ["U"] = "u", ["S"] = "s", ["SUB"] = "sub", ["SUP"] = "sup",
        ["BLOCKQUOTE"] = "blockquote", ["PRE"] = "pre", ["CODE"] = "code", ["DIV"] = "div", ["SECTION"] = "div", ["ARTICLE"] = "div",
        ["TABLE"] = "table", ["CAPTION"] = "caption", ["THEAD"] = "thead", ["TBODY"] = "tbody", ["TFOOT"] = "tfoot", ["TR"] = "tr", ["TH"] = "th", ["TD"] = "td",
        ["DL"] = "dl", ["DT"] = "dt", ["DD"] = "dd", ["MARK"] = "mark", ["CENTER"] = "div", ["ASIDE"] = "div", ["KBD"] = "code", ["SAMP"] = "code", ["TT"] = "code"
    };

    // Builds new, inert markup from the sanitized document instead of passing original HTML through: only the tags above are
    // emitted, every text node is encoded, no source attributes survive (bar numeric table spans and header scope), links become
    // labeled text without their URL, and images become placeholders that show — and flag — their text alternative.
    // Canvas themes, inline styles, colors, media and scripts are deliberately not reproduced.
    public static string? Preview(string? html, Uri? origin = null)
    {
        if (string.IsNullOrEmpty(html)) return null;
        if (html.Length > 60000) throw new CanvasReadException(TeachWell.Application.CanvasError.LimitExceeded);
        using var document = new HtmlParser(new HtmlParserOptions { IsScripting = false }).ParseDocument(html);
        foreach (var node in document.QuerySelectorAll("script,style,iframe,object,embed,template,noscript,form,input,button,link,meta,svg,math")) node.Remove();
        if (document.Body is null) return null;
        var output = new StringBuilder(); var count = 0;
        foreach (var child in document.Body.ChildNodes) Render(child, output, origin, 0, ref count);
        if (output.Length > PreviewLengthLimit) return output.ToString(0, PreviewLengthLimit) + "<p class=\"pv-note\">Preview truncated at the capture limit.</p>";
        return output.Length == 0 ? null : output.ToString();
    }

    private static void Render(INode node, StringBuilder output, Uri? origin, int depth, ref int count)
    {
        if (++count > PreviewNodeLimit) throw new CanvasReadException(TeachWell.Application.CanvasError.LimitExceeded);
        if (output.Length > PreviewLengthLimit) return;
        if (node is IText text) { output.Append(Encode(text.Data)); return; }
        if (node is not IElement el) return;
        if (depth > PreviewDepthLimit) { output.Append(Encode(Collapse(el.TextContent))); return; }
        switch (el.TagName)
        {
            case "BR": output.Append("<br>"); return;
            case "HR": output.Append("<hr>"); return;
            case "IMG": output.Append(ImagePlaceholder(el)); return;
            case "A":
                var label = el.GetAttribute("aria-label") is { Length: > 0 } aria ? aria : Collapse(el.TextContent);
                if (label.Length == 0) label = string.Join(" ", el.QuerySelectorAll("img[alt]").Select(i => i.GetAttribute("alt")!.Trim()).Where(a => a.Length > 0));
                var (kind, host) = el.GetAttribute("href") is { } href ? Classify(href, origin) : ("None", null);
                var where = host is null ? kind.ToLowerInvariant() : "external: " + host;
                output.Append(label.Length == 0
                    ? $"<span class=\"pv-link pv-flag\" title=\"Link ({Encode(where)}) with no accessible text\">[link with no text]</span>"
                    : $"<span class=\"pv-link\" title=\"Link ({Encode(where)})\">{Encode(Clip(label)!)}</span>");
                return;
        }
        var classes = FormatClasses(el);
        if (!PreviewTags.TryGetValue(el.TagName, out var tag) && classes.Count > 0 && el.TagName is "SPAN" or "FONT")
            tag = "span"; // styled inline text keeps its formatting; unstyled spans are unwrapped below
        if (tag is null)
        {
            foreach (var child in el.ChildNodes) Render(child, output, origin, depth + 1, ref count);
            return;
        }
        var empty = tag[0] == 'h' && tag.Length == 2 && Collapse(el.TextContent).Length == 0 && el.QuerySelector("img") is null;
        output.Append('<').Append(tag);
        if (empty) classes.Add("pv-flag");
        if (classes.Count > 0) output.Append(" class=\"").Append(string.Join(' ', classes)).Append('"');
        if (tag is "th" or "td")
        {
            foreach (var span in new[] { "colspan", "rowspan" })
                if (int.TryParse(el.GetAttribute(span), out var n) && n is > 1 and <= 50) output.Append(' ').Append(span).Append("=\"").Append(n).Append('"');
            if (tag == "th" && el.GetAttribute("scope")?.ToLowerInvariant() is "row" or "col" or "rowgroup" or "colgroup") output.Append(" scope=\"").Append(el.GetAttribute("scope")!.ToLowerInvariant()).Append('"');
        }
        output.Append('>');
        if (empty) output.Append("[empty heading]");
        else foreach (var child in el.ChildNodes) Render(child, output, origin, depth + 1, ref count);
        output.Append("</").Append(tag).Append('>');
    }

    // Canvas formatting kept as a fixed set of classes rather than inline styles: the Content-Security-Policy forbids inline styles, and a
    // closed vocabulary means nothing from the source can inject CSS. Colours snap to the nearest of a small accessible palette.
    private static readonly string[] Palette = ["red", "orange", "yellow", "green", "teal", "blue", "purple", "pink", "gray"];
    private static List<string> FormatClasses(IElement el)
    {
        var classes = new List<string>();
        var declarations = (el.GetAttribute("style") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Split(':', 2)).Where(p => p.Length == 2)
            .GroupBy(p => p[0].Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Last()[1].Trim().ToLowerInvariant());
        var align = declarations.GetValueOrDefault("text-align") ?? el.GetAttribute("align")?.Trim().ToLowerInvariant() ?? (el.TagName == "CENTER" ? "center" : null);
        if (align is "center" or "right" or "justify") classes.Add("pv-align-" + align);
        if (declarations.GetValueOrDefault("font-weight") is "bold" or "bolder" or "600" or "700" or "800" or "900") classes.Add("pv-bold");
        if (declarations.GetValueOrDefault("font-style") == "italic") classes.Add("pv-italic");
        var decoration = declarations.GetValueOrDefault("text-decoration") ?? declarations.GetValueOrDefault("text-decoration-line") ?? "";
        if (decoration.Contains("underline")) classes.Add("pv-underline");
        if (decoration.Contains("line-through")) classes.Add("pv-strike");
        if (ColorName(declarations.GetValueOrDefault("color") ?? (el.TagName == "FONT" ? el.GetAttribute("color") : null), background: false) is { } fg) classes.Add("pv-fg-" + fg);
        if (ColorName(declarations.GetValueOrDefault("background-color") ?? declarations.GetValueOrDefault("background"), background: true) is { } bg) classes.Add("pv-bg-" + bg);
        if (el.TagName == "MARK" && !classes.Any(c => c.StartsWith("pv-bg-"))) classes.Add("pv-bg-yellow");

        // Text set in a monospace font reads as code, whatever element carries it.
        if (declarations.GetValueOrDefault("font-family") is { } font && MonospaceFont().IsMatch(font)) classes.Add("pv-mono");
        var scale = FontScale(declarations.GetValueOrDefault("font-size"));
        if (scale >= 1.2) classes.Add("pv-large");
        else if (scale is > 0 and <= 0.875) classes.Add("pv-small");

        // Callout boxes: a tinted background, a border and/or a coloured left accent bar on a block element.
        if (el.TagName is "DIV" or "P" or "SECTION" or "ARTICLE" or "ASIDE" or "BLOCKQUOTE" or "CENTER")
        {
            var boxed = Border(declarations, "border") is { Width: > 0 };
            var accent = Border(declarations, "border-left");
            if (accent is { Width: >= 3 } a) classes.Add("pv-accent-" + (ColorName(a.Color, background: false) ?? "gray"));
            if (boxed) classes.Add("pv-boxed");
            if (boxed || accent is { Width: >= 3 } || classes.Any(c => c.StartsWith("pv-bg-"))) classes.Add("pv-box");
        }
        // A rule under a heading or paragraph, in its colour when it has one.
        if (el.TagName is "H1" or "H2" or "H3" or "H4" or "H5" or "H6" or "P" or "DIV" && Border(declarations, "border-bottom") is { Width: > 0 } rule)
        {
            classes.Add("pv-rule");
            if (ColorName(rule.Color, background: false) is { } ruleColor && ruleColor != "gray") classes.Add("pv-rule-" + ruleColor);
        }
        return classes;
    }

    // Width (in px, roughly) and colour of a border shorthand such as "4px solid #999" or "1px solid rgb(0, 0, 0)".
    private static (double Width, string? Color)? Border(Dictionary<string, string> declarations, string property)
    {
        if (!declarations.TryGetValue(property, out var value) || value is "none" or "0" or "0px") return null;
        var color = RgbFunction().Match(value) is { Success: true } rgb ? rgb.Value : value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(t => t.StartsWith('#') || NamedColors.ContainsKey(t));
        var width = PixelWidth().Match(value) is { Success: true } w ? double.Parse(w.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            : value.Contains("thick") ? 5 : value.Contains("medium") ? 3 : value.Contains("thin") ? 1 : 1;
        return (width, color ?? (declarations.GetValueOrDefault(property + "-color")));
    }

    // Font size relative to normal body text (16px), or 0 when unknown.
    private static double FontScale(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var keyword = value switch { "small" or "smaller" => 0.85, "x-small" or "xx-small" => 0.7, "large" or "larger" => 1.2, "x-large" => 1.5, "xx-large" => 2, _ => 0.0 };
        if (keyword > 0) return keyword;
        if (SizeValue().Match(value) is not { Success: true } m || !double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n)) return 0;
        return m.Groups[2].Value switch { "px" => n / 16, "pt" => n * 4 / 3 / 16, "em" or "rem" => n, "%" => n / 100, _ => 0 };
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"monospace|courier|consolas|menlo|monaco|cascadia|source code|fira (?:code|mono)|jetbrains|lucida console|ubuntu mono|sf mono", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex MonospaceFont();
    [System.Text.RegularExpressions.GeneratedRegex(@"rgba?\([^)]*\)")] private static partial System.Text.RegularExpressions.Regex RgbFunction();
    [System.Text.RegularExpressions.GeneratedRegex(@"(\d+(?:\.\d+)?)px")] private static partial System.Text.RegularExpressions.Regex PixelWidth();
    [System.Text.RegularExpressions.GeneratedRegex(@"^(\d+(?:\.\d+)?)(px|pt|em|rem|%)$")] private static partial System.Text.RegularExpressions.Regex SizeValue();

    // Maps a CSS colour to a palette name, or null when it is the page default (near-black text, near-white background) or unreadable.
    private static string? ColorName(string? value, bool background)
    {
        if (string.IsNullOrWhiteSpace(value) || ParseColor(FirstColorToken(value)) is not { } color) return null;
        var (r, g, b) = color;
        double max = Math.Max(r, Math.Max(g, b)) / 255.0, min = Math.Min(r, Math.Min(g, b)) / 255.0, light = (max + min) / 2, delta = max - min;
        var saturation = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * light - 1));
        if (background ? light > 0.985 : light < 0.22) return null;
        if (saturation < 0.15) return background || light < 0.8 ? "gray" : null;
        double hue = delta == 0 ? 0 : max == r / 255.0 ? 60 * (((g - b) / 255.0 / delta) % 6) : max == g / 255.0 ? 60 * ((b - r) / 255.0 / delta + 2) : 60 * ((r - g) / 255.0 / delta + 4);
        if (hue < 0) hue += 360;
        return hue switch { < 15 or >= 345 => "red", < 40 => "orange", < 70 => "yellow", < 160 => "green", < 195 => "teal", < 255 => "blue", < 290 => "purple", _ => "pink" };
    }

    // The colour part of a value such as "rgb(241, 196, 15)" or "#fff url(x) no-repeat": a functional notation is kept whole.
    private static string FirstColorToken(string value)
    {
        var v = value.Trim();
        if (v.StartsWith("rgb", StringComparison.Ordinal) && v.IndexOf(')') is var close and > 0) return v[..(close + 1)];
        return v.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
    }

    private static readonly Dictionary<string, (int, int, int)> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = (255, 0, 0), ["orange"] = (255, 165, 0), ["yellow"] = (255, 255, 0), ["green"] = (0, 128, 0), ["lime"] = (0, 255, 0), ["teal"] = (0, 128, 128),
        ["aqua"] = (0, 255, 255), ["cyan"] = (0, 255, 255), ["blue"] = (0, 0, 255), ["navy"] = (0, 0, 128), ["purple"] = (128, 0, 128), ["fuchsia"] = (255, 0, 255),
        ["magenta"] = (255, 0, 255), ["pink"] = (255, 192, 203), ["maroon"] = (128, 0, 0), ["olive"] = (128, 128, 0), ["brown"] = (165, 42, 42), ["gray"] = (128, 128, 128),
        ["grey"] = (128, 128, 128), ["silver"] = (192, 192, 192), ["black"] = (0, 0, 0), ["white"] = (255, 255, 255)
    };
    private static (int R, int G, int B)? ParseColor(string value)
    {
        if (value is "transparent" or "none" or "inherit" or "initial" or "currentcolor") return null;
        if (NamedColors.TryGetValue(value, out var named)) return named;
        if (value.StartsWith('#'))
        {
            var hex = value[1..];
            if (hex.Length == 3) hex = string.Concat(hex.Select(c => $"{c}{c}"));
            return hex.Length >= 6 && int.TryParse(hex[..6], System.Globalization.NumberStyles.HexNumber, null, out var n) ? ((n >> 16) & 255, (n >> 8) & 255, n & 255) : null;
        }
        if (value.StartsWith("rgb", StringComparison.Ordinal) && value.IndexOf('(') is var open and > 0 && value.IndexOf(')') is var close && close > open)
        {
            var raw = value[(open + 1)..close].Split(',', ' ', '/').Where(p => p.Length > 0).ToArray();
            // A fully transparent colour (alpha 0) is no colour at all.
            if (raw.Length >= 4 && double.TryParse(raw[3].TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var alpha) && alpha == 0) return null;
            var parts = raw.Take(3).Select(p => int.TryParse(p, out var x) ? Math.Clamp(x, 0, 255) : -1).ToArray();
            return parts.Length == 3 && parts.All(p => p >= 0) ? (parts[0], parts[1], parts[2]) : null;
        }
        return null;
    }

    private static string ImagePlaceholder(IElement img)
    {
        var alt = img.GetAttribute("alt");
        var decorative = img.GetAttribute("role") is "presentation" or "none" || img.GetAttribute("aria-hidden") == "true";
        var (css, text) = alt?.Trim() switch
        {
            _ when decorative => ("pv-img", "Decorative image"),
            null => ("pv-img pv-flag", "Image — no alt text"),
            "" => ("pv-img pv-flag", "Image marked decorative (empty alt)"),
            var a when FileName().IsMatch(a) => ("pv-img pv-flag", "Image — alt text is a file name: " + Clip(a)),
            var a => ("pv-img", "Image: " + Clip(a))
        };
        return $"<span class=\"{css}\">{Encode(text)}</span>";
    }
    private static string Encode(string text) => System.Net.WebUtility.HtmlEncode(text);
    [System.Text.RegularExpressions.GeneratedRegex(@"^[^\s]+\.(png|jpe?g|gif|svg|webp|bmp|tiff?)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex FileName();

    private static ContentEvidence Evidence(IElement body, Uri? origin, AssignmentEvidence? assignment)
    {
        var truncated = false;
        List<T> Take<T>(IEnumerable<IElement> nodes, Func<IElement, int, T> map)
        {
            var list = nodes.ToList();
            if (list.Count > EvidenceItemLimit) truncated = true;
            return list.Take(EvidenceItemLimit).Select(map).ToList();
        }
        var images = Take(body.QuerySelectorAll("img"), (img, i) => new ImageEvidence(i, Clip(img.GetAttribute("alt")),
            img.GetAttribute("role") is "presentation" or "none" || img.GetAttribute("aria-hidden") == "true"));
        var headings = Take(body.QuerySelectorAll("h1,h2,h3,h4,h5,h6"), (h, i) => new HeadingEvidence(i, h.TagName[1] - '0', Clip(Collapse(h.TextContent))!));
        var links = Take(body.QuerySelectorAll("a[href]"), (a, i) =>
        {
            var name = a.GetAttribute("aria-label") is { Length: > 0 } label ? label : Collapse(a.TextContent);
            if (name.Length == 0) name = string.Join(" ", a.QuerySelectorAll("img[alt]").Select(img => img.GetAttribute("alt")!.Trim()).Where(alt => alt.Length > 0));
            var (kind, host) = Classify(a.GetAttribute("href")!, origin);
            return new LinkEvidence(i, Clip(name)!, kind, host);
        });
        var tables = Take(body.QuerySelectorAll("table"), (t, i) => new TableEvidence(i, t.QuerySelector("th") is not null, t.QuerySelectorAll("tr").Length));
        return new(ContentEvidence.CurrentVersion, images, headings, links, tables, assignment, truncated);
    }

    // Keeps only a destination kind and external host; full URLs can carry query-string secrets.
    private static (string Kind, string? Host) Classify(string href, Uri? origin)
    {
        href = href.Trim();
        if (href.StartsWith('#')) return ("Anchor", null);
        if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return ("Email", null);
        // Root-relative paths parse as file URIs on Unix, so classify them before Uri parsing.
        if (href.StartsWith("//", StringComparison.Ordinal)) href = "https:" + href;
        else if (href.StartsWith('/')) return ("Internal", null);
        if (!Uri.TryCreate(href, UriKind.RelativeOrAbsolute, out var uri)) return ("Invalid", null);
        if (!uri.IsAbsoluteUri) return href.Length == 0 ? ("Invalid", null) : ("Internal", null);
        if (uri.Scheme is not ("https" or "http")) return ("Invalid", null);
        if (origin is not null && string.Equals(uri.Host, origin.Host, StringComparison.OrdinalIgnoreCase)) return ("Internal", null);
        return ("External", uri.Host.Length <= 120 ? uri.Host.ToLowerInvariant() : null);
    }
    private static string Collapse(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string? Clip(string? text) => text is null ? null : text.Length <= EvidenceTextLimit ? text : text[..EvidenceTextLimit];

    private static void Walk(INode node, StringBuilder output)
    {
        var stack = new Stack<(INode Node, bool Closing)>(); stack.Push((node, false)); var count = 0;
        while (stack.TryPop(out var entry))
        {
            if (++count > 20000) throw new CanvasReadException(TeachWell.Application.CanvasError.LimitExceeded);
            if (entry.Node is IText text) { output.Append(text.Data); continue; }
            var block = entry.Node is IElement el && el.TagName is "P" or "DIV" or "SECTION" or "ARTICLE" or "LI" or "H1" or "H2" or "H3" or "H4" or "H5" or "H6" or "BR" or "TR";
            if (block && output.Length > 0 && output[^1] != '\n') output.AppendLine();
            if (entry.Closing) continue;
            if (block) stack.Push((entry.Node, true));
            foreach (var child in entry.Node.ChildNodes.Reverse()) stack.Push((child, false));
        }
    }
}
