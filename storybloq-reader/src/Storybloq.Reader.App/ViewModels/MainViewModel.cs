using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Storybloq.Reader.Platform.Presentation;
using Storybloq.Reader.Platform.Projects;

namespace Storybloq.Reader.App.ViewModels;

public enum Section
{
    Overview,
    Tickets,
    Issues,
    Handovers,
    Knowledge,
    Attention,
}

/// <summary>
/// The shell: the list of projects, the selected section, and the cross-project attention view.
/// </summary>
/// <remarks>
/// Section visibility is exposed as <see cref="Visibility"/> properties rather than booleans fed
/// through value converters. It puts a UI type on the view model, but it removes an entire class
/// of resource-lookup failures from XAML that no test on this repository could catch.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ProjectWorkspace _workspace;
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<Task<string?>> _pickFolder;

    [ObservableProperty]
    private ProjectViewModel? _selectedProject;

    [ObservableProperty]
    private Section _selectedSection = Section.Overview;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private HandoverRow? _selectedHandover;

    [ObservableProperty]
    private string _handoverContent = string.Empty;

    public MainViewModel(ProjectWorkspace workspace, DispatcherQueue dispatcher, Func<Task<string?>> pickFolder)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(pickFolder);

        _workspace = workspace;
        _dispatcher = dispatcher;
        _pickFolder = pickFolder;

        _workspace.ProjectUpdated += OnWorkspaceProjectUpdated;
    }

    public ObservableCollection<ProjectViewModel> Projects { get; } = [];

    public ObservableCollection<AttentionRow> Attention { get; } = [];

    public bool HasProjects => Projects.Count > 0;

    public Visibility EmptyStateVisibility => Projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ContentVisibility => Projects.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    public Visibility OverviewVisibility => VisibleWhen(Section.Overview);

    public Visibility TicketsVisibility => VisibleWhen(Section.Tickets);

    public Visibility IssuesVisibility => VisibleWhen(Section.Issues);

    public Visibility HandoversVisibility => VisibleWhen(Section.Handovers);

    public Visibility KnowledgeVisibility => VisibleWhen(Section.Knowledge);

    public Visibility AttentionVisibility => VisibleWhen(Section.Attention);

    public Visibility DiagnosticsVisibility =>
        SelectedProject?.Presentation.Diagnostics.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Restores saved projects at startup.</summary>
    public void Initialise()
    {
        _workspace.LoadSaved();
        SyncProjects();
        SelectedProject = Projects.FirstOrDefault();
        RefreshAttention();
    }

    [RelayCommand]
    private async Task AddProjectAsync()
    {
        var folder = await _pickFolder().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        var result = _workspace.Add(folder);
        StatusMessage = result.Outcome switch
        {
            AddProjectOutcome.Added => null,
            AddProjectOutcome.AlreadyPresent => "That project is already open.",
            _ => result.Message,
        };

        SyncProjects();
        RefreshAttention();

        if (result.Session is not null)
        {
            SelectedProject = Projects.FirstOrDefault(project => PathsEqual(project.Root, result.Session.Root));
        }
    }

    [RelayCommand]
    private void RemoveProject(ProjectViewModel? project)
    {
        if (project is null)
        {
            return;
        }

        _workspace.Remove(project.Root);
        SyncProjects();
        SelectedProject = Projects.FirstOrDefault();
        RefreshAttention();
    }

    [RelayCommand]
    private void Refresh() => SelectedProject?.Refresh();

    partial void OnSelectedProjectChanged(ProjectViewModel? value)
    {
        if (value is not null)
        {
            // Selecting a project promotes it: an unwatched project starts watching again and
            // re-reads, so what the user is looking at is never the stale copy.
            _workspace.Touch(value.Root);
        }

        SelectedHandover = null;
        HandoverContent = string.Empty;
        NotifySectionsChanged();
    }

    partial void OnSelectedSectionChanged(Section value) => NotifySectionsChanged();

    partial void OnSelectedHandoverChanged(HandoverRow? value) =>
        HandoverContent = value is null || SelectedProject is null
            ? string.Empty
            : SelectedProject.ReadHandover(value);

    private void OnWorkspaceProjectUpdated(object? sender, ProjectSessionUpdatedEventArgs e) =>
        _dispatcher.TryEnqueue(() =>
        {
            RefreshAttention();
            OnPropertyChanged(nameof(DiagnosticsVisibility));
        });

    private void SyncProjects()
    {
        var sessions = _workspace.Sessions;

        foreach (var stale in Projects.Where(project =>
            !sessions.Any(session => PathsEqual(session.Root, project.Root))).ToList())
        {
            Projects.Remove(stale);
            stale.Dispose();
        }

        foreach (var session in sessions)
        {
            if (!Projects.Any(project => PathsEqual(project.Root, session.Root)))
            {
                Projects.Add(new ProjectViewModel(session, _dispatcher));
            }
        }

        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(EmptyStateVisibility));
        OnPropertyChanged(nameof(ContentVisibility));
    }

    private void RefreshAttention()
    {
        Attention.Clear();
        foreach (var row in ProjectPresenter.BuildAttention(_workspace.Attention))
        {
            Attention.Add(row);
        }
    }

    private void NotifySectionsChanged()
    {
        OnPropertyChanged(nameof(OverviewVisibility));
        OnPropertyChanged(nameof(TicketsVisibility));
        OnPropertyChanged(nameof(IssuesVisibility));
        OnPropertyChanged(nameof(HandoversVisibility));
        OnPropertyChanged(nameof(KnowledgeVisibility));
        OnPropertyChanged(nameof(AttentionVisibility));
        OnPropertyChanged(nameof(DiagnosticsVisibility));
    }

    private Visibility VisibleWhen(Section section) =>
        SelectedSection == section ? Visibility.Visible : Visibility.Collapsed;

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _workspace.ProjectUpdated -= OnWorkspaceProjectUpdated;

        foreach (var project in Projects)
        {
            project.Dispose();
        }

        Projects.Clear();
        _workspace.Dispose();
    }
}
