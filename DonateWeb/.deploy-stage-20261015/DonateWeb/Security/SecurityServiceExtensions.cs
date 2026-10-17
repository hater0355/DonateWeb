using DonateWeb.Security.Configuration;
using DonateWeb.Security.ContentModeration;
using DonateWeb.Security.RateLimiting;
using DonateWeb.Security.Webhook;

namespace DonateWeb.Security
{
    /// <summary>
    /// PHƯƠNG THỨC MỞ RỘNG (EXTENSION METHODS) ĐĂNG KÝ PHÂN HỆ BẢO MẬT & KIỂM DUYỆT
    /// Lưu trữ trong thư mục riêng: Security/
    /// Giúp Program.cs đăng ký toàn bộ DI Service chỉ bằng 1 dòng code duy nhất:
    /// builder.Services.AddSecurityAndModeration(builder.Configuration);
    /// </summary>
    public static class SecurityServiceExtensions
    {
        public static IServiceCollection AddSecurityAndModeration(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // 1. Đăng ký Cấu hình SecuritySettings từ appsettings.json
            services.Configure<SecuritySettings>(configuration.GetSection(SecuritySettings.SectionName));

            // 2. Đăng ký Service Giới hạn tần suất request (Rate Limiting) - Singleton để giữ trạng thái bộ đếm trong RAM
            services.AddSingleton<IIpRateLimiterService, IpRateLimiterService>();

            // 3. Đăng ký Service Kiểm duyệt nội dung lời nhắn & chống spam link - Singleton vì logic thuần xử lý chuỗi và regex
            services.AddSingleton<IContentModerationService, ContentModerationService>();

            // 4. Đăng ký Service Bảo mật & Xác thực Webhook (IP Whitelist + Secret Key/HMAC) - Scoped theo vòng đời request
            services.AddScoped<IWebhookSecurityService, WebhookSecurityService>();

            return services;
        }
    }
}
