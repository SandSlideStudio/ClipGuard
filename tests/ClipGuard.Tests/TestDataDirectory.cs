using System.Runtime.CompilerServices;

namespace ClipGuard.Tests;

/// <summary>
/// Points Storage at a throwaway directory before anything touches it.
/// Storage.Root is resolved in a static initialiser, so this has to run before
/// the first access - a module initialiser is the only hook early enough.
/// Without it the suite would read and overwrite the real install's history.
/// </summary>
internal static class TestDataDirectory
{
    public static string Path { get; private set; } = string.Empty;

    [ModuleInitializer]
    internal static void Redirect()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ClipGuard.Tests", Guid.NewGuid().ToString("N"));

        System.IO.Directory.CreateDirectory(Path);
        Environment.SetEnvironmentVariable("CLIPGUARD_DATA_DIR", Path);
    }
}
