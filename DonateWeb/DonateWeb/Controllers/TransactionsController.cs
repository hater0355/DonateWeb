using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Data;
using DonateWeb.Models.Enums;
using DonateWeb.ViewModels;

namespace DonateWeb.Controllers
{
    [Authorize]
    public class TransactionsController : Controller
    {
        private readonly AppDbContext _context;

        public TransactionsController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string? type)
        {
            var list = new List<TransactionViewModel>();

            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return View(list);
            }

            await _context.Database.ExecuteSqlRawAsync(@"
                IF OBJECT_ID('dbo.WalletTransactions', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.WalletTransactions (
                        Id INT IDENTITY(1,1) NOT NULL,
                        UserId INT NOT NULL,
                        TransactionCode NVARCHAR(100) NOT NULL,
                        TransactionType NVARCHAR(30) NOT NULL,
                        Amount DECIMAL(18,2) NOT NULL,
                        BalanceBefore DECIMAL(18,2) NOT NULL DEFAULT 0,
                        BalanceAfter DECIMAL(18,2) NOT NULL DEFAULT 0,
                        PaymentMethodName NVARCHAR(100) NULL,
                        OrderCode BIGINT NULL,
                        [Status] INT NOT NULL DEFAULT 1,
                        Note NVARCHAR(500) NULL,
                        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                        CONSTRAINT PK_WalletTransactions PRIMARY KEY CLUSTERED (Id),
                        CONSTRAINT FK_WalletTransactions_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
                    );
                END

                IF OBJECT_ID('dbo.WalletTransactions', 'U') IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.WalletTransactions') AND name = 'OrderCode')
                BEGIN
                    ALTER TABLE dbo.WalletTransactions ADD OrderCode BIGINT NULL;
                END
            ");

            var user = await _context.Users
                .Include(u => u.StreamerProfile)
                .FirstOrDefaultAsync(u => u.Id == userId);

            ViewBag.CurrentBalance = user?.WalletBalance ?? 0m;
            ViewBag.SelectedType = type ?? "ALL";

            // 1. Lấy lịch sử Nạp tiền / Rút tiền từ bảng WalletTransactions
            var walletTxs = await _context.WalletTransactions
                .Where(wt => wt.UserId == userId)
                .OrderByDescending(wt => wt.CreatedAt)
                .Take(50)
                .ToListAsync();

            foreach (var wt in walletTxs)
            {
                var isDeposit = wt.TransactionType == "DEPOSIT";
                list.Add(new TransactionViewModel
                {
                    TransactionId = wt.TransactionCode,
                    Time = wt.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"),
                    Type = isDeposit
                        ? $"Nạp tiền vào ví ({wt.PaymentMethodName})"
                        : $"Rút tiền về {wt.PaymentMethodName}",
                    TotalAmount = (isDeposit ? "+" : "-") + wt.Amount.ToString("N0") + " VNĐ",
                    Status = wt.Status == 1 ? "Thành công" : (wt.Status == 0 ? "Đang chờ duyệt" : "Đã từ chối")
                });
            }

            // 2. Lấy lịch sử Donate (Ủng hộ gửi đi hoặc nhận được)
            var donations = await _context.Donations
                .Include(d => d.StreamerProfile)
                .Where(d => d.DonorUserId == userId || (user != null && user.StreamerProfile != null && d.StreamerProfileId == user.StreamerProfile.Id))
                .OrderByDescending(d => d.CreatedAt)
                .Take(50)
                .ToListAsync();

            foreach (var d in donations)
            {
                bool isReceived = user?.StreamerProfile != null && d.StreamerProfileId == user.StreamerProfile.Id && d.DonorUserId != userId;
                list.Add(new TransactionViewModel
                {
                    TransactionId = d.TransactionCode ?? $"DON_{d.Id}",
                    Time = d.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"),
                    Type = isReceived
                        ? $"Nhận donate từ {d.DonorName}"
                        : $"Donate cho kênh {d.StreamerProfile?.DisplayName ?? "Streamer"}",
                    TotalAmount = (isReceived ? "+" : "-") + d.Amount.ToString("N0") + " VNĐ",
                    Status = d.Status == DonationStatus.Success ? "Thành công" : d.Status.ToString()
                });
            }

            return View(list);
        }
    }
}
