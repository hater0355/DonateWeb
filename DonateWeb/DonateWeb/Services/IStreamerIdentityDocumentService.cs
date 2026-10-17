using DonateWeb.ViewModels.Streamer;
using Microsoft.AspNetCore.Http;

namespace DonateWeb.Services
{
    public sealed record StoredIdentityDocument(byte[] Content, string ContentType);

    public interface IStreamerIdentityDocumentService
    {
        string ProtectIdentity(StreamerIdentityData identity);
        StreamerIdentityData? UnprotectIdentity(string? protectedIdentity);
        Task<string> SaveDocumentAsync(IFormFile file, CancellationToken cancellationToken = default);
        Task<StoredIdentityDocument?> ReadDocumentAsync(string? key, CancellationToken cancellationToken = default);
        Task DeleteDocumentAsync(string? key, CancellationToken cancellationToken = default);
    }
}
