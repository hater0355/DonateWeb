using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using DonateWeb.Security.Configuration;

namespace DonateWeb.Security.ContentModeration
{
    /// <summary>
    /// SERVICE KIỂM DUYỆT LỜI NHẮN & LỌC SPAM LINK ĐỘC HẠI CHO STREAMER
    /// Lưu trữ trong thư mục riêng: Security/ContentModeration/
    /// Tính năng:
    /// - Quét từ ngữ thô tục, phân biệt chủng tộc, xúc phạm danh dự
    /// - Quét liên kết ngoài, link rút gọn, domain cờ bạc lậu, lừa đảo
    /// - Khử thủ thuật lách luật (leetspeak, dấu phân cách xen kẽ: đ.m, v.c.l, c*ặ*c)
    /// - Làm sạch văn bản (Sanitize) trước khi xuất hiện trên OBS Alert Box
    /// </summary>
    public class ContentModerationService : IContentModerationService
    {
        private readonly ContentModerationSettings _settings;
        private readonly ILogger<ContentModerationService> _logger;

        // Biểu thức chính quy phát hiện URL / Liên kết
        private static readonly Regex UrlRegex = new(
            ModerationKeywords.GeneralUrlRegexPattern,
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Biểu thức phát hiện ký tự gây nhiễu cố tình lách luật (chấm, gạch, sao xen giữa chữ cái)
        private static readonly Regex PunctuationNoiseRegex = new(
            @"[-.,_*#@!?$/\\\s]+",
            RegexOptions.Compiled);

        public ContentModerationService(
            IOptions<SecuritySettings> securityOptions,
            ILogger<ContentModerationService> logger)
        {
            _settings = securityOptions.Value.ContentModeration;
            _logger = logger;
        }

        /// <summary>
        /// Phân tích toàn diện lời nhắn quyên góp và tên người ủng hộ
        /// </summary>
        public ModerationResult ModerateContent(string? message, string? donorName = null)
        {
            var result = new ModerationResult
            {
                OriginalText = message ?? string.Empty,
                SanitizedText = message ?? string.Empty
            };

            // Nếu tính năng kiểm duyệt bị tắt trong cấu hình
            if (!_settings.IsEnabled || string.IsNullOrWhiteSpace(message))
            {
                return result;
            }

            var workingText = message;

            // BƯỚC 1: QUÉT VÀ XỬ LÝ LIÊN KẾT NGOÀI & SPAM LINK ĐỘC HẠI
            workingText = ScanAndSanitizeLinks(workingText, result);

            // BƯỚC 2: QUÉT VÀ LỌC CÁC NHÓM TỪ KHÓA VI PHẠM (THÔ TỤC, PHÂN BIỆT CHỦNG TỘC, XÚC PHẠM)
            workingText = ScanAndSanitizeKeywords(workingText, result);

            // BƯỚC 3: KIỂM TRA TÊN NGƯỜI ỦNG HỘ (DONOR NAME) NẾU CÓ
            if (!string.IsNullOrWhiteSpace(donorName))
            {
                ScanDonorName(donorName, result);
            }

            result.SanitizedText = workingText;

            if (!result.IsClean)
            {
                _logger.LogWarning(
                    "[BẢO MẬT & KIỂM DUYỆT] Phát hiện nội dung donate vi phạm: {Summary}. Nội dung gốc: '{Original}', Đã xử lý: '{Sanitized}'",
                    result.GetSummary(),
                    result.OriginalText,
                    result.SanitizedText);
            }

            return result;
        }

        /// <summary>
        /// Làm sạch lời nhắn để hiển thị trực tiếp lên OBS Studio Alert Box
        /// </summary>
        public string SanitizeForStream(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return string.Empty;
            var result = ModerateContent(message);
            return result.SanitizedText;
        }

        /// <summary>
        /// Kiểm tra nhanh lời nhắn có hợp lệ, không chứa từ cấm hoặc link độc hại hay không
        /// </summary>
        public bool IsClean(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return true;
            return ModerateContent(message).IsClean;
        }

        // ====================================================================
        // CÁC HÀM XỬ LÝ BÊN TRONG (INTERNAL ENGINE)
        // ====================================================================

        /// <summary>
        /// Quét và xử lý liên kết URL, tên miền cờ bạc lậu, link rút gọn phishing
        /// </summary>
        private string ScanAndSanitizeLinks(string text, ModerationResult result)
        {
            var matches = UrlRegex.Matches(text);
            if (matches.Count == 0) return text;

            result.ContainsAnyLink = true;
            var replacedText = text;

            foreach (Match match in matches)
            {
                var url = match.Value;
                bool isMalicious = false;

                // Kiểm tra URL có chứa tên miền cờ bạc / lừa đảo / link rút gọn trong danh sách đen không
                foreach (var pattern in ModerationKeywords.MaliciousDomainPatterns)
                {
                    if (url.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        isMalicious = true;
                        result.HasMaliciousLink = true;
                        if (!result.DetectedKeywords.Contains(pattern))
                        {
                            result.DetectedKeywords.Add(pattern);
                        }
                        break;
                    }
                }

                if (isMalicious)
                {
                    if (!result.FlaggedReasons.Contains("Spam link độc hại / cờ bạc lậu"))
                    {
                        result.FlaggedReasons.Add("Spam link độc hại / cờ bạc lậu");
                    }
                }
                else
                {
                    if (!result.FlaggedReasons.Contains("Chèn liên kết ngoài không được phép"))
                    {
                        result.FlaggedReasons.Add("Chèn liên kết ngoài không được phép");
                    }
                }

                // Nếu cấu hình bật chặn liên kết -> Thay thế bằng thông báo an toàn
                if (_settings.BlockMaliciousLinks)
                {
                    replacedText = replacedText.Replace(url, _settings.BlockedLinkPlaceholder);
                }
            }

            return replacedText;
        }

        /// <summary>
        /// Quét từ khóa vi phạm (thô tục, kỳ thị chủng tộc, xúc phạm danh dự, từ cấm tùy chỉnh)
        /// </summary>
        private string ScanAndSanitizeKeywords(string text, ModerationResult result)
        {
            var sanitized = text;

            // 1. Quét từ ngữ phân biệt chủng tộc, kỳ thị (Mức độ nghiêm trọng cao)
            sanitized = CheckCategory(
                sanitized,
                ModerationKeywords.RacismAndDiscrimination,
                "Phân biệt chủng tộc / Kỳ thị vùng miền",
                () => result.HasRacism = true,
                result);

            // 2. Quét từ ngữ xúc phạm danh dự, lăng mạ nhân phẩm cá nhân
            sanitized = CheckCategory(
                sanitized,
                ModerationKeywords.DefamationAndHarassment,
                "Xúc phạm danh dự / Lăng mạ cá nhân",
                () => result.HasDefamation = true,
                result);

            // 3. Quét từ ngữ thô tục, tục tĩu
            sanitized = CheckCategory(
                sanitized,
                ModerationKeywords.Profanities,
                "Từ ngữ thô tục / Chửi bậy",
                () => result.HasProfanity = true,
                result);

            // 4. Quét từ cấm tùy chỉnh do Quản trị viên cấu hình trong appsettings.json
            if (_settings.CustomBannedWords.Count > 0)
            {
                sanitized = CheckCategory(
                    sanitized,
                    _settings.CustomBannedWords,
                    "Từ cấm do quản trị viên quy định",
                    () => { },
                    result);
            }

            // 5. Quét thêm dạng lách luật leetspeak (d.m, d_m, v.c.l, c.ặ.c)
            sanitized = CheckObfuscatedPatterns(sanitized, result);

            return sanitized;
        }

        /// <summary>
        /// Quét từng nhóm từ khóa vi phạm và thay thế bằng ký tự che mặt định (***)
        /// </summary>
        private string CheckCategory(
            string text,
            IEnumerable<string> words,
            string categoryName,
            Action setFlag,
            ModerationResult result)
        {
            var output = text;

            foreach (var word in words)
            {
                if (string.IsNullOrWhiteSpace(word)) continue;

                // Tạo pattern Regex kiểm tra từ khóa độc lập để tránh nhận diện nhầm từ con trong từ bình thường
                var pattern = "(?i)(?<![\\p{L}\\p{N}])" + Regex.Escape(word) + "(?![\\p{L}\\p{N}])";

                if (Regex.IsMatch(output, pattern))
                {
                    setFlag();
                    if (!result.FlaggedReasons.Contains(categoryName))
                    {
                        result.FlaggedReasons.Add(categoryName);
                    }

                    if (!result.DetectedKeywords.Contains(word))
                    {
                        result.DetectedKeywords.Add(word);
                    }

                    // Che từ vi phạm thành chuỗi dấu hoa thị (ví dụ: ***)
                    var mask = new string(_settings.MaskCharacter, Math.Max(3, word.Length));
                    output = Regex.Replace(output, pattern, mask);
                }
            }

            return output;
        }

        /// <summary>
        /// Quét phát hiện người dùng cố tình chèn dấu chấm, dấu gạch để lách bộ lọc:
        /// Ví dụ: "d.m", "d_m", "v.c.l", "c.ặ.c", "l.ồ.n", "f.u.c.k"
        /// </summary>
        private string CheckObfuscatedPatterns(string text, ModerationResult result)
        {
            var output = text;

            // Danh sách các từ nhạy cảm hay bị lách nhất
            var highRiskWords = new[] { "dm", "dcm", "vcl", "vcc", "cac", "lon", "buoi", "dit", "fuck", "shit" };

            // Chuẩn hóa văn bản: xóa hết dấu chấm/gạch/sao xen kẽ để kiểm tra
            var stripped = PunctuationNoiseRegex.Replace(output, string.Empty);

            foreach (var word in highRiskWords)
            {
                if (stripped.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    // Tạo regex mềm dẻo cho phép các ký tự nhiễu xen giữa các chữ cái của từ đó
                    var patternBuilder = new StringBuilder();
                    patternBuilder.Append(@"(?i)(?<![\p{L}\p{N}])");
                    for (int i = 0; i < word.Length; i++)
                    {
                        if (i > 0) patternBuilder.Append(@"[-.,_*#@!?$\s]*");
                        patternBuilder.Append(Regex.Escape(word[i].ToString()));
                    }
                    patternBuilder.Append(@"(?![\p{L}\p{N}])");

                    var pattern = patternBuilder.ToString();
                    if (Regex.IsMatch(output, pattern))
                    {
                        result.HasProfanity = true;
                        if (!result.FlaggedReasons.Contains("Từ ngữ thô tục (cố tình lách luật)"))
                        {
                            result.FlaggedReasons.Add("Từ ngữ thô tục (cố tình lách luật)");
                        }

                        if (!result.DetectedKeywords.Contains(word + " (lách luật)"))
                        {
                            result.DetectedKeywords.Add(word + " (lách luật)");
                        }

                        var mask = new string(_settings.MaskCharacter, 3);
                        output = Regex.Replace(output, pattern, mask);
                    }
                }
            }

            return output;
        }

        /// <summary>
        /// Quét kiểm tra biệt danh / tên người ủng hộ (DonorName)
        /// </summary>
        private void ScanDonorName(string donorName, ModerationResult result)
        {
            var nameResult = ModerateContent(donorName);
            if (!nameResult.IsClean)
            {
                result.FlaggedReasons.Add("Tên người ủng hộ chứa từ ngữ vi phạm tiêu chuẩn cộng đồng");
                foreach (var kw in nameResult.DetectedKeywords)
                {
                    if (!result.DetectedKeywords.Contains($"Tên: {kw}"))
                    {
                        result.DetectedKeywords.Add($"Tên: {kw}");
                    }
                }
            }
        }
    }
}
