namespace JApp.Launcher.Tests;

public static class CommandLineTests
{
    [Fact]
    public static async Task NoArgumentsPrintsHelpAndExitsZero()
    {
        Assert.Equal(0, await CommandLine.RunAsync([]));
    }

    [Theory]
    [InlineData("help")]
    [InlineData("-help")]
    [InlineData("--help")]
    public static async Task HelpVariantsExitZero(string mode)
    {
        Assert.Equal(0, await CommandLine.RunAsync([mode]));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("--version")]
    public static async Task VersionVariantsExitZero(string mode)
    {
        Assert.Equal(0, await CommandLine.RunAsync([mode]));
    }

    [Fact]
    public static async Task UnknownModeExitsOne()
    {
        Assert.Equal(1, await CommandLine.RunAsync(["bogus"]));
    }

    [Fact]
    public static async Task RunWithoutFileExitsOne()
    {
        Assert.Equal(1, await CommandLine.RunAsync(["run"]));
    }

    [Fact]
    public static async Task RunHelpExitsZero()
    {
        Assert.Equal(0, await CommandLine.RunAsync(["run", "--help"]));
    }

    [Fact]
    public static async Task RunRejectsUnknownOptionWithOne()
    {
        Assert.Equal(1, await CommandLine.RunAsync(["run", "-Xmx1g"]));
    }

    [Fact]
    public static async Task RunMissingJappFileSurfacesError()
    {
        // The file does not exist: metadata reading fails, message goes to stderr, exit 1.
        Assert.Equal(1, await CommandLine.RunAsync(["run", "/nonexistent/app.japp"]));
    }

    // ---- dual launch mode (embedded archive), exercised through the injected run seam ----

    private const int FakeExitCode = 7;

    private static (string File, IReadOnlyList<string> JvmOptions, IReadOnlyList<string> AppArgs)? _invocation;

    private static Task<int> CaptureRun(string file, IReadOnlyList<string> jvmOptions, IReadOnlyList<string> appArgs)
    {
        _invocation = (file, jvmOptions, appArgs);
        return Task.FromResult(FakeExitCode);
    }

    private static void AssertCaptured(string file, string[] jvmOptions, string[] appArgs)
    {
        Assert.NotNull(_invocation);
        Assert.Equal(Path.GetFullPath(file), _invocation.Value.File);
        Assert.Equal(jvmOptions, _invocation.Value.JvmOptions);
        Assert.Equal(appArgs, _invocation.Value.AppArgs);
    }

    [Fact]
    public static async Task EmbeddedModeForwardsEveryArgumentVerbatim()
    {
        _invocation = null;
        Assert.Equal(FakeExitCode,
            await CommandLine.RunAsync(["-J-Xmx1g", "--", "help", "run"], "/tmp/embedded.japp", CaptureRun));
        AssertCaptured("/tmp/embedded.japp", [], ["-J-Xmx1g", "--", "help", "run"]);

        _invocation = null;
        Assert.Equal(FakeExitCode, await CommandLine.RunAsync([], "/tmp/embedded.japp", CaptureRun));
        AssertCaptured("/tmp/embedded.japp", [], []);
    }

    [Fact]
    public static async Task EmbeddedGatewayParsesJvmOptionsAndSeparator()
    {
        _invocation = null;
        Assert.Equal(FakeExitCode,
            await CommandLine.RunAsync(["--japp", "run", "-J-Xmx4g", "-J-XX:+UseZGC", "--", "a", "b"],
                "/tmp/embedded.japp", CaptureRun));
        AssertCaptured("/tmp/embedded.japp", ["-Xmx4g", "-XX:+UseZGC"], ["a", "b"]);
    }

    [Fact]
    public static async Task EmbeddedGatewayTreatsNonFilePositionalsAsAppArguments()
    {
        _invocation = null;
        Assert.Equal(FakeExitCode,
            await CommandLine.RunAsync(["--japp", "run", "not-a-file", "arg"], "/tmp/embedded.japp", CaptureRun));
        AssertCaptured("/tmp/embedded.japp", [], ["not-a-file", "arg"]);
    }

    [Fact]
    public static async Task EmbeddedGatewayUsesExistingPositionalAsExplicitFile()
    {
        var existing = Path.GetTempFileName();

        _invocation = null;
        Assert.Equal(FakeExitCode,
            await CommandLine.RunAsync(["--japp", "run", existing, "x"], "/tmp/embedded.japp", CaptureRun));
        AssertCaptured(existing, [], ["x"]);
    }

    [Fact]
    public static async Task EmbeddedGatewayHelpVariantsExitZero()
    {
        Assert.Equal(0, await CommandLine.RunAsync(["--japp"], "/tmp/embedded.japp", CaptureRun));
        Assert.Equal(0, await CommandLine.RunAsync(["--japp", "help"], "/tmp/embedded.japp", CaptureRun));
        Assert.Equal(0, await CommandLine.RunAsync(["--japp", "run", "--help"], "/tmp/embedded.japp", CaptureRun));
    }

    [Fact]
    public static async Task EmbeddedGatewayUnknownModeExitsOne()
    {
        Assert.Equal(1, await CommandLine.RunAsync(["--japp", "bogus"], "/tmp/embedded.japp", CaptureRun));
    }

    [Fact]
    public static async Task BareModeKeepsJavaCompatibleParsing()
    {
        _invocation = null;
        Assert.Equal(FakeExitCode,
            await CommandLine.RunAsync(["run", "-J-Xmx1g", "some.japp", "--", "-J-x"], null, CaptureRun));
        AssertCaptured("some.japp", ["-Xmx1g"], ["-J-x"]);

        _invocation = null;
        Assert.Equal(1, await CommandLine.RunAsync(["run", "-Xmx1g"], null, CaptureRun));
        Assert.Null(_invocation);
    }
}
