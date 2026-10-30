namespace JApp.Launcher.Tests;

using System.Runtime.InteropServices;

/// <summary>Fixture-driven integration tests: they run only when the
/// environment provides a JVM, the boot jar and a japp archive, and otherwise
/// skip. Their purpose is to verify JniLauncher's JNIEnv function table
/// indices against a real JVM — the table layout is platform-independent per
/// the JNI specification, so passing here covers the Windows production path
/// as well.</summary>
public static class JniLauncherIntegrationTests
{
    /// <summary>The declared function table must land every used member on
    /// its jni.h slot number — this catches any padding miscount before a
    /// real JVM is involved.</summary>
    [Fact]
    public static void DeclaredFunctionTableMatchesTheHeaderSlots()
    {
        var slot = (nint)IntPtr.Size;

        Assert.Equal(6 * slot, Marshal.OffsetOf<JniLauncher.JniNativeInterface>(nameof(JniLauncher.JniNativeInterface.FindClass)));
        Assert.Equal(15 * slot, Marshal.OffsetOf<JniLauncher.JniNativeInterface>(nameof(JniLauncher.JniNativeInterface.ExceptionOccurred)));
        Assert.Equal(16 * slot, Marshal.OffsetOf<JniLauncher.JniNativeInterface>(nameof(JniLauncher.JniNativeInterface.ExceptionDescribe)));
        Assert.Equal(113 * slot, Marshal.OffsetOf<JniLauncher.JniNativeInterface>(nameof(JniLauncher.JniNativeInterface.GetStaticMethodID)));
        Assert.Equal(143 * slot, Marshal.OffsetOf<JniLauncher.JniNativeInterface>(nameof(JniLauncher.JniNativeInterface.CallStaticVoidMethodA)));
        Assert.Equal(167 * slot, Marshal.OffsetOf<JniLauncher.JniNativeInterface>(nameof(JniLauncher.JniNativeInterface.NewStringUTF)));
        Assert.Equal(172 * slot, Marshal.OffsetOf<JniLauncher.JniNativeInterface>(nameof(JniLauncher.JniNativeInterface.NewObjectArray)));
        Assert.Equal(174 * slot, Marshal.OffsetOf<JniLauncher.JniNativeInterface>(nameof(JniLauncher.JniNativeInterface.SetObjectArrayElement)));
    }

    private static string? Fixture(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    // Class loading needs a deep native stack; testhost worker threads are
    // too small, so drive the launch on a dedicated thread like the
    // production path does.
    private static int? RunOnVmThread(string jvmLibrary, IReadOnlyList<string> options, IReadOnlyList<string> appArgs)
    {
        int? result = null;
        var thread = new Thread(() => result = JniLauncher.RunInProcess(jvmLibrary, options, appArgs), 32 * 1024 * 1024);
        thread.Start();
        thread.Join();
        return result;
    }

    private static string JavaExecutable(string javaHome) =>
        Path.Combine(javaHome, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");

    private static string[] BootCommand(
        string javaHome, string bootJar, string? bootArgsProperty, IReadOnlyList<string> appArgs)
    {
        List<string> command = [JavaExecutable(javaHome)];
        if (bootArgsProperty is not null)
            command.Add(bootArgsProperty);

        command.Add("--module-path");
        command.Add(bootJar);
        command.Add("--add-exports=java.base/jdk.internal.loader=org.glavo.japp.boot");
        command.Add("--add-exports=java.base/jdk.internal.module=org.glavo.japp.boot");
        command.Add("--add-exports=java.base/jdk.internal.misc=org.glavo.japp.boot");
        command.Add("--add-opens=java.base/jdk.internal.loader=org.glavo.japp.boot");
        command.Add("--add-opens=java.base/java.lang=org.glavo.japp.boot");
        command.Add("--module");
        command.Add("org.glavo.japp.boot");
        command.AddRange(appArgs);
        return [.. command];
    }

    [Fact]
    public static async Task InProcessJvmRunsAJappApplication()
    {
        var javaHome = Fixture("JAPP_TEST_JAVA_HOME");
        var bootJar = Fixture("JAPP_TEST_BOOT_JAR");
        var japp = Fixture("JAPP_TEST_JAPP");
        if (javaHome is null || bootJar is null || japp is null)
            return;

        var jvmLibrary = JniLauncher.FindJvmLibrary(javaHome);
        Assert.NotNull(jvmLibrary);

        var os = OperatingSystem.IsMacOS() ? "macos" : "linux";
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "aarch64" : "x86-64";

        var metadata = JAppLauncherMetadata.ReadFile(japp);
        var group = metadata.Group;
        group.Resolve(new JvmContext(21, os, arch, "default"));

        var bootArgs = new BootArgs.Builder();
        bootArgs.WriteHeader(japp, metadata.BaseOffset, metadata.BootMetadataOffset, metadata.BootMetadataSize);
        await bootArgs.WriteClassOrModulePathAsync(BootArgs.Field.ModulePath, 21, group.ModulePath);
        bootArgs.WriteMainClass(group.MainClass);
        bootArgs.WriteMainModule(group.MainModule);
        bootArgs.WriteEnd();

        var command = BootCommand(
            javaHome, bootJar,
            "-Dorg.glavo.japp.boot.args=" + Convert.ToBase64String(bootArgs.ToArray()),
            ["x"]);
        var plan = JniLauncher.TryTranslate(command);
        Assert.NotNull(plan);

        var result = RunOnVmThread(jvmLibrary, JniLauncher.PrepareVmOptions(plan), ["x"]);
        Assert.Equal(0, result);
    }
}
