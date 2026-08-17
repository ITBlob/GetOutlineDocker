using Microsoft.UI.Xaml;

namespace Storybloq.Reader.App;

/// <summary>
/// Helpers callable from <c>x:Bind</c> function bindings.
/// </summary>
/// <remarks>
/// The presentation rows deliberately live in a framework-neutral assembly so they can be unit
/// tested, which means they cannot expose <see cref="Visibility"/> themselves. Converting here
/// keeps that separation without registering value converters as XAML resources — and, unlike
/// code-behind, this file compiles without the XAML-generated partials, so it is covered by the
/// same type check as the view models.
/// </remarks>
public static class XamlHelpers
{
    public static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Hide(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility ShowText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
}
