namespace DonateWeb.Services;

/// <summary>Tạo mã VietQR tĩnh để người xem chuyển tiền trực tiếp tới streamer.</summary>
public interface IStreamerBankQrService
{
    Task<string?> CreateStaticQrImageDataUriAsync(
        string? bankName,
        string? accountNumber,
        string? accountName,
        string transferContent,
        CancellationToken cancellationToken = default);
}
