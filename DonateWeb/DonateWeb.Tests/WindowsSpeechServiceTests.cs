using System.Text;
using DonateWeb.Areas.Widgets.Services;
using Xunit;

namespace DonateWeb.Tests;

public class WindowsSpeechServiceTests
{
    [Fact]
    public async Task VietnameseWindowsVoiceProducesPlayableWaveAudio()
    {
        if (!OperatingSystem.IsWindows()) return;

        var wave = await new WindowsSpeechService()
            .SynthesizeVietnameseAsync("Xin chào, đây là âm thanh thử nghiệm.", CancellationToken.None);

        Assert.True(wave.Length > 44);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wave, 8, 4));
    }
}
