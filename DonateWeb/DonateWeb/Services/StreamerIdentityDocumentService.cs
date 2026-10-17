using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using DonateWeb.ViewModels.Streamer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace DonateWeb.Services
{
    /// <summary>
    /// Mã hóa thông tin và ảnh CCCD, lưu ảnh ngoài wwwroot để không thể truy cập công khai.
    /// </summary>
    public sealed class StreamerIdentityDocumentService : IStreamerIdentityDocumentService
    {
        private const int MaxDocumentBytes = 5 * 1024 * 1024;
        private static readonly Regex SafeKeyPattern = new("^[a-f0-9]{64}\\.bin$", RegexOptions.Compiled);
        private readonly IDataProtector _identityProtector;
        private readonly IDataProtector _documentProtector;
        private readonly string _storagePath;

        public StreamerIdentityDocumentService(
            IDataProtectionProvider dataProtectionProvider,
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            _identityProtector = dataProtectionProvider.CreateProtector("DonateWeb.StreamerIdentity.v1");
            _documentProtector = dataProtectionProvider.CreateProtector("DonateWeb.StreamerIdentityDocument.v1");
            _storagePath = configuration["IdentityDocuments:StoragePath"]
                ?? Path.Combine(environment.ContentRootPath, "private-data", "identity-documents");
        }

        public string ProtectIdentity(StreamerIdentityData identity)
        {
            var json = JsonSerializer.Serialize(identity);
            return _identityProtector.Protect(json);
        }

        public StreamerIdentityData? UnprotectIdentity(string? protectedIdentity)
        {
            if (string.IsNullOrWhiteSpace(protectedIdentity)) return null;

            try
            {
                return JsonSerializer.Deserialize<StreamerIdentityData>(_identityProtector.Unprotect(protectedIdentity));
            }
            catch (CryptographicException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public async Task<string> SaveDocumentAsync(IFormFile file, CancellationToken cancellationToken = default)
        {
            if (file.Length <= 0 || file.Length > MaxDocumentBytes)
                throw new InvalidDataException("Mỗi ảnh CCCD phải có dung lượng từ 1 byte đến 5 MB.");

            await using var input = file.OpenReadStream();
            await using var buffer = new MemoryStream((int)file.Length);
            await input.CopyToAsync(buffer, cancellationToken);
            var content = buffer.ToArray();
            var contentType = DetectImageType(content);
            if (contentType == null)
                throw new InvalidDataException("Ảnh CCCD phải là JPEG, PNG hoặc WebP hợp lệ.");

            var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant() + ".bin";
            Directory.CreateDirectory(_storagePath);
            var protectedContent = _documentProtector.Protect(content);
            await File.WriteAllBytesAsync(GetSafePath(key), protectedContent, cancellationToken);
            return key;
        }

        public async Task<StoredIdentityDocument?> ReadDocumentAsync(string? key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key) || !SafeKeyPattern.IsMatch(key)) return null;

            try
            {
                var protectedContent = await File.ReadAllBytesAsync(GetSafePath(key), cancellationToken);
                var content = _documentProtector.Unprotect(protectedContent);
                var contentType = DetectImageType(content);
                return contentType == null ? null : new StoredIdentityDocument(content, contentType);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        public Task DeleteDocumentAsync(string? key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(key) && SafeKeyPattern.IsMatch(key))
            {
                var path = GetSafePath(key);
                if (File.Exists(path)) File.Delete(path);
            }
            return Task.CompletedTask;
        }

        private string GetSafePath(string key)
        {
            if (!SafeKeyPattern.IsMatch(key)) throw new InvalidOperationException("Mã tài liệu không hợp lệ.");
            return Path.Combine(_storagePath, key);
        }

        private static string? DetectImageType(byte[] content)
        {
            if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
                return "image/jpeg";

            if (content.Length >= 8 && content.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
                return "image/png";

            if (content.Length >= 12 &&
                content.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                content.AsSpan(8, 4).SequenceEqual("WEBP"u8))
                return "image/webp";

            return null;
        }
    }
}
