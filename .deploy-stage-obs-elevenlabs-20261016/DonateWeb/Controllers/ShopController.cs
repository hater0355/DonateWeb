using System;
using System.Data;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Claims;
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

        public ShopController(
            AppDbContext context,
            IWebHostEnvironment webHostEnvironment,
            IPayOSService? payOS = null,
            ILogger<ShopController>? logger = null)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
            _payOS = payOS;
            _logger = logger;
        }

        /// <summary>
        /// 1. SÀN SẢN PHẨM / SHOP CHUNG CỦA TẤT CẢ CÁC STREAMER
        /// Hiển thị toàn bộ sản phẩm của các streamer đã đưa lên sàn, có ghi rõ từ streamer nào và thanh tìm kiếm
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index(string? keyword, string? streamerSlug, string? sortBy)
        {
            var query = _context.ShopProducts
                .Include(p => p.StreamerProfile)
                    .ThenInclude(sp => sp.User)
                .Where(p => p.IsActive && p.ApprovalStatus == ProductApprovalStatus.Approved && p.StreamerProfile.IsActive)
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
                Price = p.Price,
                ImageUrl = string.IsNullOrWhiteSpace(p.ImageUrl) ? "/images/default-banner.jpg" : p.ImageUrl,
                StockQuantity = p.StockQuantity,
                StreamerProfileId = p.StreamerProfileId,
                StreamerName = p.StreamerProfile.DisplayName,
                StreamerSlug = !string.IsNullOrWhiteSpace(p.StreamerProfile.User != null ? p.StreamerProfile.User.AccountId : null) ? p.StreamerProfile.User!.AccountId! : p.StreamerProfile.Slug,
                StreamerAvatar = string.IsNullOrWhiteSpace(p.StreamerProfile.AvatarUrl) ? "/images/default-avatar.png" : p.StreamerProfile.AvatarUrl,
                StreamerRank = p.StreamerProfile.Rank,
                IsVerified = p.StreamerProfile.IsVerified
            }).ToListAsync();

            // Danh sách streamer để hiển thị bộ lọc
            var activeStreamers = await _context.StreamerProfiles
                .Where(s => s.IsActive && s.ShopProducts.Any(p => p.IsActive && p.ApprovalStatus == ProductApprovalStatus.Approved))
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
                .FirstOrDefaultAsync(s => s.Slug == cleanSlug || (s.User != null && s.User.AccountId == cleanSlug));

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
                ViewerPhone = viewerPhone
            };

            return View(viewModel);
        }

        /// <summary>
        /// 3. XỬ LÝ ĐẶT HÀNG & THANH TOÁN (HỖ TRỢ THANH TOÁN BẰNG VÍ HOẶC QUÉT MÃ VIETQR)
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout([FromBody] CheckoutRequestViewModel model)
        {
            await ExpireStaleStockReservationsAsync();

            if (model == null || model.Items == null || !model.Items.Any())
            {
                return Json(new { success = false, message = "Giỏ hàng của bạn đang trống." });
            }

            if (model.Items.Count > 100 || model.Items.Any(item => item.ProductId <= 0 || item.Quantity <= 0))
            {
                return Json(new { success = false, message = "Sản phẩm hoặc số lượng trong giỏ hàng không hợp lệ." });
            }

            // Gộp sản phẩm trùng nhau để không thể vượt kho bằng cách gửi nhiều dòng cho cùng mã hàng.
            var groupedItems = model.Items
                .GroupBy(item => item.ProductId)
                .Select(group => new CartItemInputModel
                {
                    ProductId = group.Key,
                    Quantity = (int)Math.Min(group.Sum(item => (long)item.Quantity), int.MaxValue)
                })
                .ToList();
            if (groupedItems.Any(item => item.Quantity > 9999))
            {
                return Json(new { success = false, message = "Mỗi sản phẩm chỉ được mua tối đa 9.999 chiếc trong một đơn." });
            }
            model.Items = groupedItems;

            var streamer = await _context.StreamerProfiles
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.Id == model.StreamerProfileId);

            if (streamer == null)
            {
                return Json(new { success = false, message = "Không tìm thấy thông tin Streamer bán hàng." });
            }

            // Lấy danh sách sản phẩm hợp lệ trong database
            var productIds = model.Items.Select(i => i.ProductId).Distinct().ToList();
            var products = await _context.ShopProducts
                .Where(p => productIds.Contains(p.Id) && p.StreamerProfileId == streamer.Id && p.IsActive && p.ApprovalStatus == ProductApprovalStatus.Approved)
                .ToListAsync();

            if (!products.Any() || products.Count != productIds.Count)
            {
                return Json(new { success = false, message = "Các sản phẩm trong giỏ không hợp lệ hoặc đã ngừng bán." });
            }

            decimal subtotalAmount = 0;
            var orderItems = new List<ShopOrderItem>();

            foreach (var item in model.Items)
            {
                var prod = products.FirstOrDefault(p => p.Id == item.ProductId);
                if (prod != null && item.Quantity > 0)
                {
                    subtotalAmount += prod.Price * item.Quantity;
                    orderItems.Add(new ShopOrderItem
                    {
                        ShopProductId = prod.Id,
                        ProductName = prod.Name,
                        Price = prod.Price,
                        Quantity = item.Quantity
                    });
                }
            }

            var insufficientStock = orderItems.FirstOrDefault(item =>
                products.First(product => product.Id == item.ShopProductId).StockQuantity < item.Quantity);
            if (insufficientStock != null)
            {
                var available = products.First(product => product.Id == insufficientStock.ShopProductId).StockQuantity;
                return Json(new
                {
                    success = false,
                    message = $"Sản phẩm \"{insufficientStock.ProductName}\" chỉ còn {available} trong kho. Vui lòng cập nhật giỏ hàng."
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
            var totalAmount = subtotalAmount - discountAmount;

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
                    StreamerProfileId = streamer.Id,
                    BuyerUserId = currentUserId,
                    BuyerName = model.BuyerName.Trim(),
                    BuyerPhone = model.BuyerPhone?.Trim(),
                    BuyerAddress = model.BuyerAddress?.Trim(),
                    Note = model.Note?.Trim(),
                    SubtotalAmount = subtotalAmount,
                    DiscountAmount = discountAmount,
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
                if (streamerUser != null)
                {
                    await _context.Users
                        .Where(user => user.Id == streamerUser.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(
                            user => user.WalletBalance,
                            user => user.WalletBalance + totalAmount));
                    await _context.Entry(streamerUser).ReloadAsync();
                    streamerBalanceAfter = streamerUser.WalletBalance;
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
                    Note = $"Thanh toán đơn hàng #{orderCode} tại Shop của {streamer.DisplayName}",
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
                        Amount = totalAmount,
                        BalanceBefore = streamerBalanceAfter!.Value - totalAmount,
                        BalanceAfter = streamerBalanceAfter.Value,
                        PaymentMethodName = "Ví số dư OnlyFan",
                        Status = WalletTransaction.StatusCompleted,
                        Note = $"Doanh thu bán hàng đơn #{orderCode} từ khách {model.BuyerName.Trim()}",
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
                    BuyerAddress = model.BuyerAddress?.Trim(),
                    Note = model.Note?.Trim(),
                    SubtotalAmount = subtotalAmount,
                    DiscountAmount = discountAmount,
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
                StreamerProfileId = streamer.Id,
                BuyerUserId = currentUserId,
                BuyerName = model.BuyerName.Trim(),
                BuyerPhone = model.BuyerPhone?.Trim(),
                BuyerAddress = model.BuyerAddress?.Trim(),
                Note = model.Note?.Trim(),
                SubtotalAmount = subtotalAmount,
                DiscountAmount = discountAmount,
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

            if (model.Items.Count > 100 || model.Items.Any(item => item.ProductId <= 0 || item.Quantity <= 0))
            {
                return Json(new { success = false, message = "Sản phẩm hoặc số lượng trong giỏ hàng không hợp lệ." });
            }

            var groupedItems = model.Items
                .GroupBy(item => item.ProductId)
                .Select(group => new CartItemInputModel
                {
                    ProductId = group.Key,
                    Quantity = (int)Math.Min(group.Sum(item => (long)item.Quantity), int.MaxValue)
                })
                .ToList();
            if (groupedItems.Any(item => item.Quantity > 9999))
            {
                return Json(new { success = false, message = "Mỗi sản phẩm chỉ được mua tối đa 9.999 chiếc trong một đơn." });
            }
            model.Items = groupedItems;

            var productIds = model.Items.Select(item => item.ProductId).Distinct().ToList();
            var products = await _context.ShopProducts
                .Where(product => productIds.Contains(product.Id)
                    && product.StreamerProfileId == model.StreamerProfileId
                    && product.IsActive
                    && product.ApprovalStatus == ProductApprovalStatus.Approved)
                .ToDictionaryAsync(product => product.Id);

            if (products.Count != productIds.Count)
            {
                return Json(new { success = false, message = "Một hoặc nhiều sản phẩm đã ngừng bán. Hãy tải lại gian hàng." });
            }

            var outOfStock = model.Items.FirstOrDefault(item => products[item.ProductId].StockQuantity < item.Quantity);
            if (outOfStock != null)
            {
                return Json(new { success = false, message = $"Sản phẩm \"{products[outOfStock.ProductId].Name}\" không còn đủ hàng trong kho." });
            }

            var subtotalAmount = model.Items.Sum(item => products[item.ProductId].Price * item.Quantity);
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
            await ExpireStaleStockReservationsAsync();

            if (string.IsNullOrWhiteSpace(orderCode))
            {
                return BadRequest(new { success = false, message = "Thiếu mã đơn hàng." });
            }

            var order = await _context.ShopOrders
                .AsNoTracking()
                .Where(o => o.OrderCode == orderCode.Trim() && o.PaymentMethod == "PayOS")
                .Select(o => new { o.OrderCode, o.PaymentStatus, o.PaidAt })
                .FirstOrDefaultAsync();

            if (order == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy đơn hàng PayOS." });
            }

            return Json(new
            {
                success = true,
                orderCode = order.OrderCode,
                status = order.PaymentStatus,
                isPaid = order.PaymentStatus == "Paid",
                paidAt = order.PaidAt
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

            if (string.Equals(payment, "cancelled", StringComparison.OrdinalIgnoreCase))
            {
                await CancelPendingPayOsOrderAsync(orderCode.Trim());
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

            return View(order);
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
                .Include(s => s.ShopProducts)
                .FirstOrDefaultAsync(s => s.UserId == uid);

            if (streamer == null)
            {
                TempData["ErrorMessage"] = "Bạn chưa có hồ sơ Streamer.";
                return RedirectToAction("Register", "Streamer");
            }

            var allProducts = streamer.ShopProducts.ToList();
            var pendingCount = allProducts.Count(p => p.ApprovalStatus == ProductApprovalStatus.Pending);
            var approvedCount = allProducts.Count(p => p.ApprovalStatus == ProductApprovalStatus.Approved);
            var rejectedCount = allProducts.Count(p => p.ApprovalStatus == ProductApprovalStatus.Rejected);

            var filteredProducts = allProducts.AsEnumerable();
            var currentFilter = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLower();

            filteredProducts = currentFilter switch
            {
                "pending" => filteredProducts.Where(p => p.ApprovalStatus == ProductApprovalStatus.Pending),
                "approved" => filteredProducts.Where(p => p.ApprovalStatus == ProductApprovalStatus.Approved),
                "rejected" => filteredProducts.Where(p => p.ApprovalStatus == ProductApprovalStatus.Rejected),
                _ => filteredProducts
            };

            var model = new ProductManageViewModel
            {
                StreamerProfileId = streamer.Id,
                StreamerName = streamer.DisplayName,
                Products = filteredProducts.OrderByDescending(p => p.CreatedAt).ToList(),
                NewProduct = new ProductInputModel(),
                PendingCount = pendingCount,
                ApprovedCount = approvedCount,
                RejectedCount = rejectedCount,
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
        public async Task<IActionResult> CreateProduct(ProductInputModel model)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            {
                return RedirectToAction("Login", "Auth");
            }

            var streamer = await _context.StreamerProfiles.FirstOrDefaultAsync(s => s.UserId == uid);
            if (streamer == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy hồ sơ Streamer.";
                return RedirectToAction("Register", "Streamer");
            }

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

            var product = new ShopProduct
            {
                StreamerProfileId = streamer.Id,
                Name = model.Name.Trim(),
                Price = model.Price,
                Description = model.Description?.Trim(),
                ImageUrl = finalImageUrl,
                StockQuantity = model.StockQuantity,
                IsActive = true,
                ApprovalStatus = ProductApprovalStatus.Pending, // Chờ duyệt
                CreatedAt = DateTime.UtcNow
            };

            _context.ShopProducts.Add(product);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Đã gửi sản phẩm \"{product.Name}\" thành công! Sản phẩm đang chờ Quản trị viên (Admin) xét duyệt trước khi hiển thị mở bán.";
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

            var streamerId = await _context.StreamerProfiles
                .Where(profile => profile.UserId == userId)
                .Select(profile => (int?)profile.Id)
                .FirstOrDefaultAsync();
            if (streamerId == null)
                return RedirectToAction("Register", "Streamer");

            var updated = await _context.ShopProducts
                .Where(product => product.Id == id && product.StreamerProfileId == streamerId.Value)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(product => product.StockQuantity, stockQuantity)
                    .SetProperty(product => product.UpdatedAt, DateTime.UtcNow));

            TempData[updated == 1 ? "SuccessMessage" : "ErrorMessage"] = updated == 1
                ? "Đã cập nhật số lượng tồn kho."
                : "Không tìm thấy sản phẩm thuộc gian hàng của bạn.";
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

            var streamer = await _context.StreamerProfiles.FirstOrDefaultAsync(s => s.UserId == uid);
            if (streamer == null) return RedirectToAction("Login", "Auth");

            var product = await _context.ShopProducts.FirstOrDefaultAsync(p => p.Id == id && p.StreamerProfileId == streamer.Id);
            if (product != null)
            {
                _context.ShopProducts.Remove(product);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã xóa sản phẩm khỏi gian hàng.";
            }

            return RedirectToAction(nameof(MyProducts));
        }

        /// <summary>
        /// Trừ kho bằng câu UPDATE có điều kiện trong transaction. Nếu lượng hàng vừa bị người
        /// khác mua hết thì trả lỗi; đơn và số dư ví sẽ được rollback cùng transaction.
        /// </summary>
        private async Task<string?> ReserveInventoryAsync(int streamerId, IEnumerable<ShopOrderItem> items)
        {
            foreach (var item in items.OrderBy(item => item.ShopProductId))
            {
                var reserved = await _context.ShopProducts
                    .Where(product => product.Id == item.ShopProductId
                        && product.StreamerProfileId == streamerId
                        && product.IsActive
                        && product.ApprovalStatus == ProductApprovalStatus.Approved
                        && product.StockQuantity >= item.Quantity)
                    .ExecuteUpdateAsync(update => update.SetProperty(
                        product => product.StockQuantity,
                        product => product.StockQuantity - item.Quantity));

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
                await _context.ShopProducts
                    .Where(product => product.Id == item.ShopProductId)
                    .ExecuteUpdateAsync(update => update.SetProperty(
                        product => product.StockQuantity,
                        product => product.StockQuantity + item.Quantity));
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
