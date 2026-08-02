using System.IO;
using ClipGuard.Services;

namespace ClipGuard.Tests;

/// <summary>
/// Getting this wrong is expensive in both directions: too eager and every user
/// is nagged on a legitimate install, too lax and someone runs from Downloads
/// believing they're covered after the next sign-in.
/// </summary>
public class InstallLocationTests
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void Downloads_is_volatile() =>
        Assert.Equal(LocationProblem.Downloads,
            InstallLocation.Diagnose(Path.Combine(Home, "Downloads", "ClipGuard.exe")));

    [Fact]
    public void A_subfolder_of_downloads_is_volatile() =>
        Assert.Equal(LocationProblem.Downloads,
            InstallLocation.Diagnose(Path.Combine(Home, "Downloads", "clipguard-1.0.0", "ClipGuard.exe")));

    [Fact]
    public void The_temp_folder_is_volatile() =>
        Assert.Equal(LocationProblem.Temporary,
            InstallLocation.Diagnose(Path.Combine(Path.GetTempPath(), "ClipGuard.exe")));

    [Fact]
    public void A_zip_extracted_into_temp_is_volatile() =>
        Assert.Equal(LocationProblem.Temporary,
            InstallLocation.Diagnose(Path.Combine(Path.GetTempPath(), "Temp1_clipguard.zip", "ClipGuard.exe")));

    [Fact]
    public void The_suggested_directory_is_acceptable()
    {
        // Whatever we offer to move to must itself pass, or the prompt would
        // reappear forever after a successful move.
        Assert.Equal(LocationProblem.None,
            InstallLocation.Diagnose(Path.Combine(InstallLocation.SuggestedDirectory, "ClipGuard.exe")));
    }

    [Theory]
    [InlineData(@"C:\Program Files\ClipGuard\ClipGuard.exe")]
    [InlineData(@"C:\Tools\ClipGuard\ClipGuard.exe")]
    public void Ordinary_install_paths_are_left_alone(string path) =>
        Assert.Equal(LocationProblem.None, InstallLocation.Diagnose(path));

    [Fact]
    public void The_desktop_is_left_alone()
    {
        // Plenty of people keep tools on the Desktop and it persists, so
        // flagging it would be nagging without cause.
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Assert.Equal(LocationProblem.None, InstallLocation.Diagnose(Path.Combine(desktop, "ClipGuard.exe")));
    }

    [Fact]
    public void A_developer_build_output_is_left_alone()
    {
        // Running from bin\Debug during development shouldn't prompt.
        Assert.Equal(LocationProblem.None,
            InstallLocation.Diagnose(@"C:\src\ClipGuard\src\ClipGuard\bin\Debug\net10.0-windows\ClipGuard.exe"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unknown_path_is_not_grounds_for_blocking(string path) =>
        Assert.Equal(LocationProblem.None, InstallLocation.Diagnose(path));

    [Fact]
    public void Every_problem_has_wording_for_the_user()
    {
        foreach (var problem in Enum.GetValues<LocationProblem>())
        {
            Assert.False(string.IsNullOrWhiteSpace(InstallLocation.Describe(problem)));

            if (problem != LocationProblem.None)
                Assert.False(string.IsNullOrWhiteSpace(InstallLocation.ExplainRisk(problem)));
        }
    }
}
