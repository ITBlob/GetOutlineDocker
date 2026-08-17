using System.Text.Json;
using Storybloq.Reader.Core.Discovery;
using Storybloq.Reader.Core.Models;
using Storybloq.Reader.Core.Serialization;

namespace Storybloq.Reader.Core.Loading;

/// <summary>
/// Reads a <c>.story/</c> directory into an immutable <see cref="StoryProject"/>.
/// </summary>
/// <remarks>
/// Nothing here throws for bad data. Missing directories are normal (a project may never have
/// created a note), unreadable or malformed files become diagnostics, and everything that parsed
/// is still returned.
/// </remarks>
public sealed class StoryProjectLoader
{
    private readonly ResilientFileReader _reader;
    private readonly TimeProvider _timeProvider;

    public StoryProjectLoader(ResilientFileReader? reader = null, TimeProvider? timeProvider = null)
    {
        _reader = reader ?? new ResilientFileReader();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Full cold load of every section.</summary>
    public StoryProject Load(string projectRoot) =>
        Reload(StoryProject.Empty(projectRoot), StorySection.All);

    /// <summary>
    /// Re-reads only <paramref name="sections"/>, carrying everything else over from
    /// <paramref name="previous"/> untouched.
    /// </summary>
    public StoryProject Reload(StoryProject previous, StorySection sections)
    {
        ArgumentNullException.ThrowIfNull(previous);

        var root = previous.Root;

        // Keep diagnostics from sections this reload is not touching. Note HasFlag is useless
        // here: every value "has" the zero flag, so an untagged diagnostic would survive forever.
        var diagnostics = new List<LoadDiagnostic>(
            previous.Diagnostics.Where(diagnostic =>
                diagnostic.Section != StorySection.None && (diagnostic.Section & sections) == 0));

        var storyDirectory = StoryPaths.StoryDirectory(root);
        if (!Directory.Exists(storyDirectory))
        {
            return new StoryProject(
                root,
                config: null,
                roadmap: null,
                tickets: [],
                issues: [],
                notes: [],
                lessons: [],
                handovers: [],
                [LoadDiagnostic.Error($"No .story directory at {storyDirectory}.", storyDirectory, StorySection.Config)],
                _timeProvider.GetUtcNow());
        }

        var config = sections.HasFlag(StorySection.Config)
            ? LoadConfig(root, diagnostics)
            : previous.Config;

        var roadmap = sections.HasFlag(StorySection.Roadmap)
            ? LoadRoadmap(root, diagnostics)
            : previous.Roadmap;

        var tickets = sections.HasFlag(StorySection.Tickets)
            ? LoadRecords<Ticket>(StoryPaths.TicketsDirectory(root), StorySection.Tickets, diagnostics)
            : previous.Tickets;

        var issues = sections.HasFlag(StorySection.Issues)
            ? LoadRecords<Issue>(StoryPaths.IssuesDirectory(root), StorySection.Issues, diagnostics)
            : previous.Issues;

        var notes = sections.HasFlag(StorySection.Notes)
            ? LoadRecords<Note>(StoryPaths.NotesDirectory(root), StorySection.Notes, diagnostics)
            : previous.Notes;

        var lessons = sections.HasFlag(StorySection.Lessons)
            ? LoadRecords<Lesson>(StoryPaths.LessonsDirectory(root), StorySection.Lessons, diagnostics)
            : previous.Lessons;

        var handovers = sections.HasFlag(StorySection.Handovers)
            ? LoadHandovers(root, diagnostics)
            : previous.Handovers;

        return new StoryProject(
            root,
            config,
            roadmap,
            tickets,
            issues,
            notes,
            lessons,
            handovers,
            diagnostics,
            _timeProvider.GetUtcNow());
    }

    /// <summary>Reads a handover body on demand — bodies are not held in the project snapshot.</summary>
    public FileReadResult ReadHandoverContent(HandoverFile handover)
    {
        ArgumentNullException.ThrowIfNull(handover);
        return _reader.ReadAllText(handover.Path);
    }

    private StoryConfig? LoadConfig(string root, List<LoadDiagnostic> diagnostics)
    {
        var path = StoryPaths.ConfigFile(root);
        var result = _reader.ReadAllText(path);

        switch (result.Outcome)
        {
            case FileReadOutcome.Vanished:
                diagnostics.Add(LoadDiagnostic.Error("config.json is missing.", path, StorySection.Config));
                return null;

            case FileReadOutcome.Failed:
                diagnostics.Add(LoadDiagnostic.Error(
                    $"Could not read config.json: {result.Error?.Message}", path, StorySection.Config));
                return null;

            default:
                try
                {
                    var config = StoryJson.Deserialize<StoryConfig>(result.Content!);
                    if (config is null)
                    {
                        diagnostics.Add(LoadDiagnostic.Warning("config.json is empty.", path, StorySection.Config));
                    }

                    return config;
                }
                catch (JsonException exception)
                {
                    diagnostics.Add(LoadDiagnostic.Error(
                        $"config.json is not valid JSON: {exception.Message}", path, StorySection.Config));
                    return null;
                }
        }
    }

    private Roadmap? LoadRoadmap(string root, List<LoadDiagnostic> diagnostics)
    {
        var path = StoryPaths.RoadmapFile(root);
        var result = _reader.ReadAllText(path);

        switch (result.Outcome)
        {
            // A project with the roadmap feature disabled simply has no roadmap.json.
            case FileReadOutcome.Vanished:
                return null;

            case FileReadOutcome.Failed:
                diagnostics.Add(LoadDiagnostic.Error(
                    $"Could not read roadmap.json: {result.Error?.Message}", path, StorySection.Roadmap));
                return null;

            default:
                try
                {
                    return StoryJson.Deserialize<Roadmap>(result.Content!);
                }
                catch (JsonException exception)
                {
                    diagnostics.Add(LoadDiagnostic.Error(
                        $"roadmap.json is not valid JSON: {exception.Message}", path, StorySection.Roadmap));
                    return null;
                }
        }
    }

    private IReadOnlyList<T> LoadRecords<T>(string directory, StorySection section, List<LoadDiagnostic> diagnostics)
        where T : StoryRecord
    {
        string[] files;
        try
        {
            if (!Directory.Exists(directory))
            {
                // Normal: the CLI only creates a record directory once the first record exists.
                return [];
            }

            files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(LoadDiagnostic.Error(
                $"Could not list {Path.GetFileName(directory)}: {exception.Message}", directory, section));
            return [];
        }

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        var records = new List<T>(files.Length);
        foreach (var file in files)
        {
            var result = _reader.ReadAllText(file);
            if (result.Outcome == FileReadOutcome.Vanished)
            {
                // Deleted between enumeration and read. Ordinary, and not worth a diagnostic.
                continue;
            }

            if (result.Outcome == FileReadOutcome.Failed)
            {
                diagnostics.Add(LoadDiagnostic.Error(
                    $"Could not read file: {result.Error?.Message}", file, section));
                continue;
            }

            T? record;
            try
            {
                record = StoryJson.Deserialize<T>(result.Content!);
            }
            catch (JsonException exception)
            {
                diagnostics.Add(LoadDiagnostic.Error($"Invalid JSON: {exception.Message}", file, section));
                continue;
            }

            if (record is null)
            {
                diagnostics.Add(LoadDiagnostic.Warning("File is empty.", file, section));
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.Id))
            {
                diagnostics.Add(LoadDiagnostic.Warning("Record has no id and was skipped.", file, section));
                continue;
            }

            record.SourcePath = file;
            records.Add(record);
        }

        return records;
    }

    private IReadOnlyList<HandoverFile> LoadHandovers(string root, List<LoadDiagnostic> diagnostics)
    {
        var directory = StoryPaths.HandoversDirectory(root);

        string[] files;
        try
        {
            if (!Directory.Exists(directory))
            {
                return [];
            }

            files = Directory.GetFiles(directory, "*.md", SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(LoadDiagnostic.Error(
                $"Could not list handovers: {exception.Message}", directory, StorySection.Handovers));
            return [];
        }

        var handovers = files.Select(HandoverFile.Parse).ToList();
        handovers.Sort(HandoverFile.CompareNewestFirst);
        return handovers;
    }
}
