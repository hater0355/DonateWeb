namespace DonateWeb.Security.Uploads;

public static class BannerUploadService
{
    public const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB cho ảnh bìa

    public static async Task<(string? Path, string? Error)> SaveAsync(
        IFormFile? file,
        string webRootPath,
        int userId,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return (null, null);
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return (null, "Ảnh bìa không được vượt quá 10 MB.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var header = new byte[12];
        await using (var input = file.OpenReadStream())
        {
            var count = 0;
            while (count < header.Length)
            {
                var read = await input.ReadAsync(header.AsMemory(count), cancellationToken);
                if (read == 0) break;
                count += read;
            }

            if (!HasValidImageSignature(extension, header.AsSpan(0, count)))
            {
                return (null, "Tệp tải lên phải là ảnh JPEG, PNG, GIF hoặc WebP hợp lệ.");
            }
        }

        var uploadsFolder = Path.Combine(webRootPath, "images", "banners");
        Directory.CreateDirectory(uploadsFolder);

        var uniqueFileName = $"banner_{userId}_{Guid.NewGuid():N}{extension}";
        var physicalFilePath = Path.Combine(uploadsFolder, uniqueFileName);
        await using (var output = new FileStream(physicalFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await using (var input = file.OpenReadStream())
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        return ($"/images/banners/{uniqueFileName}", null);
    }

    private static bool HasValidImageSignature(string extension, ReadOnlySpan<byte> header) => extension switch
    {
        ".jpg" or ".jpeg" => header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
        ".png" => header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        ".gif" => header.Length >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8)),
        ".webp" => header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8),
        _ => false
    };
}
