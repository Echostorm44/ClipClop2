using System.Text.Json;
using ClipClop2.Storage;

namespace ClipClop2.Tests;

internal sealed class DpapiTests
{
    [Test]
    public async Task ProtectedDataRoundTrips()
    {
        byte[] plain = "pinned: hunter2 ✓"u8.ToArray();

        byte[] encrypted = Dpapi.Protect(plain);
        byte[]? decrypted = Dpapi.Unprotect(encrypted);

        await Assert.That(encrypted.AsSpan().IndexOf(plain) < 0).IsTrue();
        await Assert.That(decrypted).IsNotNull();
        await Assert.That(decrypted!.SequenceEqual(plain)).IsTrue();
    }

    [Test]
    public async Task TamperedBlobIsRejected()
    {
        byte[] encrypted = Dpapi.Protect("secret"u8);
        encrypted[^1] ^= 0xFF;

        await Assert.That(Dpapi.Unprotect(encrypted)).IsNull();
    }
}

internal sealed class SettingsTests
{
    [Test]
    public async Task DefaultsMatchSpec()
    {
        var settings = new Settings();

        await Assert.That(settings.HotkeyKey).IsEqualTo(Cascade.UI.Key.Backtick); // VK_OEM_3, the `~ key
        await Assert.That(settings.HotkeyModifiers).IsEqualTo(Cascade.UI.ModifierKeys.Ctrl);
        await Assert.That(settings.HistoryBudgetMb).IsEqualTo(300);
        await Assert.That(settings.StartWithWindows).IsFalse();
    }

    [Test]
    public async Task BudgetBelowMinimumIsRaised()
    {
        var settings = new Settings { HistoryBudgetMb = 12 };

        settings.Normalize();

        await Assert.That(settings.HistoryBudgetMb).IsEqualTo(Settings.MinHistoryBudgetMb);
    }

    [Test]
    public async Task SourceGeneratedJsonRoundTrips()
    {
        var original = new Settings { HistoryBudgetMb = 512, StartWithWindows = true, HotkeyKey = Cascade.UI.Key.V, HotkeyModifiers = Cascade.UI.ModifierKeys.Ctrl | Cascade.UI.ModifierKeys.Shift };

        string json = JsonSerializer.Serialize(original, SettingsJsonContext.Default.Settings);
        Settings? restored = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.Settings);

        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.HistoryBudgetMb).IsEqualTo(512);
        await Assert.That(restored.StartWithWindows).IsTrue();
        await Assert.That(restored.HotkeyKey).IsEqualTo(Cascade.UI.Key.V);
        await Assert.That(restored.HotkeyModifiers).IsEqualTo(Cascade.UI.ModifierKeys.Ctrl | Cascade.UI.ModifierKeys.Shift);
        await Assert.That(json).Contains("\"HotkeyKey\": \"V\"");
    }
}
