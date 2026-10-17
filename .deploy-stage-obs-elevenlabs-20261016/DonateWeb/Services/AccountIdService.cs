using Microsoft.EntityFrameworkCore;
using DonateWeb.Data;

namespace DonateWeb.Services
{
    public class AccountIdService : IAccountIdService
    {
        private readonly AppDbContext _context;

        public AccountIdService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<string> GenerateAccountIdAsync(bool isStreamer, int? registrationYear = null)
        {
            var prefix = isStreamer ? "1" : "9";
            var year = (registrationYear.HasValue && registrationYear.Value > 1900 
                ? registrationYear.Value 
                : DateTime.UtcNow.Year).ToString("D4");

            int attempts = 0;
            while (attempts < 50)
            {
                attempts++;
                var randomSuffix = Random.Shared.Next(0, 100000).ToString("D5");
                var candidateId = $"{prefix}{year}{randomSuffix}";

                bool exists = await _context.Users.AnyAsync(u => u.AccountId == candidateId);
                if (!exists)
                {
                    return candidateId;
                }
            }

            // Fallback nếu có va chạm liên tiếp (cực kỳ hiếm gặp)
            var fallbackSuffix = (DateTime.UtcNow.Ticks % 100000).ToString("D5");
            return $"{prefix}{year}{fallbackSuffix}";
        }

        public async Task<string> UpgradeToStreamerIdAsync(string? currentAccountId, int? registrationYear = null)
        {
            int year = registrationYear ?? DateTime.UtcNow.Year;

            if (!string.IsNullOrWhiteSpace(currentAccountId) && currentAccountId.Length == 10 && currentAccountId.StartsWith("9"))
            {
                // Thay chữ số đầu tiên từ 9 thành 1
                var upgradedId = "1" + currentAccountId.Substring(1);

                // Kiểm tra nếu ID sau khi đổi số đầu chưa bị trùng thì sử dụng ngay
                bool exists = await _context.Users.AnyAsync(u => u.AccountId == upgradedId);
                if (!exists)
                {
                    return upgradedId;
                }

                // Nếu tình cờ bị trùng ID đó, trích xuất năm cũ từ ID và sinh 5 số ngẫu nhiên mới với đầu 1
                if (int.TryParse(currentAccountId.Substring(1, 4), out var parsedYear))
                {
                    year = parsedYear;
                }
            }

            // Nếu tài khoản cũ chưa có AccountId hoặc bị trùng, sinh ID Streamer mới bắt đầu bằng 1
            return await GenerateAccountIdAsync(isStreamer: true, year);
        }

        public async Task<string> DowngradeToViewerIdAsync(string? currentAccountId, int? registrationYear = null)
        {
            int year = registrationYear ?? DateTime.UtcNow.Year;

            if (!string.IsNullOrWhiteSpace(currentAccountId) && currentAccountId.Length == 10 && currentAccountId.StartsWith("1"))
            {
                // Thay chữ số đầu tiên từ 1 thành 9
                var downgradedId = "9" + currentAccountId.Substring(1);

                // Kiểm tra nếu ID sau khi đổi số đầu chưa bị trùng thì sử dụng ngay
                bool exists = await _context.Users.AnyAsync(u => u.AccountId == downgradedId);
                if (!exists)
                {
                    return downgradedId;
                }

                // Nếu tình cờ bị trùng ID đó, trích xuất năm cũ từ ID và sinh 5 số ngẫu nhiên mới với đầu 9
                if (int.TryParse(currentAccountId.Substring(1, 4), out var parsedYear))
                {
                    year = parsedYear;
                }
            }

            // Nếu tài khoản cũ chưa có AccountId hoặc bị trùng, sinh ID Viewer mới bắt đầu bằng 9
            return await GenerateAccountIdAsync(isStreamer: false, year);
        }
    }
}
