using ImageCleanup.Data.Services;
using Microsoft.UI.Xaml.Markup;

namespace ImageCleanup.App.Localization;

/// <summary>
/// XAML markup extension for static page text — `{loc:Loc Key=Some.Key}`
/// resolves through LocalizationService.Current.GetString at XAML-parse
/// time. Chosen over x:Bind-to-a-ViewModel-property-per-string specifically
/// to keep Page XAML readable (one attribute per string, not a
/// LocalizedFoo property added to every ViewModel for every label).
///
/// Trade-off, deliberate: this resolves ONCE, when the element is
/// constructed — it does not re-evaluate if the language changes later.
/// Combined with every Page using NavigationCacheMode.Enabled (constructed
/// once, reused for the app's lifetime), a language change via Settings
/// does not update already-rendered page text without an app restart. This
/// is the "acceptable to require a restart for language" case called out
/// when this was built — text bound this way and theme (which uses a
/// live-cascading WinUI mechanism, ElementTheme, with no such
/// once-only-evaluation limitation) genuinely behave differently for that
/// reason, not by oversight. Code-behind-constructed ContentDialogs (built
/// fresh on every show, not cached) call LocalizationService.Current.GetString
/// directly and DO reflect a language change immediately — see
/// SettingsViewModel.Language's setter and any Page's dialog-showing methods.
/// </summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue() => LocalizationService.Current.GetString(Key);
}
