namespace JApp.Launcher.Tests;

public static class JniLauncherTests
{
    // The exact shape Launcher.BuildCommandAsync produces.
    private static string[] SampleCommand() =>
    [
        @"C:\java\bin\java.exe",
        "-Dk=v",
        "-Xmx1g",
        "-Dorg.glavo.japp.boot.args=AAA=",
        "--module-path", @"C:\boot\japp-boot.jar",
        "--add-exports=java.base/jdk.internal.loader=org.glavo.japp.boot",
        "--add-exports=java.base/jdk.internal.module=org.glavo.japp.boot",
        "--add-exports=java.base/jdk.internal.misc=org.glavo.japp.boot",
        "--add-opens=java.base/jdk.internal.loader=org.glavo.japp.boot",
        "--add-opens=java.base/java.lang=org.glavo.japp.boot",
        "--module", "org.glavo.japp.boot",
        "arg1", "arg2",
    ];

    [Fact]
    public static void TryTranslateSplitsVmOptionsModuleAndAppArguments()
    {
        var plan = JniLauncher.TryTranslate(SampleCommand());

        Assert.NotNull(plan);
        Assert.Equal(
        [
            "-Dk=v",
            "-Xmx1g",
            "-Dorg.glavo.japp.boot.args=AAA=",
            "--add-exports=java.base/jdk.internal.loader=org.glavo.japp.boot",
            "--add-exports=java.base/jdk.internal.module=org.glavo.japp.boot",
            "--add-exports=java.base/jdk.internal.misc=org.glavo.japp.boot",
            "--add-opens=java.base/jdk.internal.loader=org.glavo.japp.boot",
            "--add-opens=java.base/java.lang=org.glavo.japp.boot",
        ], plan.VmOptions);
        Assert.Equal(@"C:\boot\japp-boot.jar", plan.ModulePath);
        Assert.Equal("org.glavo.japp.boot", plan.Module);
        Assert.Equal(["arg1", "arg2"], plan.AppArgs);
    }

    [Fact]
    public static void TryTranslateRejectsIncompleteCommands()
    {
        Assert.Null(JniLauncher.TryTranslate(["java.exe", "-Xmx1g"]));
        Assert.Null(JniLauncher.TryTranslate(["java.exe", "--module-path", "x"]));
        Assert.Null(JniLauncher.TryTranslate(["java.exe", "--module-path", "x", "--module"]));
        Assert.Null(JniLauncher.TryTranslate(["java.exe", "--module", "m", "arg"]));
    }

    [Fact]
    public static void PrepareVmOptionsAddsLauncherEmulation()
    {
        var plan = JniLauncher.TryTranslate(SampleCommand())!;

        string? saved = Environment.GetEnvironmentVariable("JDK_JAVA_OPTIONS");
        try
        {
            Environment.SetEnvironmentVariable("JDK_JAVA_OPTIONS", null);
            var options = JniLauncher.PrepareVmOptions(plan);
            Assert.Equal("-Dk=v", options[0]);
            Assert.Equal(@"--module-path=C:\boot\japp-boot.jar", options[^3]);
            Assert.Equal("-Dsun.java.launcher=SUN_STANDARD", options[^2]);
            Assert.Equal("-Dsun.java.command=arg1 arg2", options[^1]);

            Environment.SetEnvironmentVariable("JDK_JAVA_OPTIONS", "-Xms64m \"-Dq=a b\"");
            options = JniLauncher.PrepareVmOptions(plan);
            Assert.Equal(
                [
                    "-Xms64m", "-Dq=a b", .. plan.VmOptions,
                    @"--module-path=C:\boot\japp-boot.jar",
                    "-Dsun.java.launcher=SUN_STANDARD",
                    "-Dsun.java.command=arg1 arg2"
                ],
                options);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JDK_JAVA_OPTIONS", saved);
        }
    }

    [Fact]
    public static void SplitTokensHonorsQuotes()
    {
        Assert.Equal(["a", "b c", "d"], JniLauncher.SplitTokens("  a \"b c\" d "));
        Assert.Equal(["-Dx=1"], JniLauncher.SplitTokens("-Dx=1"));
        Assert.Equal([], JniLauncher.SplitTokens("   "));
        Assert.Equal([""], JniLauncher.SplitTokens("\""));
    }

    [Fact]
    public static void TryParseStackSizeReadsAttachedXss()
    {
        Assert.Equal(512 * 1024, JniLauncher.TryParseStackSize(["-Xss512k"]));
        Assert.Equal(2 * 1024 * 1024, JniLauncher.TryParseStackSize(["-Xss2m"]));
        Assert.Equal(1024L * 1024 * 1024, JniLauncher.TryParseStackSize(["-Xss1g", "-Xmx1g"]));
        Assert.Equal(0, JniLauncher.TryParseStackSize(["-Xmx1g"]));
        Assert.Equal(0, JniLauncher.TryParseStackSize(["-Xss"]));
        Assert.Equal(0, JniLauncher.TryParseStackSize(["-Xssbananas"]));
    }
}
