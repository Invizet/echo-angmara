using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EchoAngmara.News;

public sealed class NewsItem
{
    public int Id { get; set; }
    public string Url { get; set; } = "";
    public DateTimeOffset? Date { get; set; }
    public string Html { get; set; } = "";
    public List<string> Photos { get; set; } = new();
}

/// <summary>Новости из ветки site/news (их туда кладёт GitHub Actions из Telegram). Кэш на диске — на случай офлайна.</summary>
public static class NewsService
{
    static string CacheFile => Path.Combine(Localization.L10nState.Dir, "news.json");

    public sealed record Feed(List<NewsItem> Items, string BaseUrl, bool FromCache);

    public static async Task<Feed?> LoadAsync(HttpClient http, CancellationToken ct)
    {
        foreach (var site in AppLinks.SiteMirrors)
        {
            try
            {
                string json = await http.GetStringAsync(site + "news/news.json", ct);
                var items = Parse(json);
                Directory.CreateDirectory(Localization.L10nState.Dir);
                await File.WriteAllTextAsync(CacheFile, JsonSerializer.Serialize(new { baseUrl = site + "news/", json }), ct);
                return new Feed(items, site + "news/", false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }
        try
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(CacheFile, ct));
            return new Feed(Parse(doc.RootElement.GetProperty("json").GetString()!), doc.RootElement.GetProperty("baseUrl").GetString()!, true);
        }
        catch { return null; }
    }

    static List<NewsItem> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("items").Deserialize<List<NewsItem>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    }

    // ---------- HTML-подмножество Telegram → FlowDocument ----------

    static readonly Regex Token = new(@"<(/?)([a-z]+)([^>]*)>|([^<]+)", RegexOptions.Compiled);
    static readonly Regex Href = new(@"href=""([^""]*)""", RegexOptions.Compiled);

    /// <summary>Рисует один пост: дата, картинки, текст с b/i/u/s/a/blockquote/code.</summary>
    public static Section Render(NewsItem item, string baseUrl, Brush accent, Brush muted)
    {
        var sec = new Section { Margin = new Thickness(0, 0, 0, 18) };
        var head = new Paragraph { Margin = new Thickness(0, 0, 0, 4), FontSize = 11, Foreground = muted };
        head.Inlines.Add(new Run(item.Date?.ToLocalTime().ToString("d MMMM yyyy, HH:mm", new System.Globalization.CultureInfo("ru-RU")) ?? ""));
        head.Inlines.Add(new Run("  ·  "));
        var open = new Hyperlink(new Run("открыть в Telegram")) { NavigateUri = new Uri(item.Url), Foreground = muted };
        open.Click += (_, _) => Shell.Open(item.Url);
        head.Inlines.Add(open);
        sec.Blocks.Add(head);

        foreach (var p in item.Photos.Take(1)) // одна картинка-обложка, остальные — по ссылке на пост
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = new Uri(baseUrl + p);
                img.DecodePixelWidth = 640;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                sec.Blocks.Add(new BlockUIContainer(new System.Windows.Controls.Image
                {
                    Source = img, MaxHeight = 220, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 2, 0, 6),
                }));
            }
            catch { }
        }

        var para = new Paragraph { Margin = new Thickness(0) };
        sec.Blocks.Add(para);
        var stack = new Stack<Span>();
        Span Current() => stack.Count > 0 ? stack.Peek() : null!;
        void Add(Inline i) { if (stack.Count > 0) Current().Inlines.Add(i); else para.Inlines.Add(i); }

        foreach (Match m in Token.Matches(item.Html))
        {
            // U+FE0F (вариант эмодзи) WPF рисует квадратом — выбрасываем
            if (m.Groups[4].Success) { Add(new Run(WebUtility.HtmlDecode(m.Groups[4].Value).Replace("\uFE0F", ""))); continue; }
            bool close = m.Groups[1].Value == "/";
            string tag = m.Groups[2].Value;
            if (tag == "br") { Add(new LineBreak()); continue; }
            if (tag == "blockquote")
            {
                // цитату — отдельным абзацем с полосой слева
                stack.Clear();
                para = close
                    ? new Paragraph { Margin = new Thickness(0) }
                    : new Paragraph { Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(10, 2, 0, 2), BorderBrush = accent, BorderThickness = new Thickness(3, 0, 0, 0) };
                sec.Blocks.Add(para);
                continue;
            }
            if (close) { if (stack.Count > 0) stack.Pop(); continue; }
            Span s = tag switch
            {
                "b" or "strong" => new Bold(),
                "i" or "em" => new Italic(),
                "u" => new Underline(),
                "s" or "del" => new Span { TextDecorations = TextDecorations.Strikethrough },
                "code" or "pre" => new Span { FontFamily = new FontFamily("Consolas") },
                "a" => MakeLink(Href.Match(m.Groups[3].Value).Groups[1].Value, accent),
                _ => new Span(),
            };
            Add(s);
            stack.Push(s);
        }
        return sec;
    }

    static Span MakeLink(string href, Brush accent)
    {
        href = WebUtility.HtmlDecode(href);
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri)) return new Span();
        var h = new Hyperlink { NavigateUri = uri, Foreground = accent };
        h.Click += (_, _) => Shell.Open(uri.ToString());
        return h;
    }
}

public static class Shell
{
    public static void Open(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
