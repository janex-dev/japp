namespace JApp.Launcher.Tests;

public static class BundlerTests
{
    private static string WriteTempFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "japp-bundler-test-" + Path.GetRandomFileName());
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] SampleArchive() => TestJapp.GroupBytes(writer =>
        TestJapp.WriteStringField(writer, JAppConfigGroup.Field.MainClass, "com.example.Main"));

    private static byte[] BuildArchiveFile() => TestJapp.BuildFile(SampleArchive(), bootMetadataOffset: 32);

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(part => part.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    private static byte[] RunBundle(byte[] japp, byte[] launcher)
    {
        var jappPath = WriteTempFile(japp);
        var launcherPath = WriteTempFile(launcher);
        var output = Path.Combine(Path.GetTempPath(), "japp-bundler-out-" + Path.GetRandomFileName());

        Assert.Equal(0, Bundler.Run([jappPath, "--launcher", launcherPath, "-o", output]));
        return File.ReadAllBytes(output);
    }

    [Fact]
    public static void InsertsArchiveBeforeTrailingJar()
    {
        var exe = new byte[100];
        var japp = BuildArchiveFile();
        var jar = TestJapp.Eocd();

        var result = RunBundle(japp, Concat(exe, jar));

        Assert.Equal(Concat(exe, japp, jar), result);
    }

    [Fact]
    public static void AppendsArchiveToBareLauncher()
    {
        var exe = new byte[100];
        var japp = BuildArchiveFile();

        var result = RunBundle(japp, exe);

        Assert.Equal(Concat(exe, japp), result);
    }

    [Fact]
    public static void ReplacesTheArchivesAlreadyEmbeddedInTheBase()
    {
        var exe = new byte[50];
        var oldArchive = BuildArchiveFile();
        var newArchive = TestJapp.BuildFile(
            SampleArchive(), bootMetadataOffset: 8, zipSuffix: TestJapp.Eocd());
        var jar = TestJapp.Eocd();

        // Base shape: [exe][old archive][jar]
        var result = RunBundle(newArchive, Concat(exe, oldArchive, jar));

        Assert.Equal(Concat(exe, newArchive, jar), result);
    }

    [Fact]
    public static void OutputInheritsTheBaseExecutableBit()
    {
        if (OperatingSystem.IsWindows())
            return;

        var launcher = WriteTempFile(Concat(new byte[10], TestJapp.Eocd()));
        File.SetUnixFileMode(launcher,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var japp = WriteTempFile(BuildArchiveFile());
        var output = Path.Combine(Path.GetTempPath(), "japp-bundler-mode-" + Path.GetRandomFileName());

        Assert.Equal(0, Bundler.Run([japp, "--launcher", launcher, "-o", output]));

        var mode = File.GetUnixFileMode(output);
        Assert.True(mode.HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public static void RejectsOutputOverwritingAnInput()
    {
        var japp = WriteTempFile(BuildArchiveFile());
        var launcher = WriteTempFile(new byte[10]);

        Assert.Equal(1, Bundler.Run([japp, "--launcher", launcher, "-o", launcher]));
        Assert.Equal(1, Bundler.Run([japp, "--launcher", launcher, "-o", japp]));
    }

    [Fact]
    public static void RejectsInvalidArchiveInput()
    {
        var japp = WriteTempFile(new byte[64]);
        var launcher = WriteTempFile(new byte[10]);
        var output = Path.Combine(Path.GetTempPath(), "japp-bundler-bad-" + Path.GetRandomFileName());

        Assert.Equal(1, Bundler.Run([japp, "--launcher", launcher, "-o", output]));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public static void WindowsTargetBaseGainsExeSuffixInDefaultOutput()
    {
        var japp = WriteTempFile(BuildArchiveFile());
        var jappPath = Path.ChangeExtension(japp, ".japp");
        File.Move(japp, jappPath);

        // A PE-magic base: the default output name carries .exe regardless of
        // the bundling host.
        var launcher = WriteTempFile([(byte)'M', (byte)'Z', .. new byte[8]]);

        Assert.Equal(0, Bundler.Run([jappPath, "--launcher", launcher]));

        Assert.True(File.Exists(jappPath[..^".japp".Length] + ".exe"));
    }

    [Fact]
    public static void RejectsUnknownOptionsAndMissingValues()
    {
        Assert.Equal(1, Bundler.Run(["--bogus"]));
        Assert.Equal(1, Bundler.Run(["a.japp", "-o"]));
        Assert.Equal(1, Bundler.Run(["a.japp", "--launcher"]));
        Assert.Equal(1, Bundler.Run([]));
    }
}
