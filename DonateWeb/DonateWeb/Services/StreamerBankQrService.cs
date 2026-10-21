using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;

namespace DonateWeb.Services;

/// <summary>
/// Sinh QR chuyển khoản không khóa số tiền. Người donate có thể nhập số tiền và
/// nối lời nhắn sau mã nhận diện đã được điền sẵn trong app ngân hàng.
/// </summary>
public sealed class StreamerBankQrService : IStreamerBankQrService
{
    private const string BanksCacheKey = "vietqr-bank-list-v2";
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<StreamerBankQrService> _logger;

    public StreamerBankQrService(
        HttpClient httpClient,
        IMemoryCache memoryCache,
        ILogger<StreamerBankQrService> logger)
    {
        _httpClient = httpClient;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public async Task<string?> CreateStaticQrImageDataUriAsync(
        string? bankName,
        string? accountNumber,
        string? accountName,
        string transferContent,
        CancellationToken cancellationToken = default)
    {
        var cleanAccount = (accountNumber ?? string.Empty).Trim();
        var cleanName = NormalizeAccountName(accountName);
        var cleanContent = NormalizeTransferContent(transferContent);

        if (string.IsNullOrWhiteSpace(bankName) ||
            !Regex.IsMatch(cleanAccount, "^[A-Za-z0-9]{6,19}$") ||
            cleanName.Length < 5 || cleanName.Length > 50 ||
            string.IsNullOrWhiteSpace(cleanContent))
        {
            return null;
        }

        var bankId = await ResolveBankIdAsync(bankName, cancellationToken);
        if (string.IsNullOrWhiteSpace(bankId))
        {
            _logger.LogWarning("Không tìm được mã VietQR cho ngân hàng {BankName}.", bankName);
            return null;
        }

        var cacheKey = $"vietqr-image:{bankId}:{cleanAccount}:{cleanName}:{cleanContent}";
        if (_memoryCache.TryGetValue(cacheKey, out string? cachedImage))
        {
            return cachedImage;
        }

        // Không truyền amount để QR cho phép người xem tự điền số tiền.
        var imageUrl = "https://img.vietqr.io/image/" +
            $"{Uri.EscapeDataString(bankId)}-{Uri.EscapeDataString(cleanAccount)}-compact2.png" +
            $"?addInfo={Uri.EscapeDataString(cleanContent)}&accountName={Uri.EscapeDataString(cleanName)}";

        try
        {
            using var response = await _httpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode ||
                (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > 2_000_000))
            {
                _logger.LogWarning("Dịch vụ VietQR không trả được ảnh QR hợp lệ (HTTP {StatusCode}).", response.StatusCode);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var dataUri = CreateImageDataUri(bytes);
            if (dataUri == null)
            {
                _logger.LogWarning(
                    "Dịch vụ VietQR trả về định dạng ảnh không hỗ trợ {ContentType}.",
                    response.Content.Headers.ContentType?.MediaType ?? "không rõ");
                return null;
            }

            // Giữ nguyên SVG vì lớp ngoài chứa các nét QR; ảnh PNG bên trong chỉ là nền/khung.
            _memoryCache.Set(cacheKey, dataUri, TimeSpan.FromHours(12));
            return dataUri;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Không thể tải ảnh VietQR cho tài khoản ngân hàng đã cấu hình.");
            return null;
        }
    }

    private async Task<string?> ResolveBankIdAsync(string bankName, CancellationToken cancellationToken)
    {
        var normalizedInput = NormalizeBankText(bankName);
        if (string.IsNullOrWhiteSpace(normalizedInput))
            return null;

        if (Regex.IsMatch(normalizedInput, "^\\d{6}$"))
            return normalizedInput;

        List<VietQrBank> banks;
        try
        {
            banks = await _memoryCache.GetOrCreateAsync(BanksCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
                using var response = await _httpClient.GetAsync("https://api.vietqr.io/v2/banks", cancellationToken);
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadFromJsonAsync<VietQrBankList>(cancellationToken: cancellationToken);
                return payload?.Data ?? [];
            }) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Không thể lấy danh sách ngân hàng VietQR; thử dùng mã ngân hàng phổ biến.");
            banks = [];
        }

        var exactMatch = banks.FirstOrDefault(bank =>
            new[] { bank.Bin, bank.Code, bank.ShortName, bank.Name }
                .Any(value => NormalizeBankText(value) == normalizedInput));
        if (exactMatch != null)
            return exactMatch.Bin;

        // Cho phép các giá trị đang lưu dạng "MB Bank (Ngân hàng Quân đội)".
        var partialMatch = banks
            .SelectMany(bank => new[]
            {
                (Bank: bank, Value: NormalizeBankText(bank.ShortName)),
                (Bank: bank, Value: NormalizeBankText(bank.Code))
            })
            .Where(candidate => candidate.Value.Length >= 3 &&
                (normalizedInput.StartsWith(candidate.Value, StringComparison.Ordinal) ||
                 normalizedInput.Contains(candidate.Value, StringComparison.Ordinal)))
            .OrderByDescending(candidate => candidate.Value.Length)
            .Select(candidate => candidate.Bank)
            .FirstOrDefault();
        if (partialMatch != null)
            return partialMatch.Bin;

        // Dự phòng cho các mã ngân hàng thông dụng nếu API danh sách tạm thời lỗi.
        return normalizedInput switch
        {
            "MB" or "MBBANK" or "MILITARYBANK" => "970422",
            "VCB" or "VIETCOMBANK" => "970436",
            "BIDV" => "970418",
            "ICB" or "VIETINBANK" or "CTG" => "970415",
            "VBA" or "AGRIBANK" => "970405",
            "TCB" or "TECHCOMBANK" => "970407",
            "ACB" => "970416",
            "VPB" or "VPBANK" => "970432",
            "TPB" or "TPBANK" => "970423",
            "VIB" => "970441",
            "STB" or "SACOMBANK" => "970403",
            "HDB" or "HDBANK" => "970437",
            "OCB" => "970448",
            "MSB" => "970426",
            "SHB" => "970443",
            _ => null
        };
    }

    private static string NormalizeBankText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) !=
                System.Globalization.UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string NormalizeAccountName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = false;
        foreach (var character in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) ==
                System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
                previousWasSpace = false;
            }
            else if (char.IsWhiteSpace(character) && !previousWasSpace && builder.Length > 0)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static string NormalizeTransferContent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var safe = new string(value.Trim().Where(character =>
            char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) || character == '-').ToArray());
        return Regex.Replace(safe, "\\s+", " ").Trim();
    }

    private static bool IsPng(byte[] bytes) =>
        bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
        bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;

    private static string? CreateImageDataUri(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > 2_000_000)
            return null;

        if (IsPng(bytes))
            return "data:image/png;base64," + Convert.ToBase64String(bytes);

        if (IsJpeg(bytes))
            return "data:image/jpeg;base64," + Convert.ToBase64String(bytes);

        return IsSafeVietQrSvg(bytes)
            ? "data:image/svg+xml;base64," + Convert.ToBase64String(bytes)
            : null;
    }

    private static bool IsSafeVietQrSvg(byte[] bytes)
    {
        try
        {
            using var input = new MemoryStream(bytes);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 2_000_000
            });

            var document = XDocument.Load(reader);
            var root = document.Root;
            if (!string.Equals(root?.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
                return false;

            var elements = root!.DescendantsAndSelf().ToArray();
            if (elements.Any(element =>
                    string.Equals(element.Name.LocalName, "script", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(element.Name.LocalName, "foreignObject", StringComparison.OrdinalIgnoreCase)))
                return false;

            var attributes = elements.SelectMany(element => element.Attributes()).ToArray();
            if (attributes.Any(attribute => attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)))
                return false;

            // QR SVG chỉ được nhận tài nguyên nhúng dạng data URI hoặc tham chiếu nội bộ.
            return attributes
                .Where(attribute => string.Equals(attribute.Name.LocalName, "href", StringComparison.OrdinalIgnoreCase))
                .All(attribute => attribute.Value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) ||
                                  attribute.Value.StartsWith('#'));
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsJpeg(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

    private sealed class VietQrBankList
    {
        [JsonPropertyName("data")]
        public List<VietQrBank>? Data { get; set; }
    }

    private sealed class VietQrBank
    {
        [JsonPropertyName("bin")]
        public string Bin { get; set; } = string.Empty;

        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;

        [JsonPropertyName("shortName")]
        public string ShortName { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
