namespace DonateWeb.ViewModels.Streamer
{
    /// <summary>
    /// Thông tin trích từ CCCD. Nội dung này chỉ được lưu dưới dạng đã mã hóa.
    /// </summary>
    public sealed class StreamerIdentityData
    {
        public string FullName { get; set; } = string.Empty;
        public string DateOfBirth { get; set; } = string.Empty;
        public string CitizenId { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string Nationality { get; set; } = string.Empty;
        public string PlaceOfOrigin { get; set; } = string.Empty;
        public string PlaceOfBirth { get; set; } = string.Empty;
        public string PermanentAddress { get; set; } = string.Empty;
        public string IdentityIssuedDate { get; set; } = string.Empty;
        public string IdentityIssuedBy { get; set; } = string.Empty;
        public string? CurrentAddress { get; set; }
    }

    /// <summary>Dữ liệu đã mã hóa và khóa ảnh riêng tư để lưu vào hồ sơ Streamer.</summary>
    public sealed record StreamerIdentitySubmission(
        string ProtectedData,
        string FrontDocumentKey,
        string BackDocumentKey,
        DateTime ConsentAtUtc);
}
