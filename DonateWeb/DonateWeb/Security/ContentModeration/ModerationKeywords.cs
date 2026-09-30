namespace DonateWeb.Security.ContentModeration
{
    /// <summary>
    /// DANH TỪ ĐIỂN TỪ KHÓA VI PHẠM & MẪU QUÉT ĐỘC HẠI (BẢO VỆ STREAMER TRÊN LIVESTREAM)
    /// Lưu trữ trong thư mục riêng: Security/ContentModeration/
    /// Phân loại thành các nhóm rõ ràng:
    /// 1. Từ ngữ thô tục, tục tĩu (Profanity)
    /// 2. Phân biệt chủng tộc, kỳ thị vùng miền/màu da (Racism & Discrimination)
    /// 3. Xúc phạm danh dự, nhân phẩm, lăng mạ (Defamation & Harassment)
    /// 4. Danh sách tên miền/đường dẫn cờ bạc, lừa đảo, spam link độc hại (Malicious Domains & Link Patterns)
    /// </summary>
    public static class ModerationKeywords
    {
        /// <summary>
        /// 1. TỪ NGỮ THÔ TỤC, TỤC TĨU (PROFANITY) - TIẾNG VIỆT & TIẾNG ANH
        /// Bao gồm cả các biến thể viết tắt thông dụng trên Internet.
        /// </summary>
        public static readonly HashSet<string> Profanities = new(StringComparer.OrdinalIgnoreCase)
        {
            // Tiếng Việt thông tục / chửi bậy viết tắt & đầy đủ
            "đm", "dm", "dcm", "đcm", "đmm", "dmm", "vcl", "vcc", "đkm", "dkm", "đyt", "dyt",
            "địt", "dit", "đụ", "du ma", "đụ má", "đụ mẹ", "du me", "địt mẹ", "dit me", "địt con mẹ",
            "cặc", "cac", "buồi", "buoi", "lồn", "lon", "loz", "l0n", "c4c", "chim to",
            "đĩ", "di me", "con mẹ mày", "mẹ mày", "cha mày", "tiên sư", "bà già mày",
            "dái", "vú", "háng", "chịch", "chich", "xoạc", "nện", "quất",
            
            // Tiếng Anh
            "fuck", "fucking", "fucker", "f*ck", "shit", "bitch", "asshole", "bastard",
            "dick", "cock", "pussy", "cunt", "motherfucker", "wanker", "slut", "whore"
        };

        /// <summary>
        /// 2. PHÂN BIỆT CHỦNG TỘC, KỲ THỊ VÙNG MIỀN, DÂN TỘC, MÀU DA (RACISM & DISCRIMINATION)
        /// Vi phạm nghiêm trọng tiêu chuẩn cộng đồng livestream và pháp luật.
        /// </summary>
        public static readonly HashSet<string> RacismAndDiscrimination = new(StringComparer.OrdinalIgnoreCase)
        {
            "bắc kỳ chó", "bac ky cho", "nam kỳ chó", "nam ky cho", "parky", "bake", "nuke",
            "mọi rợ", "mọi miên", "dân mọi", "mọi da đen", "da vàng mũi tẹt", "khỉ rừng",
            "bọn thanh hóa", "bọn nghệ an", "bọn mọi", "thằng tàu khựa", "bọn khựa",
            "nigger", "nigga", "ching chong", "chink", "gook", "wetback", "kike"
        };

        /// <summary>
        /// 3. XÚC PHẠM DANH DỰ, VU KHỐNG, LĂNG MẠ CÁ NHÂN (DEFAMATION & HARASSMENT)
        /// Nhắm vào bôi nhọ danh dự của Streamer hoặc người khác trên màn hình livestream.
        /// </summary>
        public static readonly HashSet<string> DefamationAndHarassment = new(StringComparer.OrdinalIgnoreCase)
        {
            "súc vật", "suc vat", "chó đẻ", "cho de", "thằng súc sinh", "đồ súc sinh",
            "lừa đảo", "lua dao", "ăn quỵt", "an quyt", "ăn cướp", "an cuop", "ăn cắp",
            "đồ con hoang", "ngu như chó", "ngu như bò", "đầu đất", "não phẳng",
            "đồ giẻ rách", "đồ rác rưởi", "đồ vô học", "đồ mặt dày", "con điếm", "con phò",
            "gái bao", "cave", "thằng nghiện", "nghiện hút", "thằng tội phạm"
        };

        /// <summary>
        /// 4. CÁC TÊN MIỀN / ĐƯỜNG DẪN SPAM, CỜ BẠC LẬU, LỪA ĐẢO NẠP THẺ (MALICIOUS DOMAINS & SCAM LINKS)
        /// Các bot spam thường lợi dụng donate để chèn link độc hại lên stream.
        /// </summary>
        public static readonly string[] MaliciousDomainPatterns = new[]
        {
            // Các trang cờ bạc, cá độ, tài xỉu lậu tại VN
            "kubet", "thabet", "go88", "sunwin", "rikvip", "fb88", "w88", "bet188", "188bet",
            "789club", "b52club", "iwin", "hitclub", "taixiu", "baccarat", "nohu", "gamebai",
            
            // Dịch vụ rút gọn link thường dùng để phát tán phishing/malware
            "bit.ly", "tinyurl.com", "t.co", "shorturl.at", "is.gd", "buff.ly", "adf.ly", "shorte.st",
            
            // Spam group telegram lừa đảo hoặc mã độc
            "t.me/", "telegram.me/", "zalo.me/g/",
            
            // Các đuôi tên miền rác/nguy hiểm thường gặp
            ".tk", ".ml", ".ga", ".cf", ".gq", ".top", ".xyz", ".buzz", ".click"
        };

        /// <summary>
        /// Biểu thức chính quy (Regex) bắt các dạng URL bất kỳ (http, https, ftp, domain.ext)
        /// </summary>
        public const string GeneralUrlRegexPattern = @"(https?:\/\/[^\s]+)|(www\.[^\s]+)|([a-zA-Z0-9\-_]+\.(com|vn|net|org|xyz|top|site|online|io|info|pro|me|cc|biz|tv)\b[^\s]*)";
    }
}
