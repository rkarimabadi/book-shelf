using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BookStore.Core.Domain.Translation;
using ErrorOr;

namespace BookStore.Infrastructure.Translation.Epub;

/// <summary>A text document of the book (spine chapter, EPUB 3 nav or EPUB 2 NCX).</summary>
internal sealed record EpubDocument(int Index, string Href, EpubDocKind Kind)
{
    public bool IsToc => Kind != EpubDocKind.Xhtml;
}

/// <summary>
/// An EPUB held in memory: every zip entry plus the parsed OPF (documents in reading order,
/// stylesheets, metadata). Text entries are decoded once and written back as UTF-8.
/// </summary>
internal sealed class EpubBook
{
    private const string RtlCssName = "rtl-fa.css";

    // Deliberately minimal: direction, Persian fonts and line height. Alignment, margins and
    // image layout stay as the book defines them (left alignment is mirrored to right instead).
    private const string RtlCss = """
        /* Added by the «نون گرد» translation tool: right-to-left Persian text. */
        html, body { direction: rtl; unicode-bidi: embed; }
        body, p, li, dt, dd, blockquote, td, th, caption, figcaption, h1, h2, h3, h4, h5, h6 {
          font-family: "Vazirmatn", "Vazir", "Sahel", "IRANSans", "Tahoma", sans-serif;
        }
        p { line-height: 1.9; }
        h1, h2, h3, h4, h5, h6 { letter-spacing: normal; }

        """;

    private static readonly string[] TextExtensions = [".xhtml", ".html", ".htm", ".opf", ".ncx", ".xml", ".css", ".svg"];

    private readonly List<string> _order = [];
    private readonly Dictionary<string, byte[]> _binary = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _text = new(StringComparer.Ordinal);

    public string OpfPath { get; private set; } = string.Empty;
    public string OpfDir => PosixDirName(OpfPath);
    public List<EpubDocument> Documents { get; } = [];
    public List<string> Stylesheets { get; } = [];
    public string Title { get; private set; } = string.Empty;
    public string Author { get; private set; } = string.Empty;

    private EpubBook() { }

    public static ErrorOr<EpubBook> Load(Stream stream)
    {
        var book = new EpubBook();
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name.EndsWith('/') || name.StartsWith('/') || name.Split('/').Contains(".."))
                {
                    continue;
                }

                using var input = entry.Open();
                using var buffer = new MemoryStream();
                input.CopyTo(buffer);
                if (!book._binary.ContainsKey(name))
                {
                    book._order.Add(name);
                }

                book._binary[name] = buffer.ToArray();
            }
        }
        catch (InvalidDataException)
        {
            return TranslationJobErrors.InvalidEpub;
        }

        // DRM: encrypted text cannot be translated (obfuscated fonts alone are fine).
        if (book._binary.ContainsKey("META-INF/encryption.xml"))
        {
            var encryption = book.GetText("META-INF/encryption.xml");
            if (Regex.Matches(encryption, "CipherReference[^>]*URI=\"([^\"]+)\"")
                .Any(m => Regex.IsMatch(m.Groups[1].Value, @"\.(x?html?|ncx|opf)$", RegexOptions.IgnoreCase)))
            {
                return TranslationJobErrors.DrmProtected;
            }
        }

        try
        {
            book.ParsePackage();
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException
                                       or KeyNotFoundException or NullReferenceException)
        {
            return TranslationJobErrors.InvalidEpub;
        }

        return book;
    }

    private void ParsePackage()
    {
        var container = XDocument.Parse(GetText("META-INF/container.xml"));
        OpfPath = container.Descendants().First(e => e.Name.LocalName == "rootfile").Attribute("full-path")!.Value;
        var opf = XDocument.Parse(GetText(OpfPath));

        var items = new Dictionary<string, (string Href, string Type, string Props)>(StringComparer.Ordinal);
        foreach (var item in opf.Descendants().Where(e => e.Name.LocalName == "item"))
        {
            var id = (string?)item.Attribute("id") ?? string.Empty;
            items[id] = (
                ResolveHref(OpfDir, Uri.UnescapeDataString((string?)item.Attribute("href") ?? string.Empty)),
                (string?)item.Attribute("media-type") ?? string.Empty,
                (string?)item.Attribute("properties") ?? string.Empty);
        }

        var spine = opf.Descendants().First(e => e.Name.LocalName == "spine");
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string href, EpubDocKind kind)
        {
            if (seen.Add(href) && _binary.ContainsKey(href))
            {
                Documents.Add(new EpubDocument(Documents.Count, href, kind));
            }
        }

        foreach (var idref in spine.Elements().Where(e => e.Name.LocalName == "itemref").Select(e => (string?)e.Attribute("idref")))
        {
            if (idref is not null && items.TryGetValue(idref, out var item) && item.Type.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                Add(item.Href, HasProperty(item.Props, "nav") ? EpubDocKind.Nav : EpubDocKind.Xhtml);
            }
        }

        foreach (var item in items.Values.Where(i => HasProperty(i.Props, "nav")))
        {
            Add(item.Href, EpubDocKind.Nav);
        }

        var tocId = (string?)spine.Attribute("toc");
        foreach (var (key, item) in items)
        {
            if (item.Type == "application/x-dtbncx+xml" || key == tocId)
            {
                Add(item.Href, EpubDocKind.Ncx);
            }
        }

        Stylesheets.AddRange(items.Values.Where(i => i.Type == "text/css").Select(i => i.Href));

        Title = Clean(opf.Descendants().FirstOrDefault(e => e.Name.LocalName == "title")?.Value);
        Author = Clean(opf.Descendants().FirstOrDefault(e => e.Name.LocalName == "creator")?.Value);

        static string Clean(string? value) =>
            string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool HasProperty(string props, string name) =>
        props.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name);

    // ------------------------------------------------------------------ entries

    public bool Contains(string name) => _binary.ContainsKey(name) || _text.ContainsKey(name);

    public string GetText(string name)
    {
        if (_text.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var text = Decode(_binary[name], out var reencoded);
        if (reencoded)
        {
            text = Regex.Replace(text, "(<\\?xml[^>]*encoding=[\"'])[\\w.-]+([\"'])", "${1}utf-8$2");
            text = Regex.Replace(text, "(charset=)[\\w.-]+", "${1}utf-8");
            _text[name] = text;
        }

        return text;
    }

    public void SetText(string name, string text)
    {
        if (!Contains(name))
        {
            _order.Add(name);
        }

        _text[name] = text;
    }

    public void SetBinary(string name, byte[] data)
    {
        if (!Contains(name))
        {
            _order.Add(name);
        }

        _text.Remove(name);
        _binary[name] = data;
    }

    public void Remove(string name)
    {
        _order.Remove(name);
        _binary.Remove(name);
        _text.Remove(name);
    }

    private static string Decode(byte[] data, out bool reencoded)
    {
        reencoded = false;
        var offset = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data, offset, data.Length - offset);
        }
        catch (DecoderFallbackException)
        {
        }

        reencoded = true;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var head = Encoding.ASCII.GetString(data, 0, Math.Min(600, data.Length));
        var declared = Regex.Match(head, "encoding=[\"']([\\w.-]+)[\"']");
        if (!declared.Success)
        {
            declared = Regex.Match(head, "charset=([\\w.-]+)");
        }

        foreach (var name in new[] { declared.Success ? declared.Groups[1].Value : null, "windows-1256" })
        {
            if (name is null)
            {
                continue;
            }

            try
            {
                return Encoding.GetEncoding(name).GetString(data);
            }
            catch (ArgumentException)
            {
            }
        }

        return Encoding.Latin1.GetString(data);
    }

    // ------------------------------------------------------------------ metadata, RTL, cover

    /// <summary>Sets dc:title / dc:creator (the first of each) when a value is given.</summary>
    public void SetMetadata(string? title, string? author)
    {
        var opf = GetText(OpfPath);
        if (!string.IsNullOrWhiteSpace(title))
        {
            opf = Regex.Replace(opf, @"(<dc:title\b[^>]*>)(.*?)(</dc:title\s*>)",
                m => m.Groups[1].Value + EpubText.EscapeText(title.Trim()) + m.Groups[3].Value,
                RegexOptions.Singleline, TimeSpan.FromSeconds(5));
        }

        if (!string.IsNullOrWhiteSpace(author))
        {
            var replaced = false;
            opf = Regex.Replace(opf, @"(<dc:creator\b[^>]*>)(.*?)(</dc:creator\s*>)", m =>
            {
                if (replaced)
                {
                    return m.Value;
                }

                replaced = true;
                return m.Groups[1].Value + EpubText.EscapeText(author.Trim()) + m.Groups[3].Value;
            }, RegexOptions.Singleline);
        }

        SetText(OpfPath, opf);
    }

    /// <summary>Marks the book as Persian and right-to-left (port of epub_tool fix-rtl).</summary>
    public void FixRtl()
    {
        var opf = GetText(OpfPath);
        opf = Regex.IsMatch(opf, @"<dc:language\b")
            ? Regex.Replace(opf, @"(<dc:language\b[^>]*>)[^<]*(</dc:language>)", "${1}fa$2")
            : Regex.Replace(opf, @"(<metadata\b[^>]*>)", "$1\n    <dc:language>fa</dc:language>", RegexOptions.None, TimeSpan.FromSeconds(5));

        var spine = Regex.Match(opf, @"<spine\b[^>]*>");
        if (spine.Success)
        {
            opf = opf[..spine.Index] + EpubText.SetAttr(spine.Value, "page-progression-direction", "rtl") + opf[(spine.Index + spine.Length)..];
        }

        // One small stylesheet, linked last so it wins the cascade.
        var cssHref = OpfDir.Length > 0 ? $"{OpfDir}/{RtlCssName}" : RtlCssName;
        SetText(cssHref, RtlCss);
        if (!opf.Contains(RtlCssName, StringComparison.Ordinal))
        {
            opf = ReplaceFirst(opf, "</manifest>",
                $"  <item id=\"rtl-fa-css\" href=\"{RtlCssName}\" media-type=\"text/css\"/>\n  </manifest>");
        }

        SetText(OpfPath, opf);

        // Existing stylesheets: left alignment and letter-spacing break Persian.
        foreach (var href in Stylesheets.Where(h => Contains(h) && !h.EndsWith(RtlCssName, StringComparison.Ordinal)))
        {
            var css = GetText(href);
            var patched = Regex.Replace(css, @"text-align\s*:\s*left", "text-align: right", RegexOptions.IgnoreCase);
            patched = Regex.Replace(patched, @"letter-spacing\s*:\s*[^;}]+", "letter-spacing: normal", RegexOptions.IgnoreCase);
            patched = Regex.Replace(patched, @"direction\s*:\s*ltr", "direction: rtl", RegexOptions.IgnoreCase);
            if (patched != css)
            {
                SetText(href, patched);
            }
        }

        foreach (var doc in Documents)
        {
            var content = GetText(doc.Href);
            var updated = Regex.Replace(content, "(?<=\\s)((?:xml:)?lang)\\s*=\\s*\"en[^\"]*\"", "$1=\"fa\"");
            updated = Regex.Replace(updated, "(?<=\\s)dir\\s*=\\s*\"ltr\"", "dir=\"rtl\"");

            if (doc.Kind != EpubDocKind.Ncx)
            {
                var html = Regex.Match(updated, @"<html\b[^>]*>");
                if (html.Success)
                {
                    var tag = html.Value;
                    foreach (var (name, value) in new[] { ("dir", "rtl"), ("lang", "fa"), ("xml:lang", "fa") })
                    {
                        tag = EpubText.SetAttr(tag, name, value);
                    }

                    updated = updated[..html.Index] + tag + updated[(html.Index + html.Length)..];
                }

                if (!updated.Contains(RtlCssName, StringComparison.Ordinal) && updated.Contains("</head>", StringComparison.Ordinal))
                {
                    var rel = RelativePath(cssHref, PosixDirName(doc.Href));
                    updated = ReplaceFirst(updated, "</head>", $"  <link rel=\"stylesheet\" type=\"text/css\" href=\"{rel}\"/>\n</head>");
                }
            }

            if (updated != content)
            {
                SetText(doc.Href, updated);
            }
        }
    }

    /// <summary>
    /// Replaces the book's cover image (EPUB 3 <c>cover-image</c> item or EPUB 2 <c>meta name="cover"</c>);
    /// a book without one gets the image registered as its cover.
    /// </summary>
    public void ReplaceCover(byte[] image, string extension)
    {
        extension = extension.ToLowerInvariant();
        var mediaType = extension switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };

        var opf = GetText(OpfPath);
        var isEpub3 = Regex.IsMatch(opf, "<package\\b[^>]*\\bversion\\s*=\\s*[\"']3", RegexOptions.IgnoreCase);
        var items = Regex.Matches(opf, @"<item\b[^>]*>", RegexOptions.IgnoreCase).ToList();

        string? Attr(string tag, string name)
        {
            var m = Regex.Match(tag, "(?<=\\s)" + Regex.Escape(name) + "\\s*=\\s*(\"([^\"]*)\"|'([^']*)')");
            return m.Success ? (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value) : null;
        }

        var coverItem = items.FirstOrDefault(m => (Attr(m.Value, "properties") ?? string.Empty).Split(' ').Contains("cover-image"));
        if (coverItem is null)
        {
            var metaCover = Regex.Match(opf, "<meta\\b[^>]*name\\s*=\\s*[\"']cover[\"'][^>]*>", RegexOptions.IgnoreCase);
            var coverId = metaCover.Success ? Attr(metaCover.Value, "content") : null;
            coverItem = items.FirstOrDefault(m => coverId is not null && Attr(m.Value, "id") == coverId);
        }

        if (coverItem is not null && (Attr(coverItem.Value, "media-type") ?? string.Empty).StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            var oldHref = Uri.UnescapeDataString(Attr(coverItem.Value, "href") ?? string.Empty);
            var oldPath = ResolveHref(OpfDir, oldHref);
            var oldName = oldHref.Split('/')[^1];
            var newName = "cover-fa" + extension;
            var hrefDir = oldHref.Contains('/') ? oldHref[..(oldHref.LastIndexOf('/') + 1)] : string.Empty;
            var newPath = ResolveHref(OpfDir, hrefDir + newName);

            var tag = EpubText.SetAttr(coverItem.Value, "href", hrefDir + newName);
            tag = EpubText.SetAttr(tag, "media-type", mediaType);
            opf = opf[..coverItem.Index] + tag + opf[(coverItem.Index + coverItem.Length)..];
            SetText(OpfPath, opf);

            // Every page that shows the old cover (cover.xhtml, SVG wrappers, NCX) now points at the new one.
            var reference = new Regex("(?<=[/\"'(=])" + Regex.Escape(oldName) + "(?=[\"')#?\\s])");
            foreach (var name in _order.ToList().Where(IsTextEntry))
            {
                var text = GetText(name);
                var replaced = reference.Replace(text, newName);
                if (replaced != text)
                {
                    SetText(name, replaced);
                }
            }

            if (oldPath != newPath)
            {
                Remove(oldPath);
            }

            SetBinary(newPath, image);
            return;
        }

        // No cover yet: register the image as the cover.
        var href = "cover-fa" + extension;
        var properties = isEpub3 ? " properties=\"cover-image\"" : string.Empty;
        opf = ReplaceFirst(opf, "</manifest>", $"  <item id=\"cover-fa\" href=\"{href}\" media-type=\"{mediaType}\"{properties}/>\n  </manifest>");
        if (!Regex.IsMatch(opf, "<meta\\b[^>]*name\\s*=\\s*[\"']cover[\"']", RegexOptions.IgnoreCase))
        {
            opf = ReplaceFirst(opf, "</metadata>", "  <meta name=\"cover\" content=\"cover-fa\"/>\n  </metadata>");
        }

        SetText(OpfPath, opf);
        SetBinary(ResolveHref(OpfDir, href), image);
    }

    private static bool IsTextEntry(string name) =>
        TextExtensions.Contains(Path.GetExtension(name).ToLowerInvariant());

    // ------------------------------------------------------------------ saving

    /// <summary>Writes the EPUB: <c>mimetype</c> first and stored, everything else deflated.</summary>
    public void Save(Stream output)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        var mimetype = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var writer = mimetype.Open())
        {
            writer.Write("application/epub+zip"u8);
        }

        foreach (var name in _order.Where(n => n != "mimetype"))
        {
            var fileName = name.Split('/')[^1];
            if (fileName is ".DS_Store" or "Thumbs.db" or "desktop.ini")
            {
                continue;
            }

            var data = _text.TryGetValue(name, out var text) ? new UTF8Encoding(false).GetBytes(text) : _binary[name];
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(data);
        }
    }

    // ------------------------------------------------------------------ path helpers

    private static string ReplaceFirst(string text, string search, string replacement)
    {
        var index = text.IndexOf(search, StringComparison.Ordinal);
        return index < 0 ? text : text[..index] + replacement + text[(index + search.Length)..];
    }

    private static string PosixDirName(string path)
    {
        var index = path.LastIndexOf('/');
        return index < 0 ? string.Empty : path[..index];
    }

    /// <summary>posixpath.normpath(join(dir, href)).</summary>
    private static string ResolveHref(string dir, string href)
    {
        href = href.Split('#')[0];
        var parts = new List<string>();
        foreach (var segment in (dir.Length > 0 ? dir + "/" + href : href).Split('/'))
        {
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == ".." && parts.Count > 0)
            {
                parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(segment);
        }

        return string.Join('/', parts);
    }

    private static string RelativePath(string target, string fromDir)
    {
        var targetParts = target.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var fromParts = fromDir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var common = 0;
        while (common < fromParts.Length && common < targetParts.Length - 1 && fromParts[common] == targetParts[common])
        {
            common++;
        }

        return string.Join('/', Enumerable.Repeat("..", fromParts.Length - common).Concat(targetParts.Skip(common)));
    }
}
