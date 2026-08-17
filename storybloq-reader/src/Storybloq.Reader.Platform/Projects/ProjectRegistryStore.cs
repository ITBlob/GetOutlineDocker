using System.Text.Json;
using System.Text.Json.Serialization;

namespace Storybloq.Reader.Platform.Projects;

public sealed class RegisteredProject
{
    [JsonPropertyName("root")]
    public string Root { get; set; } = string.Empty;

    /// <summary>User-chosen name, overriding the one in <c>config.json</c>.</summary>
    [JsonPropertyName("nickname")]
    public string? Nickname { get; set; }
}

/// <summary>
/// Persists the list of projects the user has added.
/// </summary>
/// <remarks>
/// Settings live under <c>%LOCALAPPDATA%\StorybloqReader</c> rather than in
/// <c>ApplicationData.Current</c>, which throws without package identity — and this app ships
/// unpackaged specifically so that CI can produce a runnable build with no code-signing
/// certificate. Writes go through a temp file and a replace, so a crash mid-save cannot leave
/// the user with an unreadable project list.
/// </remarks>
public sealed class ProjectRegistryStore
{
    private const string FolderName = "StorybloqReader";
    private const string FileName = "projects.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public ProjectRegistryStore(string? path = null) => Path = path ?? DefaultPath;

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        FolderName,
        FileName);

    public string Path { get; }

    /// <summary>
    /// Reads the saved list. A missing or damaged file yields an empty list rather than an
    /// error: losing the project list is annoying, but refusing to start is worse.
    /// </summary>
    public IReadOnlyList<RegisteredProject> Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return [];
            }

            var json = File.ReadAllText(Path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            var projects = JsonSerializer.Deserialize<List<RegisteredProject>>(json, Options);
            return projects?
                .Where(project => !string.IsNullOrWhiteSpace(project.Root))
                .ToList() ?? [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<RegisteredProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(projects.ToList(), Options);
        var temporary = Path + ".tmp";

        File.WriteAllText(temporary, json);
        File.Move(temporary, Path, overwrite: true);
    }
}
