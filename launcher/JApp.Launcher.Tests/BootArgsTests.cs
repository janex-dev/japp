namespace JApp.Launcher.Tests;

public static class BootArgsTests
{
    private static byte[] Bytes(Action<LittleEndianWriter> write)
    {
        var writer = new LittleEndianWriter();
        write(writer);
        return writer.ToArray();
    }

    [Fact]
    public static void HeaderAndStringListLayout()
    {
        var builder = new BootArgs.Builder();
        builder.WriteHeader("/tmp/a.japp", 0, 16, 32);
        builder.WriteStringListField(BootArgs.Field.AddReads, ["m1=m2"]);
        builder.WriteEnd();

        var expected = Bytes(w =>
        {
            w.WriteString("/tmp/a.japp");
            w.WriteInt64(0);
            w.WriteInt64(16);
            w.WriteInt64(32);
            w.WriteByte(5); // ADD_READS
            w.WriteInt32(1);
            w.WriteString("m1=m2");
            w.WriteByte(0); // END
        });

        Assert.Equal(expected, builder.ToArray());
    }

    [Fact]
    public static void MainClassAndModuleFields()
    {
        var builder = new BootArgs.Builder();
        builder.WriteMainClass("com.Main");
        builder.WriteMainModule("com.module");
        builder.WriteEnd();

        var expected = Bytes(w =>
        {
            w.WriteByte(1); // MAIN_CLASS
            w.WriteString("com.Main");
            w.WriteByte(2); // MAIN_MODULE
            w.WriteString("com.module");
            w.WriteByte(0);
        });

        Assert.Equal(expected, builder.ToArray());
    }

    public static TheoryData<int, int[]> MultiReleaseCases => new()
    {
        // release -> surviving multi-release indexes (9, 17 and 21 variants of one group)
        { 8, [] },
        { 9, [1] },
        { 17, [1, 2] },
        { 21, [1, 2, 3] },
    };

    [Theory]
    [MemberData(nameof(MultiReleaseCases))]
    public static async Task LocalReferencesFilterMultiReleaseIndexes(int release, int[] expectedIndexes)
    {
        var reference = new JAppResourceGroupReference.Local(
            "app", 3, new SortedList<int, int> { [9] = 1, [17] = 2, [21] = 3 });

        var builder = new BootArgs.Builder();
        await builder.WriteClassOrModulePathAsync(BootArgs.Field.ModulePath, release, [reference]);

        var expected = Bytes(w =>
        {
            w.WriteByte(3); // MODULE_PATH
            w.WriteByte(1); // RESOLVED_REFERENCE_LOCAL
            w.WriteString("app");
            w.WriteInt32(3);
            foreach (var index in expectedIndexes)
                w.WriteInt32(index);
            w.WriteInt32(-1);
            w.WriteByte(0); // RESOLVED_REFERENCE_END
        });

        Assert.Equal(expected, builder.ToArray());
    }

    [Fact]
    public static async Task MavenReferenceResolvesThroughTheInjectedResolver()
    {
        var reference = new JAppResourceGroupReference.Maven(
            "lib", null, "g", "a", "1", null);

        var builder = new BootArgs.Builder();
        await builder.WriteClassOrModulePathAsync(
            BootArgs.Field.ClassPath, 17, [reference],
            resolveMaven: _ => Task.FromResult("/repo/g/a/1/a-1.jar"));

        var expected = Bytes(w =>
        {
            w.WriteByte(4); // CLASS_PATH
            w.WriteByte(2); // RESOLVED_REFERENCE_EXTERNAL
            w.WriteString("lib");
            w.WriteString("/repo/g/a/1/a-1.jar");
            w.WriteByte(0);
        });

        Assert.Equal(expected, builder.ToArray());
    }

    [Fact]
    public static async Task EmptyListsWriteNothing()
    {
        var builder = new BootArgs.Builder();
        builder.WriteStringListField(BootArgs.Field.AddOpens, []);
        await builder.WriteClassOrModulePathAsync(BootArgs.Field.ModulePath, 17, []);
        builder.WriteEnd();

        Assert.Equal([0], builder.ToArray());
    }
}
