using Storybloq.Reader.Core.Discovery;
using Xunit;

namespace Storybloq.Reader.Core.Tests;

public class ProjectRootDiscoveryTests
{
    private static Func<string, string?> NoEnvironment => _ => null;

    private static Func<string, string?> Environment(string variable, string value) =>
        name => name == variable ? value : null;

    [Fact]
    public void FindsTheRootFromTheRootItself()
    {
        var root = ProjectRootDiscovery.Discover(Fixtures.SampleProject, NoEnvironment);
        Assert.Equal(Path.GetFullPath(Fixtures.SampleProject), root);
    }

    /// <summary>
    /// This is what makes the folder picker forgiving: the user points at any directory inside
    /// their repository and still lands on the project.
    /// </summary>
    [Fact]
    public void WalksUpFromADeeplyNestedDirectory()
    {
        using var temp = new TempDirectory();
        var projectRoot = temp.CreateProject("repo", "Nested");
        var deep = Path.Combine(projectRoot, "src", "app", "components");
        Directory.CreateDirectory(deep);

        Assert.Equal(Path.GetFullPath(projectRoot), ProjectRootDiscovery.Discover(deep, NoEnvironment));
    }

    [Fact]
    public void StopsAtTheNearestProjectWhenProjectsAreNested()
    {
        using var temp = new TempDirectory();
        temp.CreateProject("outer", "Outer");
        var inner = temp.CreateProject(Path.Combine("outer", "packages", "inner"), "Inner");

        Assert.Equal(Path.GetFullPath(inner), ProjectRootDiscovery.Discover(inner, NoEnvironment));
    }

    [Fact]
    public void ReturnsNullWhenNoAncestorHoldsAProject()
    {
        using var temp = new TempDirectory();
        var directory = temp.Combine("no", "project", "here");
        Directory.CreateDirectory(directory);

        Assert.Null(ProjectRootDiscovery.Discover(directory, NoEnvironment));
    }

    [Fact]
    public void ADirectoryWithoutConfigJsonIsNotARoot()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("half-initialised");
        Directory.CreateDirectory(Path.Combine(root, ".story", "tickets"));

        Assert.False(ProjectRootDiscovery.IsProjectRoot(root));
        Assert.Null(ProjectRootDiscovery.Discover(root, NoEnvironment));
    }

    [Fact]
    public void TheEnvironmentOverrideWinsOverWalkingUp()
    {
        using var temp = new TempDirectory();
        var overridden = temp.CreateProject("explicit", "Explicit");
        var unrelated = temp.CreateProject("walked-up", "WalkedUp");

        var root = ProjectRootDiscovery.Discover(
            unrelated,
            Environment(ProjectRootDiscovery.EnvironmentVariable, overridden));

        Assert.Equal(Path.GetFullPath(overridden), root);
    }

    [Fact]
    public void TheLegacyEnvironmentVariableIsStillHonoured()
    {
        using var temp = new TempDirectory();
        var overridden = temp.CreateProject("explicit", "Explicit");

        var root = ProjectRootDiscovery.Discover(
            temp.Path,
            Environment(ProjectRootDiscovery.LegacyEnvironmentVariable, overridden));

        Assert.Equal(Path.GetFullPath(overridden), root);
    }

    [Fact]
    public void TheCurrentVariableTakesPrecedenceOverTheLegacyOne()
    {
        using var temp = new TempDirectory();
        var current = temp.CreateProject("current", "Current");
        var legacy = temp.CreateProject("legacy", "Legacy");

        var root = ProjectRootDiscovery.Discover(temp.Path, name => name switch
        {
            ProjectRootDiscovery.EnvironmentVariable => current,
            ProjectRootDiscovery.LegacyEnvironmentVariable => legacy,
            _ => null,
        });

        Assert.Equal(Path.GetFullPath(current), root);
    }

    /// <summary>
    /// An explicit override that turns out to be wrong yields nothing, rather than quietly
    /// walking up into a neighbouring project the user did not ask for.
    /// </summary>
    [Fact]
    public void AnInvalidOverrideDoesNotFallBackToWalkingUp()
    {
        using var temp = new TempDirectory();
        var real = temp.CreateProject("real", "Real");
        var nested = Path.Combine(real, "src");
        Directory.CreateDirectory(nested);

        var root = ProjectRootDiscovery.Discover(
            nested,
            Environment(ProjectRootDiscovery.EnvironmentVariable, temp.Combine("does-not-exist")));

        Assert.Null(root);
    }

    [Fact]
    public void AnEmptyOverrideIsTreatedAsUnset()
    {
        var root = ProjectRootDiscovery.Discover(
            Fixtures.SampleProject,
            Environment(ProjectRootDiscovery.EnvironmentVariable, "   "));

        Assert.Equal(Path.GetFullPath(Fixtures.SampleProject), root);
    }
}
