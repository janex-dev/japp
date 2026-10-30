namespace JApp.Launcher.Tests;

public static class MavenResolverTests
{
    [Fact]
    public static void LayoutPathFollowsTheMavenRepositoryLayout()
    {
        Assert.Equal(
            Path.Combine("/repo", "com", "example", "lib", "lib", "1.0", "lib-1.0.jar"),
            MavenResolver.LayoutPath("/repo", "com.example.lib", "lib", "1.0", null));

        Assert.Equal(
            Path.Combine("/repo", "g", "a", "1", "a-1-sources.jar"),
            MavenResolver.LayoutPath("/repo", "g", "a", "1", "sources"));
    }
}
