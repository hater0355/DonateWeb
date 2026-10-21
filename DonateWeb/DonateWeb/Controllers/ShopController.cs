using System;
using System.Data;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using DonateWeb.Data;
using DonateWeb.Models.Entities;
using DonateWeb.Models.Enums;
using DonateWeb.Security.Uploads;
using DonateWeb.Security.RateLimiting;
using DonateWeb.Services;
using DonateWeb.ViewModels.Shop;

namespace DonateWeb.Controllers
{
    public class ShopController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IPayOSService? _payOS;
        private readonly ILogger<ShopController>? _logger;
        private readonly IGhnShippingService? _ghnShipping;

        public ShopController(
            AppDbContext context,
            IWebHostEnvironment webHostEnvironment,
            IPayOSService? payOS = null,
            ILogger<ShopController>? logger = null,
            IGhnShippingService? ghnShipping = null)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _payOS = payOS;
            _logger = logger;
            _ghnShipping = ghnShipping;
        }

        /// <summary>
        /// 1. SÀN SẢN PHẨM / SHOP CHUNG CỦA TẤT CẢ CÁC STREAMER
        /// Hiển thị toàn bộ sản phẩm của các streamer đã đưa lên sàn, có ghi rõ từ streamer nào và thanh tìm kiếm
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index(string? keyword, string? streamerSlug, string? sortBy, string? category)
        {
            var query = _context.ShopProducts
                .Include(p => p.StreamerProfile)
                    .ThenInclude(sp => sp.User)
                .Where(p => p.IsActive
                    && p.ApprovalStatus == ProductApprovalStatus.Approved
                    && p.StreamerProfile.IsActive
                    && p.StreamerProfile.ApprovalStatus == StreamerApprovalStatus.Approved)
                .AsNoTracking();

            // Lọc theo từ khóa tìm kiếm (tên sản phẩm, mô tả, tên hiển thị streamer)
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim().ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(kw) ||
                                         (p.Description != null && p.Description.ToLower().Contains(kw)) ||
                                         p.StreamerProfile.DisplayName.ToLower().Contains(kw));
            }

            // Lọc theo Streamer cụ thể
            if (!string.IsNullOrWhiteSpace(streamerSlug))
            {
                var sSlug = streamerSlug.Trim().ToLower();
                query = query.Where(p => p.StreamerProfile.Slug.ToLower() == sSlug ||
                                         (p.StreamerProfile.User != null && p.StreamerProfile.User.AccountId != null && p.StreamerProfile.User.AccountId.ToLower() == sSlug));
            }

            if (ShopProductCategories.IsValid(category))
            {
                var selectedCategory = category!.Trim();
                query = query.Where(p => p.Category == selectedCategory);
            }

            // Sắp xếp
            query = sortBy switch
            {
                "price_asc" => query.OrderBy(p => p.Price),
                "price_desc" => query.OrderByDescending(p => p.Price),
                _ => query.OrderByDescending(p => p.CreatedAt) // Mới nhất
            };

            var products = await query.Select(p => new ShopProductCardViewModel
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Category = p.Category,
                Price = p.Price,
                ImageUrl = string.IsNullOrWhiteSpace(p.ImageUrl) ? "/images/default-banner.jpg" : p.ImageUrl,
                StockQuantity = p.StockQuantity,
                HasVariants = p.VariantsJson != "[]" && p.VariantsJson != "",
                StreamerProfileId = p.StreamerProfileId,
                StreamerName = p.StreamerProfile.DisplayName,
                StreamerSlug = !string.IsNullOrWhiteSpace(p.StreamerProfile.User != null ? p.StreamerProfile.User.AccountId : null) ? p.StreamerProfile.User!.AccountId! : p.StreamerProfile.Slug,
                StreamerAvatar = string.IsNullOrWhiteSpace(p.StreamerProfile.AvatarUrl) ? "/images/default-avatar.png" : p.StreamerProfile.AvatarUrl,
                StreamerRank = p.StreamerProfile.Rank,
                IsVerified = p.StreamerProfile.IsVerified
            }).ToListAsync();

            // Danh sách streamer để hiển thị bộ lọc
            var activeStreamers = await _context.StreamerProfiles
                .Where(s => s.IsActive
                    && s.ApprovalStatus == StreamerApprovalStatus.Approved
                    && s.ShopProducts.Any(p => p.IsActive && p.ApprovalStatus == ProductApprovalStatus.Approved))
                .Select(s => new StreamerSelectOptionViewModel
                {
                    Slug = !string.IsNullOrWhiteSpace(s.User != null ? s.User.AccountId : null) ? s.User!.AccountId! : s.Slug,
                    DisplayName = s.DisplayName
                })
                .Distinct()
                .ToListAsync();

            var viewModel = new ShopIndexViewModel
            {
                Products = products,
                SearchKeyword = keyword?.Trim(),
                SelectedStreamerSlug = streamerSlug?.Trim(),
                SelectedCategory = ShopProductCategories.IsValid(category) ? category!.Trim() : null,
                SortBy = sortBy,
                Streamers = activeStreamers,
                TotalProducts = products.Count
            };

            return View(viewModel);
        }

        /// <summary>
        /// 2. GIAN HÀNG & GIỎ HÀNG RIÊNG BIỆT CỦA TỪNG STREAMER
        /// Mỗi streamer có một giỏ hàng/gian hàng độc lập hoàn toàn
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> StreamerShop(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return RedirectToAction(nameof(Index));
            }

            var cleanSlug = slug.Trim();
            var streamer = await _context.StreamerProfiles
                .Include(s => s.User)
                .Include(s => s.ShopProducts.Where(p => p.IsActive && p.ApprovalStatus == ProductApprovalStatus.Approved))
                .FirstOrDefaultAsync(s => s.IsActive
                    && s.ApprovalStatus == StreamerApprovalStatus.Approved
                    && (s.Slug == cleanSlug || (s.User != null && s.User.AccountId == cleanSlug)));

            if (streamer == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy gian hàng của Streamer này.";
                return RedirectToAction(nameof(Index));
            }

            bool isOwner = false;
            decimal viewerBalance = 0;
            string? viewerName = null;
            string? viewerPhone = null;

            if (User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                isOwner = (streamer.UserId == uid);
                var currentUser = await _context.Users.FindAsync(uid);
                if (currentUser != null)
                {
                    viewerBalance = currentUser.WalletBalance;
                    viewerName = currentUser.FullName ?? currentUser.Username;
                    viewerPhone = currentUser.PhoneNumber;
                }
            }

            var streamerIdentifier = !string.IsNullOrWhiteSpace(streamer.User?.AccountId) ? streamer.User.AccountId : streamer.Slug;

            var viewModel = new StreamerShopViewModel
            {
                StreamerId = streamer.Id,
                StreamerName = streamer.DisplayName,
                StreamerSlug = streamerIdentifier,
                StreamerAvatar = string.IsNullOrWhiteSpace(streamer.AvatarUrl) ? "/images/default-avatar.png" : streamer.AvatarUrl,
                StreamerBanner = string.IsNullOrWhiteSpace(streamer.BannerUrl) ? "/images/default-banner.jpg" : streamer.BannerUrl,
                BankName = streamer.BankName,
                BankAccountNumber = streamer.BankAccountNumber,
                BankAccountName = streamer.BankAccountName ?? streamer.DisplayName,
                IsOwner = isOwner,
                Products = streamer.ShopProducts.OrderByDescending(p => p.CreatedAt).ToList(),
                ViewerWalletBalance = viewerBalance,
                IsViewerAuthenticated = User.Identity?.IsAuthenticated == true,
                ViewerName = viewerName,
                ViewerPhone = viewerPhone,
                GhnShippingConfigured = _ghnShipping?.IsConfigured == true
            };

            return View(viewModel);
        }

        /// <summary>
        /// 3. XỬ LÝ ĐẶT HÀNG & THANH TOÁN (HỖ TRỢ THANH TOÁN BẰNG VÍ HOẶC QUÉT MÃ VIETQR)
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout([FromBody] CheckoutRequestViewModel model, CancellationToken cancellationToken = default)
        {
            await ExpireStaleStockReservationsAsync();

            if (model == null || model.Items == null || !model.Items.Any())
            {
                return Json(new { success = false, message = "Giỏ hàng của bạn đang trống." });
            }

            if (model.Items.Count > 100 || model.Items.Any(item => item.ProductId <= 0 || item.Quantity <= 0 || item.Size?.Length > 50))
            {
                return Json(new { success = false, message = "Sản phẩm hoặc số lượng trong giỏ hàng không hợp lệ." });
            }

            var streamer = await _context.StreamerProfiles
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.Id == model.StreamerProfileId);

            if (streamer == null || !streamer.IsActive || streamer.ApprovalStatus != StreamerApprovalStatus.Approved)
            {
                return Json(new { success = false, message = "Không tìm thấy thông tin Streamer bán hàng." });
            }

            // Lấy danh sách sản phẩm hợp lệ trong database
            var productIds = model.Items.Select(i => i.ProductId).Distinct().ToList();
            var products = await _context.ShopProducts
                .Where(p => productIds.Contains(p.Id)
                    && p.StreamerProfileId == streamer.Id
                    && p.IsActive
                    && p.ApprovalStatus == ProductApprovalStatus.Approved
                    && p.StreamerProfile.IsActive
                    && p.StreamerProfile.ApprovalStatus == StreamerApprovalStatus.Approved)
                .ToListAsync();

            if (!products.Any() || products.Count != productIds.Count)
            {
                return Json(new { success = false, message = "Các sản phẩm trong giỏ không hợp lệ hoặc đã ngừng bán." });
            }

            var productMap = products.ToDictionary(product => product.Id);
            var variantError = ValidateCartSizes(model.Items, productMap);
            if (variantError != null)
            {
                return Json(new { success = false, message = variantError });
            }

            // Gộp cùng sản phẩm và cùng size; giữ riêng các size khác nhau trong đơn.
            model.Items = GroupCartItems(model.Items);
            if (model.Items.Any(item => item.Quantity > 9999))
            {
                return Json(new { success = false, message = "Mỗi size của một sản phẩm chỉ được mua tối đa 9.999 chiếc trong một đơn." });
            }
            if (model.Items.GroupBy(item => item.ProductId).Any(group => group.Sum(item => (long)item.Quantity) > 9999))
            {
                return Json(new { success = false, message = "Tổng số lượng của một sản phẩm không được vượt quá 9.999 chiếc trong một đơn." });
            }

            decimal subtotalAmount = 0;
            var orderItems = new List<ShopOrderItem>();

            foreach (var item in model.Items)
            {
                var prod = products.FirstOrDefault(p => p.Id == item.ProductId);
                if (prod != null && item.Quantity > 0)
                {
                    var unitPrice = GetPriceForSelection(prod, item.Size);
                    subtotalAmount += unitPrice * item.Quantity;
                    orderItems.Add(new ShopOrderItem
                    {
                        ShopProductId = prod.Id,
                        ProductName = prod.Name,
                        VariantSelection = item.Size,
                        UsesVariantStock = prod.Variants.Count > 0,
                        Price = unitPrice,
                        Quantity = item.Quantity
                    });
                }
            }

            var insufficientStock = FindStockShortage(model.Items, productMap);
            if (insufficientStock != null)
            {
                var product = productMap[insufficientStock.ProductId];
                var available = GetAvailableStock(product, insufficientStock.Size);
                return Json(new
                {
                    success = false,
                    message = $"Lựa chọn \"{FormatSelection(product, insufficientStock.Size)}\" của sản phẩm \"{product.Name}\" chỉ còn {available} trong kho. Vui lòng cập nhật giỏ hàng."
                });
            }

            if (subtotalAmount <= 0)
            {
                return Json(new { success = false, message = "Tổng tiền đơn hàng không hợp lệ." });
            }

            var couponResult = await CalculateCouponDiscountAsync(model.CouponCode, subtotalAmount);
            if (couponResult.Error != null)
            {
                return Json(new { success = false, message = couponResult.Error });
            }

            var coupon = couponResult.Coupon;
            var discountAmount = couponResult.DiscountAmount;
            var merchandisePayable = subtotalAmount - discountAmount;
            decimal shippingFee = 0;
            var orderAddress = model.BuyerAddress.Trim();

            // Tính lại báo giá GHN staging tại máy chủ trước khi thu tiền.
            // Không tin phí do trình duyệt gửi và không gọi endpoint tạo vận đơn.
            if (_ghnShipping != null)
            {
                if (!_ghnShipping.IsConfigured)
                {
                    return Json(new { success = false, message = "GHN staging chưa được cấu hình. Chưa thể tạo đơn vì chưa tính được phí vận chuyển." });
                }
                if (!model.GhnProvinceId.HasValue || !model.GhnWardId.HasValue || !model.QuotedShippingFee.HasValue)
                {
                    return Json(new { success = false, message = "Hãy chọn địa chỉ và tính phí vận chuyển trước khi đặt hàng." });
                }

                var productRowsForShipping = model.Items
                    .Select(item =>
                    {
                        var product = products.First(product => product.Id == item.ProductId);
                        return (Product: product, Quantity: item.Quantity * GetShippingUnits(product, item.Size));
                    })
                    .ToList();
                var (shippingQuote, shippingError) = await CalculateGhnQuoteAsync(
                    productRowsForShipping,
                    model.BuyerName,
                    model.BuyerPhone,
                    model.BuyerAddress,
                    model.GhnProvinceId.Value,
                    model.GhnWardId.Value,
                    cancellationToken);
                if (shippingError != null || shippingQuote == null)
                {
                    return Json(new { success = false, message = shippingError ?? "Không tính được phí vận chuyển." });
                }

                shippingFee = shippingQuote.Fee.TotalFee;
                orderAddress = shippingQuote.FullAddress;
                if (model.QuotedShippingFee.Value != shippingQuote.Fee.TotalFee)
                {
                    return StatusCode(StatusCodes.Status409Conflict, new
                    {
                        success = false,
                        shippingFeeChanged = true,
                        shippingFee = shippingQuote.Fee.TotalFee,
                        message = "Phí GHN vừa thay đổi. Đã cập nhật phí mới vào tổng tiền; vui lòng kiểm tra rồi bấm đặt hàng lần nữa."
                    });
                }
            }

            var totalAmount = merchandisePayable + shippingFee;

            int? currentUserId = null;
            if (User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                currentUserId = uid;
            }

            var requestedPaymentMethod = model.PaymentMethod?.Trim();
            var isWalletPayment = string.Equals(requestedPaymentMethod, "Wallet", StringComparison.OrdinalIgnoreCase);
            var isPayOsPayment = string.Equals(requestedPaymentMethod, "PayOS", StringComparison.OrdinalIgnoreCase)
                || string.Equals(requestedPaymentMethod, "VietQR", StringComparison.OrdinalIgnoreCase); // Giá trị cũ vẫn được hỗ trợ.
            if (!isWalletPayment && !isPayOsPayment)
            {
                return Json(new { success = false, message = "Phương thức thanh toán không hợp lệ." });
            }

            var paymentMethod = isWalletPayment ? "Wallet" : "PayOS";
            var guestAccessToken = currentUserId.HasValue
                ? null
                : Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var guestAccessTokenHash = guestAccessToken == null ? null : HashGuestAccessToken(guestAccessToken);

            // Mã giảm giá có thể thanh toán toàn bộ đơn mà không cần gọi cổng PayOS.
            if (totalAmount == 0)
            {
                var freeOrderCode = $"CPN{DateTime.UtcNow:yyMMddHHmmss}{Random.Shared.Next(100, 999)}";
                await using var couponTransaction = await _context.Database.BeginTransactionAsync();
                var stockError = await ReserveInventoryAsync(streamer.Id, orderItems);
                if (stockError != null)
                {
                    await couponTransaction.RollbackAsync();
                    return Json(new { success = false, message = stockError });
                }
                var freeOrder = new ShopOrder
                {
                    OrderCode = freeOrderCode,
                    GuestAccessTokenHash = guestAccessTokenHash,
                    StreamerProfileId = streamer.Id,
                    BuyerUserId = currentUserId,
                    BuyerName = model.BuyerName.Trim(),
                    BuyerPhone = model.BuyerPhone?.Trim(),
                    BuyerAddress = orderAddress,
                    Note = model.Note?.Trim(),
                    SubtotalAmount = subtotalAmount,
                    DiscountAmount = discountAmount,
                    ShippingFee = shippingFee,
                    TotalAmount = 0,
                    CouponCode = coupon?.Code,
                    DiscountCouponId = coupon?.Id,
                    PaymentMethod = "Coupon",
                    PaymentStatus = "Paid",
                    StockReserved = false,
                    TransactionCode = $"COUPON-{freeOrderCode}",
                    CreatedAt = DateTime.UtcNow,
                    PaidAt = DateTime.UtcNow
                };
                foreach (var orderItem in orderItems)
                {
                    freeOrder.Items.Add(orderItem);
                }
                _context.ShopOrders.Add(freeOrder);
                if (coupon != null)
                {
                    coupon.UsedCount++;
                    coupon.UpdatedAtUtc = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync();
                await couponTransaction.CommitAsync();
                if (guestAccessToken != null)
                {
                    SetGuestOrderAccessCookie(freeOrder.OrderCode, guestAccessToken);
                }

                return Json(new
                {
                    success = true,
                    orderCode = freeOrder.OrderCode,
                    isPaid = true,
                    subtotalAmount,
                    discountAmount,
                    totalAmount,
                    message = "Mã giảm giá đã thanh toán toàn bộ đơn hàng. Đơn hàng được ghi nhận thành công."
                });
            }

            if (isPayOsPayment && _payOS == null)
            {
                return Json(new { success = false, message = "Thanh toán PayOS chưa được cấu hình. Vui lòng chọn Ví hoặc liên hệ quản trị viên." });
            }

            if (isPayOsPayment && (decimal.Truncate(totalAmount) != totalAmount || totalAmount > 500000000m))
            {
                return Json(new { success = false, message = "Tổng tiền thanh toán PayOS phải là số nguyên và không vượt quá 500.000.000 VNĐ." });
            }

            var payOsOrderCode = isPayOsPayment && totalAmount > 0 ? await CreateUniquePayOSOrderCodeAsync() : (long?)null;
            var orderCode = payOsOrderCode?.ToString(CultureInfo.InvariantCulture)
                ?? $"ORD{DateTime.UtcNow:yyMMddHHmmss}{Random.Shared.Next(100, 999)}";

            // XỬ LÝ PHƯƠNG THỨC 1: THANH TOÁN BẰNG TIỀN TRONG VÍ
            if (paymentMethod == "Wallet")
            {
                if (!currentUserId.HasValue)
                {
                    return Json(new { success = false, message = "Vui lòng đăng nhập tài khoản để thanh toán bằng số dư Ví!" });
                }

                await using var walletTransaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var stockError = await ReserveInventoryAsync(streamer.Id, orderItems);
                if (stockError != null)
                {
                    await walletTransaction.RollbackAsync();
                    return Json(new { success = false, message = stockError });
                }
                var buyer = await _context.Users.FindAsync(currentUserId.Value);
                if (buyer == null)
                {
                    await walletTransaction.RollbackAsync();
                    return Json(new { success = false, message = "Tài khoản người mua không hợp lệ." });
                }

                var deducted = await _context.Users
                    .Where(user => user.Id == buyer.Id && user.WalletBalance >= totalAmount)
                    .ExecuteUpdateAsync(update => update.SetProperty(
                        user => user.WalletBalance,
                        user => user.WalletBalance - totalAmount));
                if (deducted == 0)
                {
                    await _context.Entry(buyer).ReloadAsync();
                    await walletTransaction.RollbackAsync();
                    return Json(new { 
                        success = false, 
                        message = $"Số dư ví của bạn không đủ ({buyer.WalletBalance:N0} VNĐ) để thanh toán đơn hàng {totalAmount:N0} VNĐ. Vui lòng nạp thêm tiền hoặc chọn thanh toán quét mã QR!" 
                    });
                }

                await _context.Entry(buyer).ReloadAsync();
                var buyerBalanceAfter = buyer.WalletBalance;

                // Cộng tiền doanh thu cho Streamer
                var streamerUser = await _context.Users.FindAsync(streamer.UserId);
                decimal? streamerBalanceAfter = null;
                var streamerNet = ShopCommission.StreamerNet(merchandisePayable);
                if (streamerUser != null)
                {
                    await _context.Users
                        .Where(user => user.Id == streamerUser.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(
                            user => user.StreamerEarningsBalance,
                            user => user.StreamerEarningsBalance + streamerNet));
                    await _context.Entry(streamerUser).ReloadAsync();
                    streamerBalanceAfter = streamerUser.StreamerEarningsBalance;
                }

                // Ghi nhận lịch sử giao dịch ví cho Người mua
                _context.WalletTransactions.Add(new WalletTransaction
                {
                    UserId = buyer.Id,
                    TransactionCode = $"WTX-{orderCode}-BUY",
                    TransactionType = "PURCHASE",
                    Amount = totalAmount,
                    BalanceBefore = buyerBalanceAfter + totalAmount,
                    BalanceAfter = buyerBalanceAfter,
                    PaymentMethodName = "Ví số dư OnlyFan",
                    Status = WalletTransaction.StatusCompleted,
                    Note = $"Thanh toán đơn hàng #{orderCode} tại Shop của {streamer.DisplayName}; gồm phí vận chuyển {shippingFee:N0} VNĐ",
                    CreatedAt = DateTime.UtcNow
                });

                // Ghi nhận lịch sử doanh thu cho Streamer
                if (streamerUser != null)
                {
                    _context.WalletTransactions.Add(new WalletTransaction
                    {
                        UserId = streamerUser.Id,
                        TransactionCode = $"WTX-{orderCode}-REV",
                        TransactionType = "SHOP_SALE",
                        Amount = streamerNet,
                        BalanceBefore = streamerBalanceAfter!.Value - streamerNet,
                        BalanceAfter = streamerBalanceAfter.Value,
                        PaymentMethodName = "Ví số dư OnlyFan",
                        Status = WalletTransaction.StatusCompleted,
                        Note = $"Doanh thu bán hàng đơn #{orderCode} từ khách {model.BuyerName.Trim()}. Phí vận chuyển: {shippingFee:N0} VNĐ; phí nền tảng hàng hóa: {ShopCommission.PlatformFee(merchandisePayable):N0} VNĐ; streamer nhận: {streamerNet:N0} VNĐ.",
                        CreatedAt = DateTime.UtcNow
                    });
                }

                // Tạo đơn hàng trạng thái ĐÃ THANH TOÁN
                var paidOrder = new ShopOrder
                {
                    OrderCode = orderCode,
                    StreamerProfileId = streamer.Id,
                    BuyerUserId = buyer.Id,
                    BuyerName = string.IsNullOrWhiteSpace(model.BuyerName) ? buyer.FullName ?? buyer.Username : model.BuyerName.Trim(),
                    BuyerPhone = model.BuyerPhone?.Trim(),
                    BuyerAddress = orderAddress,
                    Note = model.Note?.Trim(),
                    SubtotalAmount = subtotalAmount,
                    DiscountAmount = discountAmount,
                    ShippingFee = shippingFee,
                    TotalAmount = totalAmount,
                    CouponCode = coupon?.Code,
                    DiscountCouponId = coupon?.Id,
                    PaymentMethod = "Wallet",
                    PaymentStatus = "Paid",
                    StockReserved = false,
                    TransactionCode = $"TX-WALLET-{orderCode}",
                    CreatedAt = DateTime.UtcNow,
                    PaidAt = DateTime.UtcNow
                };

                foreach (var oi in orderItems)
                {
                    paidOrder.Items.Add(oi);
                }

                if (coupon != null)
                {
                    coupon.UsedCount++;
                    coupon.UpdatedAtUtc = DateTime.UtcNow;
                }

                _context.ShopOrders.Add(paidOrder);
                await _context.SaveChangesAsync();
                await walletTransaction.CommitAsync();

                return Json(new
                {
                    success = true,
                    orderCode = paidOrder.OrderCode,
                    isPaid = true,
                    subtotalAmount,
                    discountAmount,
                    totalAmount,
                    message = "Thanh toán bằng số dư Ví thành công! Đơn hàng đã được ghi nhận."
                });
            }

            // XỬ LÝ PHƯƠNG THỨC 2: TẠO THANH TOÁN PAYOS VÀ QR CHO ĐƠN HÀNG
            var pendingOrder = new ShopOrder
            {
                OrderCode = orderCode,
                GuestAccessTokenHash = guestAccessTokenHash,
                StreamerProfileId = streamer.Id,
                BuyerUserId = currentUserId,
                BuyerName = model.BuyerName.Trim(),
                BuyerPhone = model.BuyerPhone?.Trim(),
                BuyerAddress = orderAddress,
                Note = model.Note?.Trim(),
                SubtotalAmount = subtotalAmount,
                DiscountAmount = discountAmount,
                ShippingFee = shippingFee,
                TotalAmount = totalAmount,
                CouponCode = coupon?.Code,
                DiscountCouponId = coupon?.Id,
                PaymentMethod = "PayOS",
                PaymentStatus = "Pending",
                TransactionCode = $"PAYOS-{orderCode}",
                CreatedAt = DateTime.UtcNow
            };

            foreach (var oi in orderItems)
            {
                pendingOrder.Items.Add(oi);
            }

            await using (var pendingTransaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                var stockError = await ReserveInventoryAsync(streamer.Id, orderItems);
                if (stockError != null)
                {
                    await pendingTransaction.RollbackAsync();
                    return Json(new { success = false, message = stockError });
                }

                pendingOrder.StockReserved = true;
                _context.ShopOrders.Add(pendingOrder);
                await _context.SaveChangesAsync();
                await pendingTransaction.CommitAsync();
            }

            try
            {
                var publicBaseUrl = $"{Request.Scheme}://{Request.Host}";
                var encodedOrderCode = Uri.EscapeDataString(pendingOrder.OrderCode);
                var detailUrl = $"{publicBaseUrl}/Shop/Detail?orderCode={encodedOrderCode}";
                var description = $"SHOP {payOsOrderCode!.Value.ToString(CultureInfo.InvariantCulture)}";
                var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);

                var payment = await _payOS!.createPaymentLink(new PaymentData
                {
                    orderCode = payOsOrderCode.Value,
                    amount = decimal.ToInt32(totalAmount),
                    description = description,
                    cancelUrl = $"{detailUrl}&payment=cancelled",
                    returnUrl = $"{detailUrl}&payment=returned",
                    buyerName = pendingOrder.BuyerName,
                    buyerPhone = pendingOrder.BuyerPhone,
                    buyerAddress = pendingOrder.BuyerAddress,
                    expiredAt = (int)expiresAt.ToUnixTimeSeconds()
                });

                if (string.IsNullOrWhiteSpace(payment.qrCode))
                {
                    throw new InvalidOperationException("PayOS không trả về dữ liệu QR cho đơn hàng.");
                }

                if (guestAccessToken != null)
                {
                    SetGuestOrderAccessCookie(pendingOrder.OrderCode, guestAccessToken);
                }

                return Json(new
                {
                    success = true,
                    orderCode = pendingOrder.OrderCode,
                    isPaid = false,
                    qrBase64 = GenerateQrCodeBase64(payment.qrCode),
                    checkoutUrl = payment.checkoutUrl,
                    bankName = string.IsNullOrWhiteSpace(payment.bin) ? "PayOS" : payment.bin,
                    bankAccountNumber = payment.accountNumber,
                    bankAccountName = payment.accountName,
                    totalAmount = totalAmount,
                    formattedAmount = totalAmount.ToString("N0") + " VNĐ",
                    subtotalAmount,
                    discountAmount,
                    couponCode = coupon?.Code,
                    transferContent = payment.description,
                    message = "Tạo thanh toán PayOS thành công. Quét mã QR hoặc mở trang thanh toán để hoàn tất đơn hàng."
                });
            }
            catch (Exception ex)
            {
                await using var releaseTransaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var failedOrder = await _context.ShopOrders
                    .Include(order => order.Items)
                    .FirstOrDefaultAsync(order => order.Id == pendingOrder.Id);
                if (failedOrder != null && failedOrder.PaymentStatus == "Pending")
                {
                    await ReleaseReservedStockAsync(failedOrder);
                    failedOrder.PaymentStatus = "Cancelled";
                    failedOrder.StockReserved = false;
                    await _context.SaveChangesAsync();
                }
                await releaseTransaction.CommitAsync();
                _logger?.LogError(ex, "Không tạo được PayOS payment link cho đơn hàng {OrderCode}.", pendingOrder.OrderCode);
                return Json(new { success = false, message = "Không tạo được mã thanh toán PayOS. Vui lòng thử lại sau; đơn hàng này đã bị hủy." });
            }
        }

        /// <summary>
        /// Xác thực mã trên máy chủ và trả số tiền xem trước. Checkout luôn tính lại trước khi tạo đơn.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PreviewCoupon([FromBody] CouponPreviewRequestViewModel model)
        {
            if (model == null || model.Items == null || model.Items.Count == 0 || string.IsNullOrWhiteSpace(model.CouponCode))
            {
                return Json(new { success = false, message = "Nhập mã giảm giá và chọn ít nhất một sản phẩm." });
            }

            if (model.Items.Count > 100 || model.Items.Any(item => item.ProductId <= 0 || item.Quantity <= 0 || item.Size?.Length > 50))
            {
                return Json(new { success = false, message = "Sản phẩm hoặc số lượng trong giỏ hàng không hợp lệ." });
            }

            var productIds = model.Items.Select(item => item.ProductId).Distinct().ToList();
            var products = await _context.ShopProducts
                .Where(product => productIds.Contains(product.Id)
                    && product.StreamerProfileId == model.StreamerProfileId
                    && product.IsActive
                    && product.ApprovalStatus == ProductApprovalStatus.Approved
                    && product.StreamerProfile.IsActive
                    && product.StreamerProfile.ApprovalStatus == StreamerApprovalStatus.Approved)
                .ToDictionaryAsync(product => product.Id);

            if (products.Count != productIds.Count)
            {
                return Json(new { success = false, message = "Một hoặc nhiều sản phẩm đã ngừng bán. Hãy tải lại gian hàng." });
            }

            var variantError = ValidateCartSizes(model.Items, products);
            if (variantError != null)
            {
                return Json(new { success = false, message = variantError });
            }

            model.Items = GroupCartItems(model.Items);
            if (model.Items.Any(item => item.Quantity > 9999))
            {
                return Json(new { success = false, message = "Mỗi size của một sản phẩm chỉ được mua tối đa 9.999 chiếc trong một đơn." });
            }
            if (model.Items.GroupBy(item => item.ProductId).Any(group => group.Sum(item => (long)item.Quantity) > 9999))
            {
                return Json(new { success = false, message = "Tổng số lượng của một sản phẩm không được vượt quá 9.999 chiếc trong một đơn." });
            }

            var outOfStock = FindStockShortage(model.Items, products);
            if (outOfStock != null)
            {
                return Json(new { success = false, message = $"Lựa chọn \"{FormatSelection(products[outOfStock.ProductId], outOfStock.Size)}\" của sản phẩm \"{products[outOfStock.ProductId].Name}\" không còn đủ hàng trong kho." });
            }

            var subtotalAmount = model.Items.Sum(item => GetPriceForSelection(products[item.ProductId], item.Size) * item.Quantity);
            if (subtotalAmount <= 0)
            {
                return Json(new { success = false, message = "Tổng tiền đơn hàng không hợp lệ." });
            }

            var result = await CalculateCouponDiscountAsync(model.CouponCode, subtotalAmount);
            if (result.Error != null)
            {
                return Json(new { success = false, message = result.Error });
            }

            return Json(new
            {
                success = true,
                couponCode = result.Coupon!.Code,
                subtotalAmount,
                discountAmount = result.DiscountAmount,
                totalAmount = subtotalAmount - result.DiscountAmount,
                message = $"Đã áp dụng mã {result.Coupon.Code}. Giảm {result.DiscountAmount:N0} VNĐ."
            });
        }

        /// <summary>Danh sách địa chỉ GHN dạng tỉnh/thành - phường/xã mới nhất.</summary>
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [DonateRateLimit(30, 60, "ghn_master_data")]
        public async Task<IActionResult> GhnProvinces(CancellationToken cancellationToken)
        {
            if (_ghnShipping?.IsConfigured != true)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "Chưa cấu hình GHN staging." });
            }

            try
            {
                return Json(new { success = true, items = await _ghnShipping.GetProvincesAsync(cancellationToken) });
            }
            catch (GhnShippingException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { success = false, message = ex.Message });
            }
        }

        /// <summary>Trả danh sách phường/xã thuộc một tỉnh theo danh mục GHN.</summary>
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [DonateRateLimit(30, 60, "ghn_master_data")]
        public async Task<IActionResult> GhnWards(int provinceId, CancellationToken cancellationToken)
        {
            if (provinceId <= 0)
            {
                return BadRequest(new { success = false, message = "Tỉnh/thành không hợp lệ." });
            }
            if (_ghnShipping?.IsConfigured != true)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "Chưa cấu hình GHN staging." });
            }

            try
            {
                return Json(new { success = true, items = await _ghnShipping.GetWardsAsync(provinceId, cancellationToken) });
            }
            catch (GhnShippingException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Chỉ gọi GHN Preview ở staging để tính phí tham khảo. Không lưu đơn và không gọi API tạo vận đơn.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [DonateRateLimit(10, 60, "ghn_shipping_preview")]
        public async Task<IActionResult> PreviewGhnShippingFee(
            [FromBody] GhnFeePreviewRequestViewModel? model,
            CancellationToken cancellationToken)
        {
            if (model == null || !ModelState.IsValid || model.Items == null || model.Items.Count == 0 || model.Items.Count > 100)
            {
                return BadRequest(new { success = false, message = "Vui lòng kiểm tra giỏ hàng và thông tin nhận hàng." });
            }
            if (_ghnShipping?.IsConfigured != true)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "Chưa cấu hình GHN staging." });
            }

            if (model.Items.Any(item => item.ProductId <= 0 || item.Quantity <= 0 || item.Size?.Length > 50))
            {
                return BadRequest(new { success = false, message = "Số lượng sản phẩm trong giỏ không hợp lệ." });
            }

            var streamer = await _context.StreamerProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == model.StreamerProfileId
                    && item.IsActive
                    && item.ApprovalStatus == StreamerApprovalStatus.Approved, cancellationToken);
            if (streamer == null)
            {
                return BadRequest(new { success = false, message = "Không tìm thấy gian hàng đang hoạt động." });
            }

            var productIds = model.Items.Select(item => item.ProductId).Distinct().ToArray();
            var products = await _context.ShopProducts
                .AsNoTracking()
                .Where(product => productIds.Contains(product.Id)
                    && product.StreamerProfileId == streamer.Id
                    && product.IsActive
                    && product.ApprovalStatus == ProductApprovalStatus.Approved)
                .ToDictionaryAsync(product => product.Id, cancellationToken);
            if (products.Count != productIds.Length)
            {
                return BadRequest(new { success = false, message = "Một hoặc nhiều sản phẩm đã ngừng bán. Hãy tải lại gian hàng." });
            }

            var variantError = ValidateCartSizes(model.Items, products);
            if (variantError != null)
            {
                return BadRequest(new { success = false, message = variantError });
            }

            var groupedItems = GroupCartItems(model.Items);
            if (groupedItems.Any(item => item.Quantity > 9999)
                || groupedItems.Sum(item => (long)item.Quantity) > 9999)
            {
                return BadRequest(new { success = false, message = "Số lượng sản phẩm trong giỏ không hợp lệ." });
            }

            var outOfStock = FindStockShortage(groupedItems, products);
            if (outOfStock != null)
            {
                return BadRequest(new { success = false, message = $"Lựa chọn \"{FormatSelection(products[outOfStock.ProductId], outOfStock.Size)}\" của sản phẩm \"{products[outOfStock.ProductId].Name}\" không còn đủ hàng trong kho." });
            }

            if (groupedItems.Any(item =>
            {
                var product = products[item.ProductId];
                return !product.WeightGrams.HasValue || !product.LengthCm.HasValue
                    || !product.WidthCm.HasValue || !product.HeightCm.HasValue;
            }))
            {
                var missingSpecProduct = groupedItems.First(item =>
                {
                    var product = products[item.ProductId];
                    return !product.WeightGrams.HasValue || !product.LengthCm.HasValue
                        || !product.WidthCm.HasValue || !product.HeightCm.HasValue;
                });
                return BadRequest(new
                {
                    success = false,
                    message = $"Streamer cần bổ sung cân nặng và kích thước đóng gói cho sản phẩm \"{products[missingSpecProduct.ProductId].Name}\" trước khi tính phí GHN."
                });
            }

            var productRows = groupedItems.Select(item =>
            {
                var product = products[item.ProductId];
                return (Product: product, Quantity: item.Quantity * GetShippingUnits(product, item.Size));
            }).ToList();
            var totalWeight = productRows.Sum(row => (long)row.Product.WeightGrams!.Value * row.Quantity);
            if (totalWeight <= 0 || totalWeight > 50000)
            {
                return BadRequest(new { success = false, message = "GHN staging chỉ xem báo phí cho kiện hàng từ 1 đến 50.000 gram." });
            }

            // Ước lượng một thùng chung theo tổng thể tích và cạnh lớn nhất đã lưu của từng sản phẩm.
            // Người bán vẫn cần đóng gói và đo lại kiện thực tế trước khi chuyển sang vận hành thật.
            var length = productRows.Max(row => row.Product.LengthCm!.Value);
            var width = productRows.Max(row => row.Product.WidthCm!.Value);
            var maxHeight = productRows.Max(row => row.Product.HeightCm!.Value);
            var totalVolume = productRows.Sum(row =>
                (decimal)row.Product.LengthCm!.Value * row.Product.WidthCm!.Value * row.Product.HeightCm!.Value * row.Quantity);
            var height = Math.Max(maxHeight, (int)Math.Ceiling(totalVolume / (length * (decimal)width)));
            if (length > 200 || width > 200 || height > 200)
            {
                return BadRequest(new { success = false, message = "Kích thước kiện ước lượng vượt giới hạn GHN 200 cm. Hãy giảm số lượng hoặc kiểm tra kích thước sản phẩm." });
            }

            try
            {
                var provinces = await _ghnShipping.GetProvincesAsync(cancellationToken);
                var province = provinces.FirstOrDefault(item => item.Id == model.ProvinceId);
                if (province == null)
                {
                    return BadRequest(new { success = false, message = "Tỉnh/thành nhận hàng không có trong danh mục GHN." });
                }

                var wards = await _ghnShipping.GetWardsAsync(model.ProvinceId, cancellationToken);
                var ward = wards.FirstOrDefault(item => item.Id == model.WardId);
                if (ward == null)
                {
                    return BadRequest(new { success = false, message = "Phường/xã không thuộc tỉnh/thành đã chọn." });
                }

                var addressParts = new List<string> { model.BuyerStreet.Trim() };
                if (!model.BuyerStreet.Contains(ward.Name, StringComparison.OrdinalIgnoreCase))
                {
                    addressParts.Add(ward.Name);
                }
                if (!model.BuyerStreet.Contains(province.Name, StringComparison.OrdinalIgnoreCase))
                {
                    addressParts.Add(province.Name);
                }
                var fullAddress = string.Join(", ", addressParts);
                if (fullAddress.Length > 1024)
                {
                    return BadRequest(new { success = false, message = "Địa chỉ nhận hàng quá dài." });
                }

                var previewItems = productRows.Select(row => new GhnShippingItem(
                    row.Product.Name,
                    row.Quantity,
                    (long)decimal.Truncate(row.Product.Price),
                    row.Product.WeightGrams!.Value,
                    row.Product.LengthCm!.Value,
                    row.Product.WidthCm!.Value,
                    row.Product.HeightCm!.Value)).ToArray();
                var quote = await _ghnShipping.PreviewFeeAsync(new GhnFeePreviewInput(
                    model.BuyerName.Trim(),
                    model.BuyerPhone.Trim(),
                    fullAddress,
                    ward.Name,
                    province.Name,
                    (int)totalWeight,
                    length,
                    width,
                    height,
                    totalWeight >= 20000 ? 5 : 2,
                    previewItems), cancellationToken);

                return Json(new
                {
                    success = true,
                    totalFee = quote.TotalFee,
                    expectedDeliveryTime = quote.ExpectedDeliveryTime,
                    packageEstimate = new { weightGrams = totalWeight, lengthCm = length, widthCm = width, heightCm = height },
                    message = "Đã nhận phí xem trước từ GHN staging. Phí này được cộng vào thanh toán thử trên DonateWeb; GHN không tạo đơn vận chuyển hay mã vận đơn."
                });
            }
            catch (GhnShippingException ex)
            {
                _logger?.LogWarning("GHN staging không báo được phí: {Reason}", ex.Message);
                return StatusCode(StatusCodes.Status502BadGateway, new { success = false, message = ex.Message });
            }
        }

        private async Task<(GhnQuoteComputation? Quote, string? Error)> CalculateGhnQuoteAsync(
            IReadOnlyList<(ShopProduct Product, int Quantity)> productRows,
            string recipientName,
            string recipientPhone,
            string recipientAddress,
            int provinceId,
            int wardId,
            CancellationToken cancellationToken)
        {
            if (_ghnShipping?.IsConfigured != true)
            {
                return (null, "GHN staging chưa được cấu hình để tính phí vận chuyển.");
            }

            var missingProduct = productRows
                .Select(row => row.Product)
                .FirstOrDefault(product =>
                    !product.WeightGrams.HasValue || !product.LengthCm.HasValue
                    || !product.WidthCm.HasValue || !product.HeightCm.HasValue
                    || product.WeightGrams <= 0 || product.LengthCm <= 0
                    || product.WidthCm <= 0 || product.HeightCm <= 0);
            if (missingProduct != null)
            {
                return (null, $"Streamer cần bổ sung cân nặng và kích thước đóng gói cho sản phẩm \"{missingProduct.Name}\" trước khi tính phí GHN.");
            }

            if (productRows.Count == 0)
            {
                return (null, "Giỏ hàng đang trống.");
            }

            var weight = productRows.Sum(row => (long)row.Product.WeightGrams!.Value * row.Quantity);
            if (weight <= 0 || weight > 50000)
            {
                return (null, "GHN chỉ nhận báo phí cho kiện hàng từ 1 đến 50.000 gram.");
            }

            var length = productRows.Max(row => row.Product.LengthCm!.Value);
            var width = productRows.Max(row => row.Product.WidthCm!.Value);
            var maxHeight = productRows.Max(row => row.Product.HeightCm!.Value);
            var volume = productRows.Sum(row =>
                (decimal)row.Product.LengthCm!.Value * row.Product.WidthCm!.Value * row.Product.HeightCm!.Value * row.Quantity);
            var height = Math.Max(maxHeight, (int)Math.Ceiling(volume / (length * (decimal)width)));
            if (length > 200 || width > 200 || height > 200)
            {
                return (null, "Kích thước kiện ước lượng vượt giới hạn GHN 200 cm. Hãy giảm số lượng hoặc kiểm tra kích thước sản phẩm.");
            }

            try
            {
                var provinces = await _ghnShipping.GetProvincesAsync(cancellationToken);
                var province = provinces.FirstOrDefault(item => item.Id == provinceId);
                if (province == null)
                {
                    return (null, "Tỉnh/thành nhận hàng không có trong danh mục GHN.");
                }

                var wards = await _ghnShipping.GetWardsAsync(provinceId, cancellationToken);
                var ward = wards.FirstOrDefault(item => item.Id == wardId);
                if (ward == null)
                {
                    return (null, "Phường/xã không thuộc tỉnh/thành đã chọn.");
                }

                var addressParts = new List<string> { recipientAddress.Trim() };
                if (!recipientAddress.Contains(ward.Name, StringComparison.OrdinalIgnoreCase))
                {
                    addressParts.Add(ward.Name);
                }
                if (!recipientAddress.Contains(province.Name, StringComparison.OrdinalIgnoreCase))
                {
                    addressParts.Add(province.Name);
                }
                var fullAddress = string.Join(", ", addressParts);
                if (fullAddress.Length > 300)
                {
                    return (null, "Địa chỉ sau khi thêm phường/xã và tỉnh/thành không được vượt quá 300 ký tự.");
                }

                var items = productRows.Select(row => new GhnShippingItem(
                    row.Product.Name,
                    row.Quantity,
                    (long)decimal.Truncate(row.Product.Price),
                    row.Product.WeightGrams!.Value,
                    row.Product.LengthCm!.Value,
                    row.Product.WidthCm!.Value,
                    row.Product.HeightCm!.Value)).ToArray();
                var fee = await _ghnShipping.PreviewFeeAsync(new GhnFeePreviewInput(
                    recipientName.Trim(), recipientPhone.Trim(), fullAddress,
                    ward.Name, province.Name, (int)weight, length, width, height,
                    weight >= 20000 ? 5 : 2, items), cancellationToken);

                return (new GhnQuoteComputation(
                    fee,
                    new GhnPackageEstimate(weight, length, width, height),
                    fullAddress), null);
            }
            catch (GhnShippingException ex)
            {
                _logger?.LogWarning("GHN staging không báo được phí: {Reason}", ex.Message);
                return (null, ex.Message);
            }
        }

        private sealed record GhnPackageEstimate(long WeightGrams, int LengthCm, int WidthCm, int HeightCm);
        private sealed record GhnQuoteComputation(GhnFeePreviewResult Fee, GhnPackageEstimate Package, string FullAddress);

        private async Task<(DiscountCoupon? Coupon, decimal DiscountAmount, string? Error)> CalculateCouponDiscountAsync(
            string? couponCode,
            decimal subtotalAmount)
        {
            if (string.IsNullOrWhiteSpace(couponCode))
            {
                return (null, 0, null);
            }

            var normalizedCode = couponCode.Trim().ToUpperInvariant();
            var coupon = await _context.DiscountCoupons.FirstOrDefaultAsync(item =>
                item.Code == normalizedCode && item.IsActive
                && (item.ExpiresAtUtc == null || item.ExpiresAtUtc > DateTime.UtcNow));

            if (coupon == null)
            {
                return (null, 0, "Mã giảm giá không hợp lệ, đã hết hạn hoặc đã bị tắt.");
            }

            var discountAmount = coupon.DiscountType switch
            {
                "Percent" => Math.Round(subtotalAmount * coupon.DiscountValue / 100m, 0, MidpointRounding.AwayFromZero),
                "Fixed" => Math.Min(subtotalAmount, coupon.DiscountValue),
                _ => 0m
            };

            if (discountAmount <= 0)
            {
                return (null, 0, "Mã giảm giá này chưa được cấu hình đúng. Vui lòng liên hệ quản trị viên.");
            }

            return (coupon, Math.Min(subtotalAmount, discountAmount), null);
        }

        /// <summary>
        /// Trả trạng thái đơn cho trang QR/chi tiết. Mã đơn PayOS là mã dài, ngẫu nhiên và chỉ trả về trạng thái, không trả dữ liệu cá nhân.
        /// </summary>
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> CheckPaymentStatus(string orderCode)
        {
            if (string.IsNullOrWhiteSpace(orderCode))
            {
                return BadRequest(new { success = false, message = "Thiếu mã đơn hàng." });
            }

            var order = await _context.ShopOrders
                .Include(o => o.StreamerProfile)
                .AsNoTracking()
                .Where(o => o.OrderCode == orderCode.Trim() && o.PaymentMethod == "PayOS")
                .Select(o => new { o.OrderCode, o.PaymentStatus, o.PaidAt, o.BuyerUserId, StreamerUserId = o.StreamerProfile.UserId, o.GuestAccessTokenHash })
                .FirstOrDefaultAsync();

            if (order == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy đơn hàng PayOS." });
            }

            if (!CanAccessShopOrder(order.BuyerUserId, order.StreamerUserId, order.GuestAccessTokenHash, order.OrderCode))
            {
                return NotFound(new { success = false, message = "Không tìm thấy đơn hàng PayOS." });
            }

            await ExpireStaleStockReservationsAsync();
            var paymentState = await _context.ShopOrders
                .AsNoTracking()
                .Where(o => o.OrderCode == order.OrderCode)
                .Select(o => new { o.OrderCode, o.PaymentStatus, o.PaidAt })
                .FirstOrDefaultAsync();

            if (paymentState == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy đơn hàng PayOS." });
            }

            return Json(new
            {
                success = true,
                orderCode = paymentState.OrderCode,
                status = paymentState.PaymentStatus,
                isPaid = paymentState.PaymentStatus == "Paid",
                paidAt = paymentState.PaidAt
            });
        }

        /// <summary>
        /// 4. XEM CHI TIẾT ĐƠN HÀNG
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Detail(string orderCode, string? payment)
        {
            if (string.IsNullOrWhiteSpace(orderCode))
            {
                return RedirectToAction(nameof(Index));
            }

            var order = await _context.ShopOrders
                .Include(o => o.StreamerProfile)
                    .ThenInclude(s => s.User)
                .Include(o => o.Items)
                    .ThenInclude(i => i.ShopProduct)
                .FirstOrDefaultAsync(o => o.OrderCode == orderCode.Trim());

            if (order == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy thông tin đơn hàng.";
                return RedirectToAction(nameof(Index));
            }

            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            Response.Headers.Pragma = "no-cache";
            Response.Headers["Referrer-Policy"] = "no-referrer";
            if (!CanAccessShopOrder(order.BuyerUserId, order.StreamerProfile.UserId, order.GuestAccessTokenHash, order.OrderCode))
            {
                return NotFound();
            }

            if (string.Equals(payment, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                await CancelPendingPayOsOrderAsync(orderCode.Trim());
                await _context.Entry(order).ReloadAsync();
            }

            return View(order);
        }

        private bool CanAccessShopOrder(int? buyerUserId, int streamerUserId, string? guestAccessTokenHash, string orderCode)
        {
            if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                if (User.IsInRole(UserRoles.Admin) || buyerUserId == userId ||
                    (User.IsInRole(UserRoles.Streamer) && streamerUserId == userId))
                {
                    return true;
                }
            }

            if (buyerUserId.HasValue || string.IsNullOrWhiteSpace(guestAccessTokenHash) ||
                !Request.Cookies.TryGetValue(GetGuestOrderCookieName(orderCode), out var token) ||
                string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            var actualHash = Convert.FromHexString(HashGuestAccessToken(token));
            var expectedHash = Convert.FromHexString(guestAccessTokenHash);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }

        private void SetGuestOrderAccessCookie(string orderCode, string token)
        {
            Response.Cookies.Append(GetGuestOrderCookieName(orderCode), token, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Path = "/Shop",
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });
        }

        private static string GetGuestOrderCookieName(string orderCode)
        {
            var codeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(orderCode)));
            return $"shop_order_{codeHash[..24]}";
        }

        private static string HashGuestAccessToken(string token)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }

        /// <summary>
        /// 5. LỊCH SỬ ĐƠN HÀNG (CỦA VIEWER VÀ STREAMER)
        /// </summary>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> History()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                return RedirectToAction("Login", "Auth");
            }

            var isStreamer = User.IsInRole(UserRoles.Streamer);
            var myPurchases = await _context.ShopOrders
                .Include(o => o.StreamerProfile)
                .Include(o => o.Items)
                .Where(o => o.BuyerUserId == uid)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            List<ShopOrder> streamerReceivedOrders = new();
            if (isStreamer)
            {
                var streamer = await _context.StreamerProfiles.FirstOrDefaultAsync(s => s.UserId == uid);
                if (streamer != null)
                {
                    streamerReceivedOrders = await _context.ShopOrders
                        .Include(o => o.Items)
                        .Where(o => o.StreamerProfileId == streamer.Id)
                        .OrderByDescending(o => o.CreatedAt)
                        .ToListAsync();
                }
            }

            ViewBag.Purchases = myPurchases;
            ViewBag.StreamerOrders = streamerReceivedOrders;
            ViewBag.IsStreamer = isStreamer;

            return View();
        }

        /// <summary>
        /// 6. STREAMER QUẢN LÝ SẢN PHẨM (THÊM / SỬA / XÓA)
        /// </summary>
        [Authorize(Roles = UserRoles.Streamer)]
        [HttpGet]
        public async Task<IActionResult> MyProducts(string? status)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                return RedirectToAction("Login", "Auth");
            }

            var streamer = await _context.StreamerProfiles
                .Include(s => s.User)
                .Include(s => s.ShopProducts)
                .FirstOrDefaultAsync(s => s.UserId == uid);

            if (streamer == null)
            {
                TempData["ErrorMessage"] = "Bạn chưa có hồ sơ Streamer.";
                return RedirectToAction("Register", "Streamer");
            }

            if (!streamer.IsActive || streamer.ApprovalStatus != StreamerApprovalStatus.Approved)
            {
                TempData["ErrorMessage"] = "Chỉ Streamer đang hoạt động và đã được Admin phê duyệt mới được quản lý sản phẩm.";
                return RedirectToAction("Index", "Home");
            }

            var allProducts = streamer.ShopProducts.ToList();
            var pendingCount = allProducts.Count(p => p.IsActive && p.ApprovalStatus == ProductApprovalStatus.Pending);
            var approvedCount = allProducts.Count(p => p.IsActive && p.ApprovalStatus == ProductApprovalStatus.Approved);
            var rejectedCount = allProducts.Count(p => p.IsActive && p.ApprovalStatus == ProductApprovalStatus.Rejected);
            var deletedCount = allProducts.Count(p => !p.IsActive);

            var filteredProducts = allProducts.AsEnumerable();
            var currentFilter = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLower();

            filteredProducts = currentFilter switch
            {
                "pending" => filteredProducts.Where(p => p.IsActive && p.ApprovalStatus == ProductApprovalStatus.Pending),
                "approved" => filteredProducts.Where(p => p.IsActive && p.ApprovalStatus == ProductApprovalStatus.Approved),
                "rejected" => filteredProducts.Where(p => p.IsActive && p.ApprovalStatus == ProductApprovalStatus.Rejected),
                "hidden" or "deleted" => filteredProducts.Where(p => !p.IsActive),
                _ => filteredProducts
            };

            var model = new ProductManageViewModel
            {
                StreamerProfileId = streamer.Id,
                StreamerName = streamer.DisplayName,
                StreamerSlug = !string.IsNullOrWhiteSpace(streamer.User?.AccountId) ? streamer.User.AccountId : streamer.Slug,
                Products = filteredProducts.OrderByDescending(p => p.CreatedAt).ToList(),
                NewProduct = new ProductInputModel(),
                TotalCount = allProducts.Count,
                PendingCount = pendingCount,
                ApprovedCount = approvedCount,
                RejectedCount = rejectedCount,
                DeletedCount = deletedCount,
                StatusFilter = currentFilter
            };

            return View(model);
        }

        /// <summary>
        /// 7. STREAMER THÊM MỚI SẢN PHẨM (HỖ TRỢ CHỌN ẢNH TỪ MÁY HOẶC NHẬP URL)
        /// </summary>
        [Authorize(Roles = UserRoles.Streamer)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(45 * 1024 * 1024)]
        public async Task<IActionResult> CreateProduct(ProductInputModel model)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                return RedirectToAction("Login", "Auth");
            }

            var streamer = await GetApprovedStreamerAsync(uid);
            if (streamer == null)
            {
                TempData["ErrorMessage"] = "Chỉ Streamer đang hoạt động và đã được Admin phê duyệt mới được thêm sản phẩm.";
                return RedirectToAction("Index", "Home");
            }

            ValidateProductVariants(model);
            ValidateProductCategoryAndSizes(model);
            ValidateAdditionalImageCount(model);
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Dữ liệu sản phẩm chưa hợp lệ. Vui lòng kiểm tra lại.";
                return RedirectToAction(nameof(MyProducts));
            }

            string? finalImageUrl = model.ImageUrl?.Trim();

            // Nếu người dùng chọn tải ảnh lên từ máy tính
            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                var (uploadedPath, error) = await ProductUploadService.SaveAsync(
                    model.ImageFile, 
                    _webHostEnvironment.WebRootPath, 
                    streamer.Id, 
                    HttpContext.RequestAborted);

                if (error != null)
                {
                    TempData["ErrorMessage"] = error;
                    return RedirectToAction(nameof(MyProducts));
                }

                if (!string.IsNullOrWhiteSpace(uploadedPath))
                {
                    finalImageUrl = uploadedPath;
                }
            }

            if (string.IsNullOrWhiteSpace(finalImageUrl))
            {
                finalImageUrl = "https://images.unsplash.com/photo-1521572267360-ee0c2909d518?q=80&w=800&auto=format&fit=crop";
            }

            var (additionalImages, additionalImageError) = await SaveAdditionalImagesAsync(model.AdditionalImageFiles, streamer.Id);
            if (additionalImageError != null)
            {
                TempData["ErrorMessage"] = additionalImageError;
                return RedirectToAction(nameof(MyProducts));
            }

            var product = new ShopProduct
            {
                StreamerProfileId = streamer.Id,
                Name = model.Name.Trim(),
                Category = model.Category,
                AvailableSizes = model.Variants.Count > 0 ? [] : model.AvailableSizes,
                AdditionalImageUrls = additionalImages,
                Variants = MapVariants(model.Variants),
                Price = model.Variants.Count > 0 ? model.Variants.Min(variant => variant.Price) : model.Price,
                WeightGrams = model.WeightGrams,
                LengthCm = model.LengthCm,
                WidthCm = model.WidthCm,
                HeightCm = model.HeightCm,
                Description = model.Description?.Trim(),
                ImageUrl = finalImageUrl,
                StockQuantity = model.Variants.Count > 0 ? model.Variants.Sum(variant => variant.StockQuantity) : model.StockQuantity,
                IsActive = true,
                ApprovalStatus = ProductApprovalStatus.Pending, // Chờ duyệt
                CreatedAt = DateTime.UtcNow
            };

            _context.ShopProducts.Add(product);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Đã gửi sản phẩm \"{product.Name}\" thành công! Sản phẩm đang chờ Quản trị viên (Admin) xét duyệt trước khi hiển thị mở bán.";
            return RedirectToAction(nameof(MyProducts));
        }

        /// <summary>Hiển thị form sửa sản phẩm do chính streamer sở hữu.</summary>
        [Authorize(Roles = UserRoles.Streamer)]
        [HttpGet]
        public async Task<IActionResult> EditProduct(int id)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return RedirectToAction("Login", "Auth");

            var streamer = await GetApprovedStreamerAsync(userId);
            if (streamer == null)
            {
                TempData["ErrorMessage"] = "Chỉ Streamer đang hoạt động và đã được Admin phê duyệt mới được sửa sản phẩm.";
                return RedirectToAction("Index", "Home");
            }

            var product = await _context.ShopProducts.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.StreamerProfileId == streamer.Id && p.IsActive);
            if (product == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy sản phẩm đang hoạt động thuộc gian hàng của bạn.";
                return RedirectToAction(nameof(MyProducts));
            }

            return View(new ProductInputModel
            {
                Id = product.Id,
                Name = product.Name,
                Category = product.Category,
                AvailableSizes = product.AvailableSizes,
                Variants = product.Variants.Select(variant => new ProductVariantInputModel
                {
                    Name = variant.Name,
                    Price = variant.Price,
                    StockQuantity = variant.StockQuantity,
                    ShippingUnits = variant.ShippingUnits
                }).ToList(),
                ExistingAdditionalImageUrls = product.AdditionalImageUrls,
                Price = product.Price,
                WeightGrams = product.WeightGrams,
                LengthCm = product.LengthCm,
                WidthCm = product.WidthCm,
                HeightCm = product.HeightCm,
                Description = product.Description,
                ImageUrl = product.ImageUrl,
                StockQuantity = product.StockQuantity
            });
        }

        /// <summary>
        /// Lưu thay đổi sản phẩm. Mọi thay đổi thông tin bán hàng sẽ đưa sản phẩm về trạng thái chờ Admin duyệt.
        /// </summary>
        [Authorize(Roles = UserRoles.Streamer)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(45 * 1024 * 1024)]
        public async Task<IActionResult> EditProduct(ProductInputModel model)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return RedirectToAction("Login", "Auth");

            var streamer = await GetApprovedStreamerAsync(userId);
            if (streamer == null)
            {
                TempData["ErrorMessage"] = "Chỉ Streamer đang hoạt động và đã được Admin phê duyệt mới được sửa sản phẩm.";
                return RedirectToAction("Index", "Home");
            }

            if (model.Id is not > 0)
                return BadRequest();

            var product = await _context.ShopProducts
                .FirstOrDefaultAsync(p => p.Id == model.Id.Value && p.StreamerProfileId == streamer.Id && p.IsActive);
            if (product == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy sản phẩm đang hoạt động thuộc gian hàng của bạn.";
                return RedirectToAction(nameof(MyProducts));
            }

            ValidateProductVariants(model);
            ValidateProductCategoryAndSizes(model);
            ValidateAdditionalImageCount(model);
            var variantStructureChanged = AreVariantPricesChanged(product.Variants, model.Variants);
            if (variantStructureChanged && await _context.ShopOrderItems.AnyAsync(item =>
                    item.ShopProductId == product.Id && item.ShopOrder.StockReserved))
            {
                ModelState.AddModelError(nameof(model.Variants), "Chưa thể đổi tên, giá, số gói tính ship hoặc danh sách lựa chọn khi đang có đơn PayOS chờ thanh toán. Hãy chờ đơn hoàn tất hoặc hết hạn rồi sửa lại.");
            }
            if (!ModelState.IsValid)
            {
                model.ExistingAdditionalImageUrls = product.AdditionalImageUrls;
                return View(model);
            }

            var currentAdditionalImages = product.AdditionalImageUrls;
            var removeImages = (model.RemoveAdditionalImageUrls ?? [])
                .Where(image => !string.IsNullOrWhiteSpace(image))
                .ToHashSet(StringComparer.Ordinal);
            var retainedAdditionalImages = currentAdditionalImages
                .Where(image => !removeImages.Contains(image))
                .ToList();
            var newAdditionalImageFiles = (model.AdditionalImageFiles ?? [])
                .Where(file => file is { Length: > 0 })
                .ToList();
            if (retainedAdditionalImages.Count + newAdditionalImageFiles.Count > 4)
            {
                ModelState.AddModelError(nameof(model.AdditionalImageFiles), "Mỗi sản phẩm chỉ có tối đa 4 ảnh phụ. Hãy gỡ bớt ảnh hoặc chọn ít ảnh mới hơn.");
                model.ExistingAdditionalImageUrls = currentAdditionalImages;
                return View(model);
            }

            var finalImageUrl = model.ImageUrl?.Trim();
            if (model.ImageFile is { Length: > 0 })
            {
                var (uploadedPath, error) = await ProductUploadService.SaveAsync(
                    model.ImageFile,
                    _webHostEnvironment.WebRootPath,
                    streamer.Id,
                    HttpContext.RequestAborted);

                if (error != null)
                {
                    ModelState.AddModelError(nameof(model.ImageFile), error);
                    model.ExistingAdditionalImageUrls = currentAdditionalImages;
                    return View(model);
                }

                if (!string.IsNullOrWhiteSpace(uploadedPath))
                    finalImageUrl = uploadedPath;
            }

            if (string.IsNullOrWhiteSpace(finalImageUrl))
                finalImageUrl = product.ImageUrl;

            var (newAdditionalImages, additionalImageError) = await SaveAdditionalImagesAsync(newAdditionalImageFiles, streamer.Id);
            if (additionalImageError != null)
            {
                ModelState.AddModelError(nameof(model.AdditionalImageFiles), additionalImageError);
                model.ExistingAdditionalImageUrls = currentAdditionalImages;
                return View(model);
            }
            var updatedAdditionalImages = retainedAdditionalImages.Concat(newAdditionalImages).ToList();

            var contentChanged = !string.Equals(product.Name, model.Name.Trim(), StringComparison.Ordinal)
                || !string.Equals(product.Category, model.Category, StringComparison.Ordinal)
                || !string.Equals(JsonSerializer.Serialize(product.AvailableSizes), JsonSerializer.Serialize(model.Variants.Count > 0 ? [] : model.AvailableSizes), StringComparison.Ordinal)
                || !string.Equals(JsonSerializer.Serialize(currentAdditionalImages), JsonSerializer.Serialize(updatedAdditionalImages), StringComparison.Ordinal)
                || product.Price != (model.Variants.Count > 0 ? model.Variants.Min(variant => variant.Price) : model.Price)
                || !AreVariantPricesChanged(product.Variants, model.Variants)
                || !string.Equals(product.Description ?? string.Empty, model.Description?.Trim() ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(product.ImageUrl ?? string.Empty, finalImageUrl ?? string.Empty, StringComparison.Ordinal);
            var shippingDetailsChanged = product.WeightGrams != model.WeightGrams
                || product.LengthCm != model.LengthCm
                || product.WidthCm != model.WidthCm
                || product.HeightCm != model.HeightCm;

            product.Name = model.Name.Trim();
            product.Category = model.Category;
            product.AvailableSizes = model.Variants.Count > 0 ? [] : model.AvailableSizes;
            product.AdditionalImageUrls = updatedAdditionalImages;
            product.Variants = MapVariants(model.Variants);
            product.Price = model.Variants.Count > 0 ? model.Variants.Min(variant => variant.Price) : model.Price;
            product.StockQuantity = model.Variants.Count > 0 ? model.Variants.Sum(variant => variant.StockQuantity) : model.StockQuantity;
            product.WeightGrams = model.WeightGrams;
            product.LengthCm = model.LengthCm;
            product.WidthCm = model.WidthCm;
            product.HeightCm = model.HeightCm;
            product.Description = model.Description?.Trim();
            product.ImageUrl = finalImageUrl;
            product.UpdatedAt = DateTime.UtcNow;

            if (contentChanged)
            {
                product.ApprovalStatus = ProductApprovalStatus.Pending;
                product.RejectionReason = null;
                product.ApprovedAt = null;
                product.ApprovedBy = null;
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = contentChanged
                ? $"Đã cập nhật sản phẩm \"{product.Name}\". Thay đổi đang chờ Admin duyệt trước khi được bán lại."
                : shippingDetailsChanged
                    ? "Đã cập nhật cân nặng và kích thước kiện hàng của sản phẩm."
                    : "Thông tin sản phẩm không thay đổi.";
            return RedirectToAction(nameof(MyProducts));
        }

        /// <summary>Streamer cập nhật lại số lượng hàng còn bán được.</summary>
        [Authorize(Roles = UserRoles.Streamer)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStock(int id, int stockQuantity)
        {
            if (stockQuantity < 0 || stockQuantity > 999999)
            {
                TempData["ErrorMessage"] = "Tồn kho phải nằm trong khoảng 0 đến 999.999 sản phẩm.";
                return RedirectToAction(nameof(MyProducts));
            }

            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return RedirectToAction("Login", "Auth");

            var streamer = await GetApprovedStreamerAsync(userId);
            if (streamer == null)
            {
                TempData["ErrorMessage"] = "Chỉ Streamer đang hoạt động và đã được Admin phê duyệt mới được cập nhật tồn kho.";
                return RedirectToAction("Index", "Home");
            }

            var product = await _context.ShopProducts.FirstOrDefaultAsync(item =>
                item.Id == id && item.StreamerProfileId == streamer.Id && item.IsActive);
            if (product == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy sản phẩm thuộc gian hàng của bạn.";
            }
            else if (product.Variants.Count > 0)
            {
                TempData["ErrorMessage"] = "Sản phẩm có lựa chọn riêng. Hãy sửa tồn kho từng lựa chọn trong nút Sửa sản phẩm.";
            }
            else
            {
                product.StockQuantity = stockQuantity;
                product.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã cập nhật số lượng tồn kho.";
            }
            return RedirectToAction(nameof(MyProducts));
        }

        /// <summary>
        /// 8. STREAMER XÓA SẢN PHẨM
        /// </summary>
        [Authorize(Roles = UserRoles.Streamer)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                return RedirectToAction("Login", "Auth");
            }

            var streamer = await GetApprovedStreamerAsync(uid);
            if (streamer == null)
            {
                TempData["ErrorMessage"] = "Chỉ Streamer đang hoạt động và đã được Admin phê duyệt mới được xóa sản phẩm.";
                return RedirectToAction("Index", "Home");
            }

            var product = await _context.ShopProducts.FirstOrDefaultAsync(p => p.Id == id && p.StreamerProfileId == streamer.Id);
            if (product != null)
            {
                // Giữ bản ghi để lịch sử đơn hàng cũ tiếp tục tham chiếu được.
                product.IsActive = false;
                product.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã ẩn sản phẩm khỏi gian hàng. Bạn có thể hiện lại bất cứ lúc nào.";
            }
            else
            {
                TempData["ErrorMessage"] = "Không tìm thấy sản phẩm thuộc gian hàng của bạn.";
            }

            return RedirectToAction(nameof(MyProducts));
        }

        /// <summary>Hiện lại sản phẩm đã ẩn mà không xóa dữ liệu hoặc lịch sử đơn hàng.</summary>
        [Authorize(Roles = UserRoles.Streamer)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreProduct(int id)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return RedirectToAction("Login", "Auth");

            var streamer = await GetApprovedStreamerAsync(userId);
            if (streamer == null)
            {
                TempData["ErrorMessage"] = "Chỉ Streamer đang hoạt động và đã được Admin phê duyệt mới được quản lý sản phẩm.";
                return RedirectToAction("Index", "Home");
            }

            var product = await _context.ShopProducts.FirstOrDefaultAsync(p => p.Id == id && p.StreamerProfileId == streamer.Id);
            if (product == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy sản phẩm thuộc gian hàng của bạn.";
                return RedirectToAction(nameof(MyProducts));
            }

            product.IsActive = true;
            product.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = product.ApprovalStatus switch
            {
                ProductApprovalStatus.Approved => "Đã hiện lại sản phẩm trong gian hàng.",
                ProductApprovalStatus.Pending => "Đã hiện lại sản phẩm trong danh sách quản lý; sản phẩm vẫn chờ Admin duyệt trước khi được bán.",
                _ => "Đã khôi phục sản phẩm trong danh sách quản lý. Sản phẩm đang bị từ chối duyệt nên chưa xuất hiện ở gian hàng; hãy sửa và gửi Admin duyệt lại."
            };
            return RedirectToAction(nameof(MyProducts));
        }

        private void ValidateProductCategoryAndSizes(ProductInputModel model)
        {
            if (!ShopProductCategories.IsValid(model.Category))
            {
                ModelState.AddModelError(nameof(model.Category), "Vui lòng chọn một danh mục trong danh sách.");
                return;
            }

            var supportedSizes = ShopProductCategories.SizesFor(model.Category);
            var requestedSizes = (model.AvailableSizes ?? [])
                .Where(size => !string.IsNullOrWhiteSpace(size))
                .Select(size => size.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (supportedSizes.Count == 0)
            {
                if (requestedSizes.Count > 0)
                    ModelState.AddModelError(nameof(model.AvailableSizes), "Danh mục này không hỗ trợ lựa chọn size.");
                model.AvailableSizes = [];
                return;
            }

            // Nếu streamer cấu hình bảng lựa chọn có giá và tồn kho riêng, size được nhập
            // như từng lựa chọn ở bảng đó (ví dụ S, M, L), không dùng danh sách size cũ.
            if (model.Variants?.Count > 0)
            {
                if (requestedSizes.Count > 0)
                    ModelState.AddModelError(nameof(model.AvailableSizes), "Hãy dùng bảng lựa chọn bên dưới để khai báo size và tồn kho riêng; không chọn đồng thời danh sách size cũ.");
                model.AvailableSizes = [];
                return;
            }

            var canonicalSizes = requestedSizes
                .Select(requested => supportedSizes.FirstOrDefault(size => string.Equals(size, requested, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (canonicalSizes.Any(size => size == null))
                ModelState.AddModelError(nameof(model.AvailableSizes), "Một hoặc nhiều size không thuộc danh mục đã chọn.");
            if (canonicalSizes.Count == 0)
                ModelState.AddModelError(nameof(model.AvailableSizes), "Hãy chọn ít nhất một size còn hàng.");

            model.AvailableSizes = canonicalSizes.Where(size => size != null).Cast<string>().Distinct(StringComparer.Ordinal).ToList();
        }

        private void ValidateProductVariants(ProductInputModel model)
        {
            model.Variants ??= [];
            model.Variants = model.Variants
                .Where(variant => variant != null)
                .Select(variant => new ProductVariantInputModel
                {
                    Name = variant.Name?.Trim() ?? string.Empty,
                    Price = variant.Price,
                    StockQuantity = variant.StockQuantity
                })
                .Where(variant => !string.IsNullOrWhiteSpace(variant.Name))
                .ToList();

            if (model.Variants.Count == 0 && (model.Price < 1000 || model.Price > 1_000_000_000))
                ModelState.AddModelError(nameof(model.Price), "Giá sản phẩm từ 1.000 đến 1.000.000.000 VNĐ.");

            if (model.Variants.Count > 50)
            {
                ModelState.AddModelError(nameof(model.Variants), "Mỗi sản phẩm có tối đa 50 lựa chọn.");
                return;
            }

            var duplicateName = model.Variants
                .GroupBy(variant => variant.Name, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1);
            if (duplicateName)
                ModelState.AddModelError(nameof(model.Variants), "Tên các lựa chọn không được trùng nhau.");

            foreach (var variant in model.Variants)
            {
                if (variant.Name.Length > 50)
                    ModelState.AddModelError(nameof(model.Variants), "Tên lựa chọn tối đa 50 ký tự.");
                if (variant.Price < 1000 || variant.Price > 1_000_000_000)
                    ModelState.AddModelError(nameof(model.Variants), $"Giá của lựa chọn \"{variant.Name}\" phải từ 1.000 đến 1.000.000.000 VNĐ.");
                if (variant.StockQuantity < 0 || variant.StockQuantity > 999999)
                    ModelState.AddModelError(nameof(model.Variants), $"Tồn kho của lựa chọn \"{variant.Name}\" phải từ 0 đến 999.999.");
                if (variant.ShippingUnits < 1 || variant.ShippingUnits > 100)
                    ModelState.AddModelError(nameof(model.Variants), $"Số lượng gói tính vận chuyển của lựa chọn \"{variant.Name}\" phải từ 1 đến 100.");
            }

            if (model.Variants.Sum(variant => (long)variant.StockQuantity) > 999999)
                ModelState.AddModelError(nameof(model.Variants), "Tổng tồn kho các lựa chọn không được vượt quá 999.999.");
        }

        private static List<ShopProductVariant> MapVariants(IEnumerable<ProductVariantInputModel> variants) => variants
            .Select(variant => new ShopProductVariant
            {
                Name = variant.Name.Trim(),
                Price = variant.Price,
                StockQuantity = variant.StockQuantity,
                ShippingUnits = variant.ShippingUnits
            })
            .ToList();

        private static bool AreVariantPricesChanged(
            IReadOnlyCollection<ShopProductVariant> existing,
            IReadOnlyCollection<ProductVariantInputModel> updated)
        {
            var current = existing.OrderBy(variant => variant.Name, StringComparer.OrdinalIgnoreCase)
                .Select(variant => (Name: variant.Name.Trim(), variant.Price, variant.ShippingUnits)).ToList();
            var next = updated.OrderBy(variant => variant.Name, StringComparer.OrdinalIgnoreCase)
                .Select(variant => (Name: variant.Name.Trim(), variant.Price, variant.ShippingUnits)).ToList();
            return !current.SequenceEqual(next);
        }

        private void ValidateAdditionalImageCount(ProductInputModel model)
        {
            if ((model.AdditionalImageFiles?.Count(file => file is { Length: > 0 }) ?? 0) > 4)
                ModelState.AddModelError(nameof(model.AdditionalImageFiles), "Mỗi sản phẩm chỉ được tải tối đa 4 ảnh phụ cùng lúc.");
        }

        private async Task<(List<string> Paths, string? Error)> SaveAdditionalImagesAsync(IEnumerable<Microsoft.AspNetCore.Http.IFormFile>? files, int streamerId)
        {
            var paths = new List<string>();
            var selectedFiles = (files ?? []).Where(file => file is { Length: > 0 }).ToList();
            if (selectedFiles.Count > 4)
                return (paths, "Mỗi sản phẩm chỉ được tải tối đa 4 ảnh phụ cùng lúc.");

            foreach (var file in selectedFiles)
            {
                var (path, error) = await ProductUploadService.SaveAsync(
                    file,
                    _webHostEnvironment.WebRootPath,
                    streamerId,
                    HttpContext.RequestAborted);
                if (error != null)
                    return (paths, error);
                if (!string.IsNullOrWhiteSpace(path))
                    paths.Add(path);
            }

            return (paths, null);
        }

        private static string? ValidateCartSizes(IEnumerable<CartItemInputModel> items, IReadOnlyDictionary<int, ShopProduct> products)
        {
            foreach (var item in items)
            {
                if (!products.TryGetValue(item.ProductId, out var product))
                    return "Một hoặc nhiều sản phẩm đã ngừng bán. Hãy tải lại gian hàng.";

                var productVariants = product.Variants;
                if (productVariants.Count > 0)
                {
                    if (string.IsNullOrWhiteSpace(item.Size))
                        return $"Vui lòng chọn lựa chọn cho sản phẩm \"{product.Name}\".";

                    var selectedVariant = productVariants.FirstOrDefault(variant =>
                        string.Equals(variant.Name, item.Size.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (selectedVariant == null)
                        return $"Lựa chọn \"{item.Size}\" của sản phẩm \"{product.Name}\" không còn bán. Hãy tải lại gian hàng.";

                    item.Size = selectedVariant.Name;
                    continue;
                }

                var availableSizes = product.AvailableSizes;
                if (availableSizes.Count == 0)
                {
                    if (!string.IsNullOrWhiteSpace(item.Size))
                        return $"Sản phẩm \"{product.Name}\" không có lựa chọn này.";
                    item.Size = null;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.Size))
                    return $"Vui lòng chọn size cho sản phẩm \"{product.Name}\".";

                var selectedSize = availableSizes.FirstOrDefault(size =>
                    string.Equals(size, item.Size.Trim(), StringComparison.OrdinalIgnoreCase));
                if (selectedSize == null)
                    return $"Size \"{item.Size}\" của sản phẩm \"{product.Name}\" hiện không còn bán. Hãy tải lại gian hàng.";

                item.Size = selectedSize;
            }

            return null;
        }

        private static decimal GetPriceForSelection(ShopProduct product, string? selection)
        {
            if (product.Variants.Count == 0 || string.IsNullOrWhiteSpace(selection))
                return product.Price;

            return product.Variants.FirstOrDefault(variant =>
                string.Equals(variant.Name, selection, StringComparison.OrdinalIgnoreCase))?.Price ?? product.Price;
        }

        private static int GetAvailableStock(ShopProduct product, string? selection)
        {
            if (product.Variants.Count == 0 || string.IsNullOrWhiteSpace(selection))
                return product.StockQuantity;

            return product.Variants.FirstOrDefault(variant =>
                string.Equals(variant.Name, selection, StringComparison.OrdinalIgnoreCase))?.StockQuantity ?? 0;
        }

        private static int GetShippingUnits(ShopProduct product, string? selection)
        {
            if (product.Variants.Count == 0 || string.IsNullOrWhiteSpace(selection))
                return 1;

            return product.Variants.FirstOrDefault(variant =>
                string.Equals(variant.Name, selection, StringComparison.OrdinalIgnoreCase))?.ShippingUnits ?? 1;
        }

        private static string FormatSelection(ShopProduct product, string? selection) =>
            string.IsNullOrWhiteSpace(selection)
                ? "mặc định"
                : product.Variants.Count > 0 ? selection : $"size {selection}";

        private static CartItemInputModel? FindStockShortage(
            IEnumerable<CartItemInputModel> items,
            IReadOnlyDictionary<int, ShopProduct> products)
        {
            foreach (var group in items.GroupBy(item => item.ProductId))
            {
                if (!products.TryGetValue(group.Key, out var product))
                    return group.First();

                if (product.Variants.Count > 0)
                {
                    var shortOption = group.FirstOrDefault(item => GetAvailableStock(product, item.Size) < item.Quantity);
                    if (shortOption != null)
                        return shortOption;
                }
                else if (product.StockQuantity < group.Sum(item => item.Quantity))
                {
                    return group.First();
                }
            }

            return null;
        }

        private static List<CartItemInputModel> GroupCartItems(IEnumerable<CartItemInputModel> items) => items
            .GroupBy(item => new { item.ProductId, Size = item.Size?.Trim() ?? string.Empty })
            .Select(group => new CartItemInputModel
            {
                ProductId = group.Key.ProductId,
                Size = string.IsNullOrWhiteSpace(group.Key.Size) ? null : group.Key.Size,
                Quantity = (int)Math.Min(group.Sum(item => (long)item.Quantity), int.MaxValue)
            })
            .ToList();

        private Task<StreamerProfile?> GetApprovedStreamerAsync(int userId) =>
            _context.StreamerProfiles.FirstOrDefaultAsync(profile =>
                profile.UserId == userId
                && profile.IsActive
                && profile.ApprovalStatus == StreamerApprovalStatus.Approved);

        /// <summary>
        /// Trừ kho bằng câu UPDATE có điều kiện trong transaction. Nếu lượng hàng vừa bị người
        /// khác mua hết thì trả lỗi; đơn và số dư ví sẽ được rollback cùng transaction.
        /// </summary>
        private async Task<string?> ReserveInventoryAsync(int streamerId, IEnumerable<ShopOrderItem> items)
        {
            foreach (var item in items.OrderBy(item => item.ShopProductId).ThenBy(item => item.VariantSelection))
            {
                int reserved;
                if (item.UsesVariantStock && !string.IsNullOrWhiteSpace(item.VariantSelection))
                {
                    // Cập nhật tồn lựa chọn và tổng tồn trên cùng một UPDATE có điều kiện,
                    // tránh hai khách cùng mua vượt quá kho của cùng một lựa chọn.
                    reserved = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                        UPDATE product
                        SET VariantsJson = JSON_MODIFY(
                                product.VariantsJson,
                                CONCAT('$[', optionRow.[key], '].StockQuantity'),
                                TRY_CONVERT(int, JSON_VALUE(optionRow.[value], '$.StockQuantity')) - {item.Quantity}),
                            StockQuantity = product.StockQuantity - {item.Quantity}
                        FROM dbo.ShopProducts AS product
                        CROSS APPLY OPENJSON(product.VariantsJson) AS optionRow
                        WHERE product.Id = {item.ShopProductId}
                          AND product.StreamerProfileId = {streamerId}
                          AND product.IsActive = 1
                          AND product.ApprovalStatus = {(int)ProductApprovalStatus.Approved}
                          AND product.StockQuantity >= {item.Quantity}
                          AND JSON_VALUE(optionRow.[value], '$.Name') = {item.VariantSelection}
                          AND TRY_CONVERT(int, JSON_VALUE(optionRow.[value], '$.StockQuantity')) >= {item.Quantity}
                          AND EXISTS (
                              SELECT 1 FROM dbo.StreamerProfiles AS streamer
                              WHERE streamer.Id = product.StreamerProfileId
                                AND streamer.IsActive = 1
                                AND streamer.ApprovalStatus = {(int)StreamerApprovalStatus.Approved}
                          );");
                }
                else
                {
                    reserved = await _context.ShopProducts
                        .Where(product => product.Id == item.ShopProductId
                            && product.StreamerProfileId == streamerId
                            && product.IsActive
                            && product.ApprovalStatus == ProductApprovalStatus.Approved
                            && product.StreamerProfile.IsActive
                            && product.StreamerProfile.ApprovalStatus == StreamerApprovalStatus.Approved
                            && product.StockQuantity >= item.Quantity)
                        .ExecuteUpdateAsync(update => update.SetProperty(
                            product => product.StockQuantity,
                            product => product.StockQuantity - item.Quantity));
                }

                if (reserved != 1)
                {
                    return $"Sản phẩm \"{item.ProductName}\" vừa hết hàng hoặc không còn đủ số lượng. Vui lòng tải lại giỏ hàng.";
                }
            }

            return null;
        }

        /// <summary>Hoàn các mặt hàng đang được giữ cho đơn PayOS bị hủy hoặc hết hạn.</summary>
        private async Task ReleaseReservedStockAsync(ShopOrder order)
        {
            if (!order.StockReserved)
                return;

            foreach (var item in order.Items.OrderBy(item => item.ShopProductId))
            {
                if (item.UsesVariantStock && !string.IsNullOrWhiteSpace(item.VariantSelection))
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync($@"
                        UPDATE product
                        SET VariantsJson = JSON_MODIFY(
                                product.VariantsJson,
                                CONCAT('$[', optionRow.[key], '].StockQuantity'),
                                TRY_CONVERT(int, JSON_VALUE(optionRow.[value], '$.StockQuantity')) + {item.Quantity}),
                            StockQuantity = product.StockQuantity + {item.Quantity}
                        FROM dbo.ShopProducts AS product
                        CROSS APPLY OPENJSON(product.VariantsJson) AS optionRow
                        WHERE product.Id = {item.ShopProductId}
                          AND JSON_VALUE(optionRow.[value], '$.Name') = {item.VariantSelection};");
                }
                else
                {
                    await _context.ShopProducts
                        .Where(product => product.Id == item.ShopProductId)
                        .ExecuteUpdateAsync(update => update.SetProperty(
                            product => product.StockQuantity,
                            product => product.StockQuantity + item.Quantity));
                }
            }

            order.StockReserved = false;
        }

        /// <summary>
        /// PayOS links hết hạn sau 10 phút. Chờ thêm 20 phút để webhook có thời gian đến rồi
        /// hoàn kho cho các đơn chưa thanh toán, tối đa 50 đơn mỗi lần gọi.
        /// </summary>
        private async Task ExpireStaleStockReservationsAsync()
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-30);
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var staleOrders = await _context.ShopOrders
                .Include(order => order.Items)
                .Where(order => order.PaymentMethod == "PayOS"
                    && order.PaymentStatus == "Pending"
                    && order.StockReserved
                    && order.CreatedAt <= cutoff)
                .OrderBy(order => order.CreatedAt)
                .Take(50)
                .ToListAsync();

            foreach (var order in staleOrders)
            {
                await ReleaseReservedStockAsync(order);
                order.PaymentStatus = "Cancelled";
            }

            if (staleOrders.Count > 0)
                await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        /// <summary>PayOS đưa khách về URL này khi họ chủ động hủy thanh toán.</summary>
        private async Task CancelPendingPayOsOrderAsync(string orderCode)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var order = await _context.ShopOrders
                .Include(item => item.Items)
                .FirstOrDefaultAsync(item => item.OrderCode == orderCode && item.PaymentMethod == "PayOS");

            if (order?.PaymentStatus == "Pending")
            {
                await ReleaseReservedStockAsync(order);
                order.PaymentStatus = "Cancelled";
                await _context.SaveChangesAsync();
            }

            await transaction.CommitAsync();
        }

        /// <summary>Mã hóa QR payload PayOS trả về thành ảnh PNG để trình duyệt hiển thị.</summary>
        private async Task<long> CreateUniquePayOSOrderCodeAsync()
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                // PayOS yêu cầu mã số. Mã ngẫu nhiên khó đoán cũng giúp bảo vệ URL tra cứu đơn khách vãng lai.
                var randomBytes = RandomNumberGenerator.GetBytes(sizeof(long));
                var randomValue = BitConverter.ToInt64(randomBytes) & long.MaxValue;
                const long codeRange = 8_000_000_000_000_000L;
                var candidate = 1_000_000_000_000_000L + randomValue % codeRange;
                var candidateText = candidate.ToString(CultureInfo.InvariantCulture);
                var exists = await _context.ShopOrders.AnyAsync(o => o.OrderCode == candidateText)
                    || await _context.WalletTransactions.AnyAsync(t => t.OrderCode == candidate)
                    || await _context.Donations.AnyAsync(d =>
                        d.TransactionCode == $"DON_{candidate}" || d.TransactionCode == $"PAYOS_{candidate}");

                if (!exists)
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("Không thể tạo mã PayOS duy nhất cho đơn hàng.");
        }

        private static string GenerateQrCodeBase64(string qrPayload)
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(qrPayload, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            byte[] qrCodeBytes = qrCode.GetGraphic(10);

            return $"data:image/png;base64,{Convert.ToBase64String(qrCodeBytes)}";
        }
    }
}
