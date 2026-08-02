using System.IO;
using ClipGuard.Models;
using ClipGuard.Services;

namespace ClipGuard.Tests;

/// <summary>
/// Storage holds previews of everything the user copies, so "is it actually
/// encrypted" is a security assertion, not a serialisation one.
/// </summary>
public class StorageTests
{
    private static string PathFor(string file) => Path.Combine(Storage.Root, file);

    private static List<ClipboardEvent> SampleEvents(string secret) =>
    [
        new()
        {
            Id = "CG-TEST-0001",
            ProcessName = "chrome.exe",
            Action = ClipboardAction.Write,
            Category = EventCategory.ClipboardWrite,
            ContentPreview = secret,
            Timestamp = DateTimeOffset.Now
        }
    ];

    [Fact]
    public void Tests_never_touch_the_real_install()
    {
        Assert.Equal(TestDataDirectory.Path, Storage.Root);
        Assert.DoesNotContain("AppData\\Local\\ClipGuard", Storage.Root);
    }

    [Fact]
    public void Encrypted_files_round_trip()
    {
        const string file = "roundtrip.json";
        var written = SampleEvents("a very secret passphrase");

        Storage.Save(file, written, encrypted: true);
        var read = Storage.Load(file, () => new List<ClipboardEvent>(), encrypted: true);

        Assert.Single(read);
        Assert.Equal("a very secret passphrase", read[0].ContentPreview);
    }

    [Fact]
    public void Clipboard_content_is_not_readable_on_disk()
    {
        const string file = "secrecy.json";
        const string secret = "correct-horse-battery-staple";

        Storage.Save(file, SampleEvents(secret), encrypted: true);

        var raw = File.ReadAllText(PathFor(file));

        Assert.DoesNotContain(secret, raw);
        Assert.DoesNotContain("chrome.exe", raw);
        Assert.StartsWith("CGENC1:", raw);
    }

    [Fact]
    public void Legacy_plaintext_files_still_load()
    {
        // An install from before encryption must not lose its history.
        const string file = "legacy.json";
        File.WriteAllText(PathFor(file),
            """[{"Id":"CG-OLD-1","ProcessName":"code.exe","ContentPreview":"old entry"}]""");

        var read = Storage.Load(file, () => new List<ClipboardEvent>(), encrypted: true);

        Assert.Single(read);
        Assert.Equal("old entry", read[0].ContentPreview);
    }

    [Fact]
    public void Legacy_files_are_encrypted_on_the_next_save()
    {
        const string file = "migrate.json";
        File.WriteAllText(PathFor(file),
            """[{"Id":"CG-OLD-2","ProcessName":"code.exe","ContentPreview":"migrate me"}]""");

        var read = Storage.Load(file, () => new List<ClipboardEvent>(), encrypted: true);
        Storage.Save(file, read, encrypted: true);

        var raw = File.ReadAllText(PathFor(file));
        Assert.StartsWith("CGENC1:", raw);
        Assert.DoesNotContain("migrate me", raw);
    }

    [Fact]
    public void A_corrupt_store_falls_back_instead_of_throwing()
    {
        // Protection starting is more important than history surviving.
        const string file = "corrupt.json";
        File.WriteAllText(PathFor(file), "CGENC1:this-is-not-valid-base64!!");

        var read = Storage.Load(file, () => new List<ClipboardEvent> { new() { Id = "fallback" } }, encrypted: true);

        Assert.Equal("fallback", read[0].Id);
    }

    [Fact]
    public void Missing_files_fall_back_quietly()
    {
        var read = Storage.Load("nope.json", () => new List<string> { "default" });
        Assert.Equal("default", read[0]);
    }

    [Fact]
    public void Settings_round_trip_without_encryption()
    {
        // Settings hold no clipboard content, so they stay readable.
        const string file = "settings-test.json";
        Storage.Save(file, new AppSettings { AutoClear = AutoClearMode.Minutes5, LaunchMinimized = false });

        var read = Storage.Load(file, () => new AppSettings());

        Assert.Equal(AutoClearMode.Minutes5, read.AutoClear);
        Assert.False(read.LaunchMinimized);
    }
}
