namespace JApp.Launcher.Tests;

public static class BootJarTests
{
    private static string WriteTempFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "japp-bootjar-test-" + Path.GetRandomFileName());
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] Eocd(uint centralDirectoryLength, uint centralDirectoryOffset, ushort commentLength)
    {
        var eocd = new byte[22];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(eocd, 0x06054b50);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(12), centralDirectoryLength);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(16), centralDirectoryOffset);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(eocd.AsSpan(20), commentLength);
        return eocd;
    }

    [Fact]
    public static void TrailingZipSizeMeasuresTheAppendedZipIncludingComment()
    {
        // [prefix 100][central dir offset 10][central dir 5][EOCD][comment 3]
        var file = new List<byte>();
        file.AddRange(new byte[100]);
        file.AddRange(new byte[10]);
        file.AddRange(new byte[5]);
        file.AddRange(Eocd(5, 10, 3));
        file.AddRange(new byte[3]);

        Assert.Equal(10 + 5 + 22 + 3, BootJar.TrailingZipSize(WriteTempFile([.. file])));
    }

    [Fact]
    public static void TrailingZipSizeRejectsFilesWithoutEocd()
    {
        Assert.Equal(-1, BootJar.TrailingZipSize(WriteTempFile(new byte[100])));
    }

    [Fact]
    public static void TrailingZipSizeRejectsMismatchedCommentLength()
    {
        var file = new List<byte>();
        file.AddRange(new byte[50]);
        file.AddRange(Eocd(0, 0, 0));
        file.AddRange(new byte[3]); // trailing bytes beyond the claimed comment

        Assert.Equal(-1, BootJar.TrailingZipSize(WriteTempFile([.. file])));
    }

    [Fact]
    public static void ModulePathEntryIsAJarSuffixedLinkToTheExecutable()
    {
        if (OperatingSystem.IsWindows())
            return; // symlink creation may require privileges there

        var self = WriteTempFile([1, 2, 3]);
        var entry = BootJar.CreateModulePathEntry(self, zipSize: 0);

        Assert.EndsWith(".jar", entry, StringComparison.Ordinal);

        var resolved = File.ResolveLinkTarget(entry, returnFinalTarget: true);
        Assert.NotNull(resolved);
        Assert.Equal(Path.GetFullPath(self), resolved.FullName);
        Assert.True(File.Exists(entry)); // follows the link to the target
    }

    [Fact]
    public static void HardLinkEntryCreatesASecondNameForTheSameFile()
    {
        var target = WriteTempFile([1, 2, 3]);
        var link = Path.Combine(Path.GetTempPath(), "japp-hardlink-test-" + Path.GetRandomFileName() + ".jar");

        BootJar.CreateHardLinkEntry(link, target);

        // Same file data: writes through one name surface via the other.
        File.WriteAllBytes(link, [9]);
        Assert.Equal([9], File.ReadAllBytes(target));
        File.Delete(link);
    }
}
