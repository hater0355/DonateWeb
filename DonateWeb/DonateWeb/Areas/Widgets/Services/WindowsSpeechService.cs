using System.Speech.Synthesis;

namespace DonateWeb.Areas.Widgets.Services;

public sealed class WindowsSpeechService : IWindowsSpeechService
{
    private static readonly SemaphoreSlim SynthesisLock = new(1, 1);

    public async Task<byte[]> SynthesizeVietnameseAsync(string text, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows speech synthesis is available only on Windows.");
        }

        await SynthesisLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => Synthesize(text), cancellationToken);
        }
        finally
        {
            SynthesisLock.Release();
        }
    }

    private static byte[] Synthesize(string text)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows speech synthesis is available only on Windows.");
        }

        using var synthesizer = new SpeechSynthesizer();
        System.Speech.Synthesis.VoiceInfo? vietnameseVoice = null;
        foreach (var installedVoice in synthesizer.GetInstalledVoices())
        {
            var voice = installedVoice.VoiceInfo;
            if (string.Equals(voice.Culture.Name, "vi-VN", StringComparison.OrdinalIgnoreCase))
            {
                vietnameseVoice = voice;
                break;
            }
        }

        if (vietnameseVoice is null)
        {
            throw new InvalidOperationException("No installed Vietnamese Windows speech voice was found.");
        }

        using var output = new MemoryStream();
        synthesizer.SelectVoice(vietnameseVoice.Name);
        synthesizer.Rate = 0;
        synthesizer.Volume = 100;
        synthesizer.SetOutputToWaveStream(output);
        synthesizer.Speak(text);
        return output.ToArray();
    }
}
