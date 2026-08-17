using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Storybloq.Reader.App.ViewModels;
using Storybloq.Reader.Platform.Projects;
using Windows.Storage.Pickers;

namespace Storybloq.Reader.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        // The view model must exist before InitializeComponent: x:Bind resolves its root object
        // once, while the generated code runs. Assigning afterwards leaves every binding pointed
        // at null with nothing to notify them otherwise, and the window comes up permanently blank.
        ViewModel = new MainViewModel(new ProjectWorkspace(), DispatcherQueue.GetForCurrentThread(), PickFolderAsync);

        InitializeComponent();

        // Loading after the bindings exist lets the observable collections drive the first render.
        ViewModel.Initialise();

        SectionNav.SelectedItem = SectionNav.MenuItems.FirstOrDefault();

        Closed += (_, _) => ViewModel.Dispose();
    }

    public MainViewModel ViewModel { get; }

    /// <summary>
    /// Opens a folder picker.
    /// </summary>
    /// <remarks>
    /// Without package identity a picker has no window to attach to and throws when shown, so
    /// the window handle has to be supplied explicitly. This is the standard cost of the
    /// unpackaged deployment that lets CI publish an unsigned, runnable build.
    /// </remarks>
    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");

        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private void OnSectionSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag }
            && Enum.TryParse<Section>(tag, out var section))
        {
            ViewModel.SelectedSection = section;
        }
    }
}
