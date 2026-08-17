using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Storybloq.Reader.Core.Loading;
using Storybloq.Reader.Platform.Presentation;
using Storybloq.Reader.Platform.Projects;

namespace Storybloq.Reader.App.ViewModels;

/// <summary>
/// One project in the rail, bridging a background <see cref="ProjectSession"/> to the UI thread.
/// </summary>
/// <remarks>
/// Watcher callbacks arrive on a thread-pool thread. Every observable property is therefore
/// assigned inside <see cref="DispatcherQueue.TryEnqueue(DispatcherQueueHandler)"/> — the whole
/// reason this class exists, and the one job the platform layer deliberately does not do.
/// </remarks>
public sealed partial class ProjectViewModel : ObservableObject, IDisposable
{
    private readonly ProjectSession _session;
    private readonly DispatcherQueue _dispatcher;
    private readonly ResilientFileReader _reader = new();

    [ObservableProperty]
    private ProjectPresentation _presentation;

    public ProjectViewModel(ProjectSession session, DispatcherQueue dispatcher)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _session = session;
        _dispatcher = dispatcher;
        _presentation = Build();

        _session.Updated += OnSessionChanged;
        _session.StatusChanged += OnSessionChanged;
    }

    public string Root => _session.Root;

    public string Name => Presentation.Name;

    public string StatusText => Presentation.StatusText;

    public int BadgeCount => Presentation.BadgeCount;

    /// <summary>Zero-count badges should not render, so the rail stays quiet when nothing is wrong.</summary>
    public bool HasBadge => Presentation.BadgeCount > 0;

    public void Refresh() => _session.Refresh();

    /// <summary>Reads a handover body on demand; bodies are never held in the project snapshot.</summary>
    public string ReadHandover(HandoverRow handover)
    {
        ArgumentNullException.ThrowIfNull(handover);

        var result = _reader.ReadAllText(handover.Path);
        return result.Outcome switch
        {
            FileReadOutcome.Success => result.Content ?? string.Empty,
            FileReadOutcome.Vanished => "This handover no longer exists.",
            _ => $"Could not read this handover: {result.Error?.Message}",
        };
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        // Never touch observable state from the watcher's thread.
        _dispatcher.TryEnqueue(() =>
        {
            Presentation = Build();

            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(BadgeCount));
            OnPropertyChanged(nameof(HasBadge));
        });
    }

    private ProjectPresentation Build() =>
        ProjectPresenter.Build(_session.State, _session.WatcherStatus, _session.IsWatching);

    public void Dispose()
    {
        _session.Updated -= OnSessionChanged;
        _session.StatusChanged -= OnSessionChanged;
    }
}
