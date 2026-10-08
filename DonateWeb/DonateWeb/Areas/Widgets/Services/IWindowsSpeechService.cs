namespace DonateWeb.Areas.Widgets.Services;

public interface IWindowsSpeechService
{
    Task<byte[]> SynthesizeVietnameseAsync(string text, CancellationToken cancellationToken);
}
