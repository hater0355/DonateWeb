using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DonateWeb.Data;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.ViewModels.Orders;

namespace DonateWeb.Controllers
{
    [Authorize]
    public class OrdersController : Controller
    {
        private readonly AppDbContext _context;

        public OrdersController(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// TRUNG TÂM QUẢN LÝ ĐƠN HÀNG, LỊCH SỬ DONATE & HIỆU ỨNG
        /// - Viewer: Xem đã donate cho ai, lời nhắn đã gửi, đơn hàng shop đã mua, hiệu ứng đã dùng.
        /// - Streamer: Xem ai đã donate cho mình, LỜI NHẮN LÀ GÌ, số tiền ủng hộ, đơn bán hàng shop.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index(
            string? tab,
            string? keyword,
            string? type,
            string? status,
            DateTime? dateFrom,
            DateTime? dateTo)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId))
            {
                return RedirectToAction("Login", "Auth", new { returnUrl = "/Orders" });
            }

            var currentUser = await _context.Users
                .Include(u => u.StreamerProfile)
                .FirstOrDefaultAsync(u => u.Id == currentUserId);

            if (currentUser == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            bool isStreamer = currentUser.StreamerProfile != null && currentUser.StreamerProfile.IsActive;
            var streamerProfile = currentUser.StreamerProfile;

            // Xác định tab mặc định: Streamer mặc định xem fan donate ("received"), Viewer mặc định xem đã donate ("sent")
            string defaultTab = isStreamer ? "received" : "sent";
            string currentTab = string.IsNullOrWhiteSpace(tab) ? defaultTab : tab.Trim().ToLower();
            if (currentTab == "effects")
            {
                currentTab = defaultTab;
            }

            var viewModel = new OrdersHistoryViewModel
            {
                IsStreamer = isStreamer,
                CurrentUserId = currentUserId,
                CurrentUserName = currentUser.FullName ?? currentUser.Username,
                CurrentUserAvatar = string.IsNullOrWhiteSpace(currentUser.AvatarUrl) ? "/images/default-avatar.png" : currentUser.AvatarUrl,
                StreamerDisplayName = streamerProfile?.DisplayName,
                StreamerSlug = streamerProfile != null ? (!string.IsNullOrWhiteSpace(currentUser.AccountId) ? currentUser.AccountId : streamerProfile.Slug) : null,
                ActiveTab = currentTab,
                SearchKeyword = keyword?.Trim(),
                SelectedType = string.IsNullOrWhiteSpace(type) ? "ALL" : type.Trim().ToUpper(),
                SelectedStatus = string.IsNullOrWhiteSpace(status) ? "ALL" : status.Trim().ToUpper(),
                DateFrom = dateFrom,
                DateTo = dateTo
            };

            var cleanKeyword = keyword?.Trim().ToLower();

            // ==========================================
            // 1. TRUY VẤN LỊCH SỬ DONATE GỬI ĐI (VIEWER ĐÃ DONATE CHO AI)
            // ==========================================
            var sentQuery = _context.Donations
                .Include(d => d.StreamerProfile)
                    .ThenInclude(sp => sp.User)
                .Where(d => d.DonorUserId == currentUserId)
                .AsNoTracking();

            // Áp dụng bộ lọc thời gian
            if (dateFrom.HasValue)
            {
                var fromUtc = DateTime.SpecifyKind(dateFrom.Value.Date, DateTimeKind.Utc);
                sentQuery = sentQuery.Where(d => d.CreatedAt >= fromUtc);
            }
            if (dateTo.HasValue)
            {
                var toUtc = DateTime.SpecifyKind(dateTo.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
                sentQuery = sentQuery.Where(d => d.CreatedAt <= toUtc);
            }

            // Áp dụng bộ lọc trạng thái
            if (!string.IsNullOrWhiteSpace(status) && status.ToUpper() != "ALL")
            {
                if (status.ToUpper() == "SUCCESS")
                    sentQuery = sentQuery.Where(d => d.Status == DonationStatus.Success);
                else if (status.ToUpper() == "PENDING")
                    sentQuery = sentQuery.Where(d => d.Status == DonationStatus.Pending);
                else if (status.ToUpper() == "FAILED")
                    sentQuery = sentQuery.Where(d => d.Status == DonationStatus.Failed || d.Status == DonationStatus.Cancelled);
            }

            // Áp dụng từ khóa tìm kiếm
            if (!string.IsNullOrWhiteSpace(cleanKeyword))
            {
                sentQuery = sentQuery.Where(d =>
                    (d.TransactionCode != null && d.TransactionCode.ToLower().Contains(cleanKeyword)) ||
                    d.StreamerProfile.DisplayName.ToLower().Contains(cleanKeyword) ||
                    (d.Message != null && d.Message.ToLower().Contains(cleanKeyword)));
            }

            var rawSentList = await sentQuery.OrderByDescending(d => d.CreatedAt).ToListAsync();

            viewModel.SentDonations = rawSentList.Select(d => new DonationSentItemViewModel
            {
                Id = d.Id,
                TransactionCode = d.TransactionCode ?? $"DON-{d.Id:D6}",
                StreamerDisplayName = d.StreamerProfile.DisplayName,
                StreamerSlug = !string.IsNullOrWhiteSpace(d.StreamerProfile.User?.AccountId) ? d.StreamerProfile.User.AccountId : d.StreamerProfile.Slug,
                StreamerAvatar = string.IsNullOrWhiteSpace(d.StreamerProfile.AvatarUrl) ? "/images/default-avatar.png" : d.StreamerProfile.AvatarUrl,
                Amount = d.Amount,
                Message = d.Message,
                CreatedAt = d.CreatedAt.ToLocalTime(),
                PaymentMethodName = d.PaymentMethod == PaymentMethod.Wallet ? "Ví số dư" : "VietQR Napas247",
                Status = d.Status,
                StatusBadgeClass = d.Status == DonationStatus.Success ? "bg-success" : (d.Status == DonationStatus.Pending ? "bg-warning text-dark" : "bg-danger"),
                StatusText = d.Status == DonationStatus.Success ? "Thành công" : (d.Status == DonationStatus.Pending ? "Đang xử lý" : "Thất bại"),
                EffectTag = d.SpecialEffectTitle
            }).ToList();

            // Tính thống kê Donate gửi đi
            viewModel.TotalDonationSentCount = viewModel.SentDonations.Count;
            viewModel.TotalDonationSentAmount = viewModel.SentDonations
                .Where(x => x.Status == DonationStatus.Success)
                .Sum(x => x.Amount);

            // ==========================================
            // 2. TRUY VẤN LỊCH SỬ DONATE NHẬN ĐƯỢC (STREAMER: AI ĐÃ DONATE CHO MÌNH & LỜI NHẮN LÀ GÌ)
            // ==========================================
            if (isStreamer && streamerProfile != null)
            {
                var receivedQuery = _context.Donations
                    .Include(d => d.DonorUser)
                    .Where(d => d.StreamerProfileId == streamerProfile.Id)
                    .AsNoTracking();

                if (dateFrom.HasValue)
                {
                    var fromUtc = DateTime.SpecifyKind(dateFrom.Value.Date, DateTimeKind.Utc);
                    receivedQuery = receivedQuery.Where(d => d.CreatedAt >= fromUtc);
                }
                if (dateTo.HasValue)
                {
                    var toUtc = DateTime.SpecifyKind(dateTo.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
                    receivedQuery = receivedQuery.Where(d => d.CreatedAt <= toUtc);
                }

                if (!string.IsNullOrWhiteSpace(status) && status.ToUpper() != "ALL")
                {
                    if (status.ToUpper() == "SUCCESS")
                        receivedQuery = receivedQuery.Where(d => d.Status == DonationStatus.Success);
                    else if (status.ToUpper() == "PENDING")
                        receivedQuery = receivedQuery.Where(d => d.Status == DonationStatus.Pending);
                    else if (status.ToUpper() == "FAILED")
                        receivedQuery = receivedQuery.Where(d => d.Status == DonationStatus.Failed || d.Status == DonationStatus.Cancelled);
                }

                if (!string.IsNullOrWhiteSpace(cleanKeyword))
                {
                    receivedQuery = receivedQuery.Where(d =>
                        (d.TransactionCode != null && d.TransactionCode.ToLower().Contains(cleanKeyword)) ||
                        d.DonorName.ToLower().Contains(cleanKeyword) ||
                        (d.DonorUser != null && d.DonorUser.FullName != null && d.DonorUser.FullName.ToLower().Contains(cleanKeyword)) ||
                        (d.Message != null && d.Message.ToLower().Contains(cleanKeyword)));
                }

                var rawReceivedList = await receivedQuery.OrderByDescending(d => d.CreatedAt).ToListAsync();

                viewModel.ReceivedDonations = rawReceivedList.Select(d => new DonationReceivedItemViewModel
                {
                    Id = d.Id,
                    TransactionCode = d.TransactionCode ?? $"DON-{d.Id:D6}",
                    DonorName = !string.IsNullOrWhiteSpace(d.DonorName) ? d.DonorName : (d.DonorUser?.FullName ?? d.DonorUser?.Username ?? "Fan ẩn danh"),
                    DonorAvatar = d.DonorUser?.AvatarUrl ?? "/images/default-avatar.png",
                    DonorAccountId = d.DonorUser?.AccountId,
                    Amount = d.Amount,
                    Message = d.Message, // LỜI NHẮN CỦA VIEWER
                    CreatedAt = d.CreatedAt.ToLocalTime(),
                    PaymentMethodName = d.PaymentMethod == PaymentMethod.Wallet ? "Ví số dư" : "VietQR Napas247",
                    Status = d.Status,
                    StatusBadgeClass = d.Status == DonationStatus.Success ? "bg-success" : (d.Status == DonationStatus.Pending ? "bg-warning text-dark" : "bg-danger"),
                    StatusText = d.Status == DonationStatus.Success ? "Thành công" : (d.Status == DonationStatus.Pending ? "Đang xử lý" : "Thất bại"),
                    EffectTag = d.SpecialEffectTitle
                }).ToList();

                viewModel.TotalDonationReceivedCount = viewModel.ReceivedDonations.Count;
                viewModel.TotalDonationReceivedAmount = viewModel.ReceivedDonations
                    .Where(x => x.Status == DonationStatus.Success)
                    .Sum(x => x.Amount);

                var latestWithMsg = viewModel.ReceivedDonations.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Message));
                if (latestWithMsg != null)
                {
                    viewModel.LatestReceivedDonorName = latestWithMsg.DonorName;
                    viewModel.LatestReceivedAmount = latestWithMsg.Amount;
                    viewModel.LatestReceivedMessage = latestWithMsg.Message;
                }
            }

            // ==========================================
            // 3. TRUY VẤN ĐƠN HÀNG SHOP (MUA HOẶC BÁN)
            // ==========================================
            var shopOrdersQuery = _context.ShopOrders
                .Include(o => o.StreamerProfile)
                .Include(o => o.Items)
                .AsNoTracking();

            if (isStreamer && streamerProfile != null)
            {
                // Streamer xem cả đơn bán và đơn mua
                shopOrdersQuery = shopOrdersQuery.Where(o => o.BuyerUserId == currentUserId || o.StreamerProfileId == streamerProfile.Id);
            }
            else
            {
                // Viewer chỉ xem đơn mua của mình
                shopOrdersQuery = shopOrdersQuery.Where(o => o.BuyerUserId == currentUserId);
            }

            if (dateFrom.HasValue)
            {
                var fromUtc = DateTime.SpecifyKind(dateFrom.Value.Date, DateTimeKind.Utc);
                shopOrdersQuery = shopOrdersQuery.Where(o => o.CreatedAt >= fromUtc);
            }
            if (dateTo.HasValue)
            {
                var toUtc = DateTime.SpecifyKind(dateTo.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
                shopOrdersQuery = shopOrdersQuery.Where(o => o.CreatedAt <= toUtc);
            }

            if (!string.IsNullOrWhiteSpace(cleanKeyword))
            {
                shopOrdersQuery = shopOrdersQuery.Where(o =>
                    o.OrderCode.ToLower().Contains(cleanKeyword) ||
                    o.BuyerName.ToLower().Contains(cleanKeyword) ||
                    o.StreamerProfile.DisplayName.ToLower().Contains(cleanKeyword));
            }

            var rawShopList = await shopOrdersQuery.OrderByDescending(o => o.CreatedAt).ToListAsync();

            viewModel.ShopOrders = rawShopList.Select(o => new ShopOrderHistoryItemViewModel
            {
                Id = o.Id,
                OrderCode = o.OrderCode,
                StreamerDisplayName = o.StreamerProfile.DisplayName,
                StreamerSlug = o.StreamerProfile.Slug,
                BuyerName = o.BuyerName,
                BuyerPhone = o.BuyerPhone,
                BuyerAddress = o.BuyerAddress,
                ItemsSummary = string.Join(", ", o.Items.Select(i => string.IsNullOrWhiteSpace(i.VariantSelection) ? $"{i.ProductName} (x{i.Quantity})" : $"{i.ProductName} - {i.VariantSelection} (x{i.Quantity})")),
                TotalAmount = o.TotalAmount,
                PaymentMethod = o.PaymentMethod,
                PaymentStatus = o.PaymentStatus,
                CreatedAt = o.CreatedAt.ToLocalTime(),
                IsStreamerSale = (isStreamer && streamerProfile != null && o.StreamerProfileId == streamerProfile.Id)
            }).ToList();

            viewModel.TotalShopOrdersCount = viewModel.ShopOrders.Count;
            viewModel.TotalShopAmount = viewModel.ShopOrders.Where(o => o.PaymentStatus == "Paid").Sum(o => o.TotalAmount);

            // ==========================================
            // 4. LỊCH SỬ HIỆU ỨNG SỰ KIỆN THẬT ĐÃ KÍCH HOẠT BỞI DONATE
            // ==========================================
            var effectDonations = await _context.Donations
                .Include(donation => donation.StreamerProfile)
                .AsNoTracking()
                .Where(donation => donation.SpecialEffectCode != null &&
                    (donation.DonorUserId == currentUserId || donation.StreamerProfile.UserId == currentUserId))
                .OrderByDescending(donation => donation.CreatedAt)
                .Take(100)
                .ToListAsync();

            viewModel.EffectItems = effectDonations.Select(donation => new EffectHistoryItemViewModel
            {
                Id = $"DON-{donation.Id:D6}",
                Name = donation.SpecialEffectTitle ?? "Hiệu ứng sự kiện",
                Description = "Hiệu ứng sự kiện đã chạy trên AlertBox khi khoản donate thành công.",
                IconClass = "fa-wand-magic-sparkles",
                Price = donation.Amount,
                TargetStreamer = donation.StreamerProfile.DisplayName,
                PurchasedAt = donation.CreatedAt.ToLocalTime(),
                Status = "Đã kích hoạt"
            }).ToList();
            viewModel.TotalEffectsCount = viewModel.EffectItems.Count;

            return View(viewModel);
        }

        // ==========================================
        // CÁC ROUTE CHUYỂN TIẾP CHO SHOP STREAMER (ĐẢM BẢO TƯƠNG THÍCH NGƯỢC NẾU CÓ LINK CŨ)
        // ==========================================
        [HttpGet]
        public IActionResult StreamerShop(string slug)
        {
            return RedirectToAction("StreamerShop", "Shop", new { slug });
        }

        [HttpGet]
        public IActionResult MyProducts()
        {
            return RedirectToAction("MyProducts", "Shop");
        }

        [HttpGet]
        public IActionResult Detail(string orderCode)
        {
            return RedirectToAction("Detail", "Shop", new { orderCode });
        }

        [HttpGet]
        public IActionResult History()
        {
            return RedirectToAction(nameof(Index));
        }
    }
}
