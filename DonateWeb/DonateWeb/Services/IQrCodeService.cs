namespace DonateWeb.Services
{
    /// <summary>
    /// Service tạo mã QR Code (hỗ trợ sinh ảnh Base64 PNG chuẩn nhúng web hoặc livestream OBS)
    /// </summary>
    public interface IQrCodeService
    {
        /// <summary>
        /// Tạo ảnh QR code dưới dạng chuỗi Data URI Base64 (data:image/png;base64,...)
        /// </summary>
        /// <param name="data">Dữ liệu hoặc đường dẫn URL cần mã hóa</param>
        /// <param name="pixelsPerModule">Độ phân giải pixel cho mỗi ô QR (mặc định 10 để ảnh sắc nét trên livestream)</param>
        /// <returns>Chuỗi Data URI Base64 PNG hoàn chỉnh</returns>
        string GenerateQrCodeBase64(string data, int pixelsPerModule = 10);
    }
}
