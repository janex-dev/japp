namespace JApp.Launcher.Tests;

public static class JAppLauncherMetadataTests
{
    [Fact]
    public static void MagicNumberSpellsJappInLittleEndian()
    {
        Assert.Equal("JAPP"u8.ToArray(),
            BitConverter.GetBytes(JAppLauncherMetadata.FileEndMagicNumber));
    }

    private static string WriteTempFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "japp-test-" + Path.GetRandomFileName() + ".japp");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] SampleGroup() => TestJapp.GroupBytes(writer =>
    {
        TestJapp.WriteStringField(writer, JAppConfigGroup.Field.Condition, "java(version: 11)");
        TestJapp.WriteStringField(writer, JAppConfigGroup.Field.MainClass, "com.example.Main");
    });

    [Fact]
    public static void ReadFileParsesTrailerAndGroup()
    {
        var file = WriteTempFile(TestJapp.BuildFile(SampleGroup(), bootMetadataOffset: 32));

        var metadata = JAppLauncherMetadata.ReadFile(file);

        Assert.Equal(0, metadata.BaseOffset);
        Assert.Equal(32, metadata.BootMetadataOffset);
        Assert.Equal(TestJapp.DataPoolSize, metadata.BootMetadataSize);
        Assert.Equal("java(version: 11)", metadata.Group.Condition);
        Assert.Equal("com.example.Main", metadata.Group.MainClass);
    }

    [Fact]
    public static void ReadFileStripsPrefixAndTrailingZip()
    {
        var prefix = "#!/usr/bin/env bash\nexec japp\n"u8.ToArray();
        var file = WriteTempFile(TestJapp.BuildFile(
            SampleGroup(), bootMetadataOffset: 32, prefix: prefix, zipSuffix: TestJapp.Eocd()));

        var metadata = JAppLauncherMetadata.ReadFile(file);

        // baseOffset recovers the shell prefix length; offsets stay content-relative.
        Assert.Equal(prefix.Length, metadata.BaseOffset);
        Assert.Equal(32, metadata.BootMetadataOffset);
        Assert.Equal("java(version: 11)", metadata.Group.Condition);
    }

    [Fact]
    public static void ReadFileParsesSubGroupsAndReferences()
    {
        var group = TestJapp.GroupBytes(writer =>
        {
            writer.WriteByte((byte)JAppConfigGroup.Field.ModulePath);
            writer.WriteInt32(1);
            TestJapp.WriteLocalReference(writer, "app", 3, (17, 5));

            writer.WriteByte((byte)JAppConfigGroup.Field.SubGroups);
            writer.WriteInt32(1);
            writer.WriteInt32(JAppConfigGroup.MagicNumber);
            TestJapp.WriteStringListField(writer, JAppConfigGroup.Field.JvmProperties, ["k=v"]);
            writer.WriteByte((byte)JAppConfigGroup.Field.End);
        });

        var file = WriteTempFile(TestJapp.BuildFile(group, bootMetadataOffset: 8));
        var metadata = JAppLauncherMetadata.ReadFile(file);

        var reference = Assert.IsType<JAppResourceGroupReference.Local>(Assert.Single(metadata.Group.ModulePath));
        Assert.Equal("app", reference.Name);
        Assert.Equal(3, reference.Index);
        Assert.NotNull(reference.MultiReleaseIndexes);
        Assert.Equal(5, reference.MultiReleaseIndexes[17]);

        var child = Assert.Single(metadata.Group.Children);
        Assert.Equal(["k=v"], child.JvmProperties);
    }

    [Fact]
    public static void ReadFileRejectsVersionMismatch()
    {
        var file = WriteTempFile(TestJapp.BuildFile(SampleGroup(), 32, majorVersion: 0));

        var exception = Assert.Throws<IOException>(() => JAppLauncherMetadata.ReadFile(file));
        Assert.Equal("Version number mismatch", exception.Message);
    }

    [Fact]
    public static void ReadFileRejectsNonEmptyFlags()
    {
        var file = WriteTempFile(TestJapp.BuildFile(SampleGroup(), 32, flags: 0b1));

        Assert.Throws<IOException>(() => JAppLauncherMetadata.ReadFile(file));
    }

    [Fact]
    public static void ReadFileRejectsBadMagic()
    {
        var bytes = TestJapp.BuildFile(SampleGroup(), 32);
        // The FileEnd magic sits exactly FileEndSize bytes from the end.
        bytes[bytes.Length - JAppLauncherMetadata.FileEndSize] ^= 0xff;
        var file = WriteTempFile(bytes);

        Assert.Throws<IOException>(() => JAppLauncherMetadata.ReadFile(file));
    }

    [Fact]
    public static void ReadFileRejectsFilesTooSmall()
    {
        var file = WriteTempFile([1, 2, 3]);

        Assert.Throws<IOException>(() => JAppLauncherMetadata.ReadFile(file));
    }

    [Fact]
    public static void HasEmbeddedArchiveDetectsTrailerAtPhysicalEnd()
    {
        Assert.True(JAppLauncherMetadata.HasEmbeddedArchive(
            WriteTempFile(TestJapp.BuildFile(SampleGroup(), 32))));
    }

    [Fact]
    public static void HasEmbeddedArchiveDetectsTrailerBeforeTrailingZip()
    {
        Assert.True(JAppLauncherMetadata.HasEmbeddedArchive(
            WriteTempFile(TestJapp.BuildFile(SampleGroup(), 32, zipSuffix: TestJapp.Eocd()))));
    }

    [Fact]
    public static void HasEmbeddedArchiveRejectsFilesWithoutTrailer()
    {
        Assert.False(JAppLauncherMetadata.HasEmbeddedArchive(WriteTempFile(new byte[100])));
        Assert.False(JAppLauncherMetadata.HasEmbeddedArchive(WriteTempFile([1, 2, 3])));

        // A trailing zip alone (no FileEnd before it) is not an archive.
        var zipOnly = new List<byte>();
        zipOnly.AddRange(new byte[100]);
        zipOnly.AddRange(TestJapp.Eocd());
        Assert.False(JAppLauncherMetadata.HasEmbeddedArchive(WriteTempFile([.. zipOnly])));
    }
}
