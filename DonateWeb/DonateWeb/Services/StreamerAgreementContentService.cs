using System.Globalization;
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
        var localTime = signedAtUtc.AddHours(7);
        var sb = new StringBuilder();
        sb.AppendLine(_document.Title);
        sb.AppendLine($"Mã phiên bản: {_document.Version}");
        sb.AppendLine($"Thời điểm chấp nhận: {localTime.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)} (UTC+7)");
        sb.AppendLine();
        sb.AppendLine("BÊN A - ĐƠN VỊ CUNG CẤP NỀN TẢNG");
        sb.AppendLine("Lưu ý: Đây là bản demo phục vụ bài tập lớn; địa chỉ và mã định danh Bên A là dữ liệu minh họa.");
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
        !string.IsNullOrWhiteSpace(OperatorCapacity) &&
        !string.IsNullOrWhiteSpace(OperatorLegalName) &&
        !string.IsNullOrWhiteSpace(OperatorAddress) &&
        !string.IsNullOrWhiteSpace(OperatorRegistrationNumber) &&
        !string.IsNullOrWhiteSpace(ContactEmail);
}
