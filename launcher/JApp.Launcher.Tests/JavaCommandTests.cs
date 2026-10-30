namespace JApp.Launcher.Tests;

public static class JavaCommandTests
{
    [Fact]
    public static async Task MissingCommandReturns127()
    {
        if (OperatingSystem.IsWindows())
            return; // the exec-replacement path is Unix-only

        Assert.Equal(127, await JavaCommand.RunAsync(["/nonexistent/java-binary"]));
    }
}
