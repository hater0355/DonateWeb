using System;
using QRCoder;

namespace DonateWeb.Services
{
    /// <summary>
    /// Service xử lý tạo mã QR chất lượng cao bằng thư viện QRCoder
    /// </summary>
    public class QrCodeService : IQrCodeService
    {
        /// <summary>
        /// Sinh chuỗi Data URI Base64 dạng PNG từ URL hoặc văn bản
        /// </summary>
        /// <param name="data">URL hoặc nội dung cần mã hóa</param>
        /// <param name="pixelsPerModule">Kích thước pixel mỗi ô QR (mặc định 10 cho độ nét cao)</param>
        /// <returns>Chuỗi Data URI Base64 dạng 'data:image/png;base64,...'</returns>
        public string GenerateQrCodeBase64(string data, int pixelsPerModule = 10)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                return string.Empty;
            }

            try
            {
                // Sử dụng Error Correction Level Q (25% khả năng sửa lỗi)
                // giúp mã QR dễ quét từ xa qua màn hình OBS/Livestream ngay cả khi mờ hoặc bị che một phần
                using var qrGenerator = new QRCodeGenerator();
                using var qrCodeData = qrGenerator.CreateQrCode(data, QRCodeGenerator.ECCLevel.Q);
                using var qrCode = new PngByteQRCode(qrCodeData);
                
                // pixelsPerModule = 10 tạo ảnh kích thước khoảng ~300x300 đến ~400x400px cực kỳ sắc nét
                byte[] qrCodeBytes = qrCode.GetGraphic(pixelsPerModule);

                return $"data:image/png;base64,{Convert.ToBase64String(qrCodeBytes)}";
            }
            catch (Exception)
            {
                // Trả về rỗng nếu có lỗi xử lý chuỗi
                return string.Empty;
            }
        }
    }
}
