namespace JApp.Launcher.Tests;

public static class LauncherTests
{
    private static JAppLauncherMetadata Metadata(long baseOffset = 0, long bootOffset = 16, long bootSize = 8) =>
        new(baseOffset, bootOffset, bootSize, new JAppConfigGroup());

    private static string BootArgsBlob(IReadOnlyList<string> command)
    {
        var argument = Assert.Single(command, arg => arg.StartsWith("-Dorg.glavo.japp.boot.args="));
        return argument["-Dorg.glavo.japp.boot.args=".Length..];
    }

    private static (string File, long BaseOffset, long BootOffset, long BootSize) DecodeHeader(string base64)
    {
        var reader = new LittleEndianReader(Convert.FromBase64String(base64));
        return (reader.ReadString(), reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64());
    }

    [Fact]
    public static async Task CommandOrderMatchesTheJavaLauncher()
    {
        var group = new JAppConfigGroup { MainClass = "com.example.Main" };
        group.JvmProperties.Add("k=v");
        group.ExtraJvmOptions.Add("-Xmx1g");
        group.ModulePath.Add(new JAppResourceGroupReference.Local("app", 0, null));

        var command = await Launcher.BuildCommandAsync(
            "/usr/bin/java", new JvmContext(17, "linux", "x86-64", "default"), group,
            "/tmp/app.japp", Metadata(), ["-Xmx2g"], ["arg1", "arg2"], "/tmp/boot.jar");

        Assert.Equal("/usr/bin/java", command[0]);
        Assert.Equal("-Dk=v", command[1]);
        Assert.Equal("-Xmx1g", command[2]);

        var bootArgsIndex = 3;
        Assert.StartsWith("-Dorg.glavo.japp.boot.args=", command[bootArgsIndex]);

        var tail = command[(bootArgsIndex + 1)..];
        Assert.Equal(
        [
            "-Xmx2g",
            "--module-path", "/tmp/boot.jar",
            "--add-exports=java.base/jdk.internal.loader=org.glavo.japp.boot",
            "--add-exports=java.base/jdk.internal.module=org.glavo.japp.boot",
            "--add-exports=java.base/jdk.internal.misc=org.glavo.japp.boot",
            "--add-opens=java.base/jdk.internal.loader=org.glavo.japp.boot",
            "--add-opens=java.base/java.lang=org.glavo.japp.boot",
            "--module", "org.glavo.japp.boot",
            "arg1", "arg2",
        ], tail);

        var header = DecodeHeader(BootArgsBlob(command));
        Assert.Equal("/tmp/app.japp", header.File);
        Assert.Equal(0, header.BaseOffset);
        Assert.Equal(16, header.BootOffset);
        Assert.Equal(8, header.BootSize);
    }

    [Fact]
    public static async Task NativeAccessOn17AddsFlagsAndFiltersAllUnnamed()
    {
        var group = new JAppConfigGroup();
        group.EnableNativeAccess.Add("ALL-UNNAMED");
        group.EnableNativeAccess.Add("m.x");

        var command = await Launcher.BuildCommandAsync(
            "/usr/bin/java", new JvmContext(17, "linux", "x86-64", "default"), group,
            "/tmp/app.japp", Metadata(), [], [], "/tmp/boot.jar");

        Assert.Contains("--enable-native-access=org.glavo.japp.boot", command);
        Assert.Contains("--enable-native-access=ALL-UNNAMED", command);
        Assert.Contains("--enable-preview", command); // 17 <= 21

        // The boot args field lists every module except ALL-UNNAMED.
        var reader = new LittleEndianReader(Convert.FromBase64String(BootArgsBlob(command)));
        reader.ReadString();
        reader.ReadInt64();
        reader.ReadInt64();
        reader.ReadInt64();

        var fields = new List<byte>();
        while (reader.Remaining > 0)
            fields.Add(reader.ReadByte());

        Assert.Contains(BootArgs.IdResolvedReferenceEnd, fields);
        Assert.Equal((byte)BootArgs.Field.EnableNativeAccess, fields[0]);

        // field id, count 1, "m.x"
        Assert.Equal(
        [
            (byte)BootArgs.Field.EnableNativeAccess, 1, 0, 0, 0, 3, 0, 0, 0, (byte)'m', (byte)'.', (byte)'x',
            (byte)BootArgs.Field.End,
        ], fields);
    }

    [Fact]
    public static async Task NativeAccessOn16UsesForeignRestricted()
    {
        var group = new JAppConfigGroup();
        group.EnableNativeAccess.Add("m.x");

        var command = await Launcher.BuildCommandAsync(
            "/usr/bin/java", new JvmContext(16, "linux", "x86-64", "default"), group,
            "/tmp/app.japp", Metadata(), [], [], "/tmp/boot.jar");

        Assert.Contains("-Dforeign.restricted=permit", command);
        Assert.DoesNotContain(command, arg => arg.StartsWith("--enable-native-access"));
        Assert.Contains("--enable-preview", command);
    }

    [Fact]
    public static async Task NativeAccessOn22SkipsPreview()
    {
        var group = new JAppConfigGroup();
        group.EnableNativeAccess.Add("m.x");

        var command = await Launcher.BuildCommandAsync(
            "/usr/bin/java", new JvmContext(22, "linux", "x86-64", "default"), group,
            "/tmp/app.japp", Metadata(), [], [], "/tmp/boot.jar");

        Assert.DoesNotContain("--enable-preview", command);
        Assert.Contains("--enable-native-access=org.glavo.japp.boot", command);
    }
}
