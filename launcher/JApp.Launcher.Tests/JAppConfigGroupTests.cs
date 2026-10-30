namespace JApp.Launcher.Tests;

public static class JAppConfigGroupTests
{
    private static readonly JvmContext Linux17 = new(17, "linux", "x86-64", "default");

    private static JAppResourceGroupReference.Local Local(string name, int index) =>
        new(name, index, null);

    [Fact]
    public static void NullConditionAlwaysApplies()
    {
        var group = new JAppConfigGroup { Condition = null };
        Assert.True(group.CanApply(Linux17));
    }

    [Fact]
    public static void ResolveSkipsInapplicableChildren()
    {
        var root = new JAppConfigGroup();
        var child = new JAppConfigGroup { Condition = "java(version: 99)" };
        child.JvmProperties.Add("k=v");
        root.Children.Add(child);

        root.Resolve(Linux17);

        Assert.Empty(root.JvmProperties);
    }

    [Fact]
    public static void ResolveReplacesPathEntriesByNameAndAppendsTheRest()
    {
        var root = new JAppConfigGroup();
        root.ModulePath.Add(Local("app", 0));
        root.ModulePath.Add(Local("other", 1));

        var child = new JAppConfigGroup();
        child.ModulePath.Add(Local("app", 7));
        child.ModulePath.Add(Local("new", 2));
        root.Children.Add(child);

        root.Resolve(Linux17);

        Assert.Equal(3, root.ModulePath.Count);
        Assert.Equal(7, Assert.IsType<JAppResourceGroupReference.Local>(root.ModulePath[0]).Index);
        Assert.Equal(1, Assert.IsType<JAppResourceGroupReference.Local>(root.ModulePath[1]).Index);
        Assert.Equal(2, Assert.IsType<JAppResourceGroupReference.Local>(root.ModulePath[2]).Index);
    }

    [Fact]
    public static void ResolveAppendsListsAndOverridesMain()
    {
        var root = new JAppConfigGroup { MainClass = "a.Main" };
        root.JvmProperties.Add("first=1");
        root.ExtraJvmOptions.Add("-Xmx1g");

        var child = new JAppConfigGroup();
        child.JvmProperties.Add("second=2");
        child.ExtraJvmOptions.Add("-Xss1m");
        child.MainModule = "app.module";
        root.Children.Add(child);

        root.Resolve(Linux17);

        Assert.Equal(["first=1", "second=2"], root.JvmProperties);
        Assert.Equal(["-Xmx1g", "-Xss1m"], root.ExtraJvmOptions);
        Assert.Equal("a.Main", root.MainClass);
        Assert.Equal("app.module", root.MainModule);
    }

    [Fact]
    public static void ResolveDescendsIntoNestedChildren()
    {
        var root = new JAppConfigGroup();
        var outer = new JAppConfigGroup();
        var inner = new JAppConfigGroup();
        inner.AddReads.Add("m1=m2");
        outer.Children.Add(inner);
        root.Children.Add(outer);

        root.Resolve(Linux17);

        Assert.Equal(["m1=m2"], root.AddReads);
    }

    private static JAppConfigGroup ReadGroup(Action<LittleEndianWriter> writeFields)
    {
        var writer = new LittleEndianWriter();
        writer.WriteInt32(JAppConfigGroup.MagicNumber);
        writeFields(writer);
        writer.WriteByte((byte)JAppConfigGroup.Field.End);
        return JAppConfigGroup.ReadFrom(writer.ToArray());
    }

    [Fact]
    public static void ReadFromParsesMavenReference()
    {
        var group = ReadGroup(writer =>
        {
            writer.WriteByte((byte)JAppConfigGroup.Field.ModulePath);
            writer.WriteInt32(1);
            writer.WriteByte(JAppResourceGroupReference.MagicNumber);
            writer.WriteByte(JAppResourceGroupReference.Maven.Id);
            writer.WriteString("lib");
            writer.WriteInt32(0); // repository: null (central)
            writer.WriteString("com.example");
            writer.WriteString("lib");
            writer.WriteString("1.0");
            writer.WriteInt32(0); // classifier: null
        });

        var reference = Assert.IsType<JAppResourceGroupReference.Maven>(Assert.Single(group.ModulePath));
        Assert.Equal("lib", reference.Name);
        Assert.Null(reference.Repository);
        Assert.Equal("com.example", reference.Group);
        Assert.Equal("lib", reference.Artifact);
        Assert.Equal("1.0", reference.Version);
        Assert.Null(reference.Classifier);
    }

    [Fact]
    public static void ReadFromRejectsIncompleteMavenReference()
    {
        byte[] Bytes()
        {
            var writer = new LittleEndianWriter();
            writer.WriteInt32(JAppConfigGroup.MagicNumber);
            writer.WriteByte((byte)JAppConfigGroup.Field.ModulePath);
            writer.WriteInt32(1);
            writer.WriteByte(JAppResourceGroupReference.MagicNumber);
            writer.WriteByte(JAppResourceGroupReference.Maven.Id);
            writer.WriteString("lib");
            writer.WriteInt32(0); // repository
            writer.WriteInt32(0); // group: missing coordinate
            writer.WriteInt32(0); // artifact
            writer.WriteInt32(0); // version
            writer.WriteByte((byte)JAppConfigGroup.Field.End);
            return writer.ToArray();
        }

        Assert.Throws<IOException>(() => JAppConfigGroup.ReadFrom(Bytes()));
    }
}
