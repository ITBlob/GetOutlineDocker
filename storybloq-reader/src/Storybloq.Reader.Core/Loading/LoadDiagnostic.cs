namespace Storybloq.Reader.Core.Loading;

public enum DiagnosticSeverity
{
    Warning,
    Error,
}

/// <summary>
/// A problem encountered while loading, attached to the project rather than thrown.
/// </summary>
/// <remarks>
/// One corrupt ticket must never cost the user their whole board. Every failure that can be
/// isolated to a single file is isolated to that file and surfaced here, so the reader shows
/// everything it could parse plus an honest account of what it could not.
///
/// The <see cref="Section"/> tag lets a partial reload replace only the diagnostics belonging to
/// the sections it re-read, instead of resurrecting stale complaints about files that have since
/// been fixed.
/// </remarks>
public sealed record LoadDiagnostic(
    DiagnosticSeverity Severity,
    string Message,
    string? Path = null,
    StorySection Section = StorySection.None)
{
    public static LoadDiagnostic Error(string message, string? path = null, StorySection section = StorySection.None) =>
        new(DiagnosticSeverity.Error, message, path, section);

    public static LoadDiagnostic Warning(string message, string? path = null, StorySection section = StorySection.None) =>
        new(DiagnosticSeverity.Warning, message, path, section);

    public string FileName => Path is null ? string.Empty : System.IO.Path.GetFileName(Path);

    public override string ToString() =>
        Path is null ? Message : $"{System.IO.Path.GetFileName(Path)}: {Message}";
}
