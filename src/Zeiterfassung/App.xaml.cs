using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace Zeiterfassung;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var de = new CultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = de;
        CultureInfo.DefaultThreadCurrentUICulture = de;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(de.IetfLanguageTag)));

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show("Es ist ein unerwarteter Fehler aufgetreten:\n\n" + args.Exception.Message,
                "Zeiterfassung", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);
    }
}
