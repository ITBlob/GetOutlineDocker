using System.Runtime.CompilerServices;
using Storybloq.Reader.Core.Loading;
using Storybloq.Reader.Core.State;

namespace Storybloq.Reader.Core.Tests;

/// <summary>
/// Locates the committed <c>.story/</c> fixture trees.
/// </summary>
/// <remarks>
/// Resolved from the compile-time path of this file rather than copied to the output directory:
/// MSBuild content globs treat dot-prefixed directories inconsistently, and a fixture that
/// silently fails to copy would show up as a mystifying test failure. The fixtures are only ever
/// read, so sharing them across tests is safe.
/// </remarks>
internal static class Fixtures
{
    public static string Root { get; } = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "fixtures"));

    public static string SampleProject => Path.Combine(Root, "sample-project");

    public static string MinimalProject => Path.Combine(Root, "minimal-project");

    public static string BrokenProject => Path.Combine(Root, "broken-project");

    public static string SecondProject => Path.Combine(Root, "second-project");

    public static StoryProject Load(string projectRoot) =>
        new StoryProjectLoader(ResilientFileReader.Immediate).Load(projectRoot);

    public static ProjectState State(string projectRoot) => ProjectState.From(Load(projectRoot));

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}

/// <summary>A scratch directory that cleans itself up.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "storybloq-reader-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <summary>Creates a project root containing an empty but valid <c>.story/config.json</c>.</summary>
    public string CreateProject(string relativeDirectory, string projectName)
    {
        var root = Combine(relativeDirectory);
        Directory.CreateDirectory(System.IO.Path.Combine(root, ".story"));
        File.WriteAllText(
            System.IO.Path.Combine(root, ".story", "config.json"),
            $$"""{ "version": "0.1.5", "project": "{{projectName}}", "type": "app", "language": "ts", "features": {} }""");
        return root;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leaked temp directory is not worth failing a test run over.
        }
    }
}
