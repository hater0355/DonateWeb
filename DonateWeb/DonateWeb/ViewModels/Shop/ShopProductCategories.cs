namespace DonateWeb.ViewModels.Shop;

/// <summary>Danh mục chuẩn dùng khi đăng sản phẩm và lọc gian hàng.</summary>
public static class ShopProductCategories
{
    public const string Clothing = "Thời trang & Quần áo";
    public const string Footwear = "Giày dép";

    public static IReadOnlyList<string> All { get; } =
    [
        Clothing,
        Footwear,
        "Đồ ăn & Đồ uống",
        "Mỹ phẩm & Chăm sóc cá nhân",
        "Công nghệ & Điện tử",
        "Nhà cửa & Đời sống",
        "Thể thao & Dã ngoại",
        "Sách & Văn phòng phẩm",
        "Sức khỏe",
        "Đồ chơi & Trẻ em",
        "Gaming & Phụ kiện máy tính",
        "Âm nhạc & Giải trí",
        "Đồ sưu tầm & Mô hình",
        "Handmade & Quà tặng",
        "Thú cưng & Phụ kiện",
        "Khác"
    ];

    public static IReadOnlyList<string> ClothingSizes { get; } = ["XS", "S", "M", "L", "XL", "XXL", "Free size"];
    public static IReadOnlyList<string> FootwearSizes { get; } = ["35", "36", "37", "38", "39", "40", "41", "42", "43", "44", "45"];

    public static IReadOnlyList<string> SizesFor(string? category) => category switch
    {
        Clothing => ClothingSizes,
        Footwear => FootwearSizes,
        _ => Array.Empty<string>()
    };

    public static bool IsValid(string? category) => !string.IsNullOrWhiteSpace(category)
        && All.Contains(category, StringComparer.Ordinal);
}
