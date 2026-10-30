namespace JApp.Launcher.Tests;

public static class ConditionParserTests
{
    private static readonly JvmContext Linux17X64 = new(17, "linux", "x86-64", "default");
    private static readonly JvmContext Windows21Arm64 = new(21, "windows", "aarch64", "default");
    private static readonly JvmContext Macos22Musl = new(22, "macos", "x86-64", "musl");

    [Fact]
    public static void VersionIsAMinimum()
    {
        var condition = ConditionParser.Parse("java(version: 17)");

        Assert.False(condition.Test(new JvmContext(16, "linux", "x86-64", "default")));
        Assert.True(condition.Test(Linux17X64));
        Assert.True(condition.Test(new JvmContext(25, "linux", "x86-64", "default")));
    }

    [Fact]
    public static void OsMatchListMatchesAlternatives()
    {
        var condition = ConditionParser.Parse("java(os: linux|macos)");

        Assert.True(condition.Test(Linux17X64));
        Assert.True(condition.Test(Macos22Musl));
        Assert.False(condition.Test(Windows21Arm64));
    }

    [Fact]
    public static void NegativeMatchListInverts()
    {
        var condition = ConditionParser.Parse("java(os: !windows)");

        Assert.True(condition.Test(Linux17X64));
        Assert.False(condition.Test(Windows21Arm64));
    }

    [Fact]
    public static void AndOrAndParentheses()
    {
        var condition = ConditionParser.Parse(
            "java(version: 11) && (java(os: linux) || java(os: macos))");

        Assert.True(condition.Test(Linux17X64));
        Assert.True(condition.Test(Macos22Musl));
        Assert.False(condition.Test(Windows21Arm64));
        Assert.False(condition.Test(new JvmContext(10, "linux", "x86-64", "default")));
    }

    [Fact]
    public static void NotBindsLowerThanAnd()
    {
        // !a && b parses as !(a && b), exactly like the Java parser.
        var condition = ConditionParser.Parse("!java(os: linux) && java(arch: x86-64)");

        Assert.True(condition.Test(Windows21Arm64)); // !(false && true)
        Assert.False(condition.Test(Linux17X64));    // !(true && true)
    }

    [Fact]
    public static void LibcCondition()
    {
        var condition = ConditionParser.Parse("java(libc: musl)");

        Assert.True(condition.Test(Macos22Musl));
        Assert.False(condition.Test(Linux17X64));
    }

    [Fact]
    public static void QuotedValues()
    {
        Assert.True(ConditionParser.Parse("java(os: 'linux')").Test(Linux17X64));
    }

    [Fact]
    public static void UnknownOptionThrows()
    {
        Assert.Throws<ArgumentException>(() => ConditionParser.Parse("java(unknown: 1)"));
    }

    [Fact]
    public static void UnknownConditionKindThrows()
    {
        Assert.Throws<ArgumentException>(() => ConditionParser.Parse("cpp(version: 17)"));
    }

    [Fact]
    public static void TrailingGarbageThrows()
    {
        Assert.Throws<ArgumentException>(() => ConditionParser.Parse("java(version: 1) x"));
    }

    [Fact]
    public static void UnbalancedParenthesesThrow()
    {
        Assert.Throws<ArgumentException>(() => ConditionParser.Parse("(java(version: 1)"));
    }

    [Fact]
    public static void SingleAmpersandThrows()
    {
        Assert.Throws<ArgumentException>(() => ConditionParser.Parse("java(version: 1) & java(os: linux)"));
    }

    [Fact]
    public static void ToStringMatchesJavaShape()
    {
        Assert.Equal("java(version=17, os=[linux])",
            ConditionParser.Parse("java(version: 17, os: linux)").ToString());
    }
}
