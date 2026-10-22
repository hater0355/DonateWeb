using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DonateWeb.Models.Entities;
using DonateWeb.ViewModels.Streamer;

namespace DonateWeb.Services;

public sealed class StreamerAgreementContentService
{
    public const string CurrentVersion = "DW-STREAMER-2026-10-v1";
    private readonly StreamerAgreementDocument _document;

    public StreamerAgreementContentService(IWebHostEnvironment environment)
    {
        var path = Path.Combine(environment.ContentRootPath, "Agreement", "streamer-agreement-v1.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("Thiếu nội dung hợp đồng streamer.", path);

        _document = JsonSerializer.Deserialize<StreamerAgreementDocument>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Không đọc được nội dung hợp đồng streamer.");

        if (!string.Equals(_document.Version, CurrentVersion, StringComparison.Ordinal))
            throw new InvalidDataException("Phiên bản hợp đồng trong dữ liệu không khớp với phiên bản ứng dụng.");
    }

    public StreamerAgreementDocument Document => _document;

    public string BuildSnapshot(
        PlatformContractOptions options,
        User user,
        StreamerProfile streamer,
        StreamerIdentityData identity,
        string website,
        DateTime signedAtUtc,
        string signerName)
    {
        if (!options.IsComplete)
            throw new InvalidOperationException("Chưa thể tạo hợp đồng khi thông tin pháp lý của bên vận hành còn thiếu hoặc là giá trị minh họa.");

        var localTime = signedAtUtc.AddHours(7);
        var sb = new StringBuilder();
        sb.AppendLine(_document.Title);
        sb.AppendLine($"Mã phiên bản: {_document.Version}");
        sb.AppendLine($"Thời điểm chấp nhận: {localTime.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)} (UTC+7)");
        sb.AppendLine();
        sb.AppendLine("BÊN A - ĐƠN VỊ CUNG CẤP NỀN TẢNG");
        sb.AppendLine($"Tư cách: {options.OperatorCapacity.Trim()}");
        sb.AppendLine($"Họ và tên: {options.OperatorLegalName.Trim()}");
        sb.AppendLine($"Địa chỉ: {options.OperatorAddress.Trim()}");
        sb.AppendLine($"Mã số thuế/mã định danh/đăng ký: {options.OperatorRegistrationNumber.Trim()}");
        sb.AppendLine($"Email: {options.ContactEmail.Trim()}");
        sb.AppendLine($"Website: {website}");
        sb.AppendLine();
        sb.AppendLine("BÊN B - STREAMER");
        sb.AppendLine($"Họ và tên theo hồ sơ định danh: {identity.FullName.Trim()}");
        sb.AppendLine($"CCCD: {MaskIdentityNumber(identity.CitizenId)}");
        sb.AppendLine($"Địa chỉ thường trú: {identity.PermanentAddress.Trim()}");
        sb.AppendLine($"Email tài khoản: {user.Email}");
        sb.AppendLine($"Tên kênh: {streamer.DisplayName}");
        sb.AppendLine($"Mã trang Streamer: {streamer.Slug}");
        sb.AppendLine();

        foreach (var section in _document.Sections)
        {
            sb.AppendLine(section.Title);
            foreach (var paragraph in section.Paragraphs)
            {
                sb.AppendLine(paragraph);
                sb.AppendLine();
            }
        }

        sb.AppendLine("XÁC NHẬN GIAO KẾT ĐIỆN TỬ");
        sb.AppendLine($"Bên B đã đăng nhập tài khoản DonateWeb, xác nhận đã đọc và đồng ý với toàn bộ nội dung phiên bản {_document.Version}, xác nhận thông tin định danh thuộc về mình và nhập lại họ tên: {signerName.Trim()}.");
        sb.AppendLine($"Hệ thống ghi nhận thời điểm {localTime.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)} (UTC+7). Bản ghi này cùng dữ liệu phiên ký là bằng chứng về thao tác chấp nhận điện tử.");
        return sb.ToString();
    }

    public static string MaskIdentityNumber(string? number)
    {
        var digits = new string((number ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? digits : $"••••••••{digits[^4..]}";
    }

    public static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed class StreamerAgreementDocument
{
    public string Version { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<StreamerAgreementSection> Sections { get; set; } = [];
}

public sealed class StreamerAgreementSection
{
    public string Title { get; set; } = string.Empty;
    public List<string> Paragraphs { get; set; } = [];
}

public sealed class PlatformContractOptions
{
    public const string SectionName = "PlatformContract";
    public string OperatorCapacity { get; set; } = string.Empty;
    public string OperatorLegalName { get; set; } = "Nguyễn Anh Hiếu";
    public string OperatorAddress { get; set; } = string.Empty;
    public string OperatorRegistrationNumber { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = "cupitcookies@gmail.com";

    public bool IsComplete =>
        IsConfiguredValue(OperatorCapacity) &&
        IsConfiguredValue(OperatorLegalName) &&
        IsConfiguredValue(OperatorAddress) && OperatorAddress.Trim().Length >= 10 &&
        IsConfiguredValue(OperatorRegistrationNumber) &&
        IsValidContactEmail(ContactEmail);

    private static bool IsValidContactEmail(string? value)
    {
        if (!IsConfiguredValue(value))
            return false;

        var email = value!.Trim();
        if (!MailAddress.TryCreate(email, out var address) ||
            !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
            return false;

        return !address.Host.Contains("example.", StringComparison.OrdinalIgnoreCase) &&
               !address.Host.EndsWith(".test", StringComparison.OrdinalIgnoreCase) &&
               !address.Host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConfiguredValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().ToLowerInvariant();
        string[] placeholderMarkers =
        [
            "minh họa",
            "bài tập lớn",
            "demo",
            "placeholder",
            "change_me",
            "change me",
            "your_",
            "chưa cấu hình",
            "chưa cập nhật",
            "điền thông tin",
            "không áp dụng",
            "n/a",
            "example."
        ];

        return !placeholderMarkers.Any(normalized.Contains);
    }
}
