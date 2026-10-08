using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DonateWeb.TtsCompanion;

internal sealed record StoredSettings(string ServerUrl, string StreamerSlug, string WidgetToken)
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DonateWeb", "tts.dat");

    public static StoredSettings Load()
    {
        try
        {
            var protectedBytes = File.ReadAllBytes(SettingsPath);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<StoredSettings>(bytes) ?? new("", "", "");
        }
        catch
        {
            return new("", "", "");
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this);
        File.WriteAllBytes(SettingsPath, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
    }
}
