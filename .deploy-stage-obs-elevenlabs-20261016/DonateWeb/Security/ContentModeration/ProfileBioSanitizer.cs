using Ganss.Xss;

namespace DonateWeb.Security.ContentModeration;

/// <summary>
/// Làm sạch HTML trong phần giới thiệu công khai của streamer.
/// Chỉ giữ lại các thẻ định dạng văn bản cơ bản; script, iframe, ảnh và thuộc tính sự kiện bị loại bỏ.
/// </summary>
public static class ProfileBioSanitizer
{
    public static string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        // Tạo sanitizer riêng cho mỗi lần gọi để tránh chia sẻ trạng thái cấu hình giữa các request.
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(new[]
        {
            "p", "br", "strong", "b", "em", "i", "u", "s", "ul", "ol", "li",
            "blockquote", "h1", "h2", "h3", "h4", "span", "a"
        });

        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.UnionWith(new[] { "href", "title" });

        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(new[] { "http", "https", "mailto" });

        return sanitizer.Sanitize(html);
    }
}
