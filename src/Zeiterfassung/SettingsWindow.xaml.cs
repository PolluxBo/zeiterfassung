using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Zeiterfassung;

public partial class SettingsWindow : Window
{
    readonly Store store;

    public SettingsWindow(Store store)
    {
        InitializeComponent();
        this.store = store;
        EmailBox.Text = store.Settings.Email;
        NameBox.Text = store.Settings.Name;
        DirectBox.IsChecked = store.Settings.DirectSend;
        exportFolder = store.Settings.ExportFolder;
        FolderBox.Text = FolderChoice.ExportFolder(store.Settings);
        ClientBox.SelectedItem = ClientBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == store.Settings.MailClient) ?? ClientBox.Items[0];
        DataPath.Text = "Daten liegen in: " + Store.Folder;
        BuildKunden();
        Loaded += (_, _) => EmailBox.Focus();
    }

    void BuildKunden()
    {
        KundenPanel.Children.Clear();
        var counts = store.CountByKunde();
        var names = store.Settings.Kunden.Concat(counts.Keys).Distinct()
            .OrderBy(x => x, StringComparer.Create(new CultureInfo("de-DE"), true)).ToList();

        if (names.Count == 0)
        {
            KundenPanel.Children.Add(new TextBlock { Text = "Noch keine Kunden erfasst.", Style = (Style)FindResource("Hint") });
            return;
        }

        foreach (var name in names)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var box = new TextBox { Text = name, Style = (Style)FindResource("Field"), FontSize = 14, Padding = new Thickness(6, 5, 6, 5) };
            var count = new TextBlock
            {
                Text = $"{(counts.TryGetValue(name, out var c) ? c : 0)}×",
                Foreground = (Brush)FindResource("MutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 0),
                ToolTip = "Anzahl Einträge"
            };
            var rename = new Button { Content = "Umbenennen", Style = (Style)FindResource("Primary"), Padding = new Thickness(10, 5, 10, 5), Visibility = Visibility.Collapsed };
            var remove = new Button
            {
                Content = "Entfernen",
                Style = (Style)FindResource("Ghost"),
                Padding = new Thickness(10, 5, 10, 5),
                Visibility = store.Settings.Kunden.Contains(name) ? Visibility.Visible : Visibility.Collapsed
            };

            box.TextChanged += (_, _) =>
            {
                bool changed = box.Text.Trim().Length > 0 && box.Text.Trim() != name;
                rename.Visibility = changed ? Visibility.Visible : Visibility.Collapsed;
                remove.Visibility = !changed && store.Settings.Kunden.Contains(name) ? Visibility.Visible : Visibility.Collapsed;
            };
            box.KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter && rename.Visibility == Visibility.Visible)
                {
                    rename.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    e.Handled = true;
                }
            };
            rename.Click += (_, _) =>
            {
                var neu = box.Text.Trim();
                int n = store.RenameKunde(name, neu);
                Info.Text = $"„{name}“ heißt jetzt „{neu}“ ({n} {(n == 1 ? "Eintrag" : "Einträge")} geändert).";
                BuildKunden();
            };
            remove.Click += (_, _) =>
            {
                store.RemoveKunde(name);
                Info.Text = $"„{name}“ aus der Schnellauswahl entfernt.";
                BuildKunden();
            };

            Grid.SetColumn(count, 1);
            Grid.SetColumn(rename, 2);
            Grid.SetColumn(remove, 2);
            grid.Children.Add(box);
            grid.Children.Add(count);
            grid.Children.Add(rename);
            grid.Children.Add(remove);
            KundenPanel.Children.Add(grid);
        }
    }

    string exportFolder = "";

    void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = FolderChoice.Pick(this, FolderBox.Text);
        if (folder == null) return;
        exportFolder = folder;
        FolderBox.Text = folder;
    }

    void DefaultFolder_Click(object sender, RoutedEventArgs e)
    {
        exportFolder = "";
        FolderBox.Text = FolderChoice.DefaultExportFolder;
    }

    string SelectedClient => (ClientBox.SelectedItem as ComboBoxItem)?.Tag as string ?? MailClients.Auto;

    void ClientBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClientInfo == null) return;
        var setting = SelectedClient;
        var resolved = Mail.Resolve(setting);
        string name = resolved switch
        {
            MailClients.NewOutlook => "neues Outlook",
            MailClients.ClassicOutlook => "klassisches Outlook",
            _ => "Standard-Mailprogramm"
        };
        ClientInfo.Text = setting == MailClients.Auto ? $"Erkannt: {name}" : "";
        DirectBox.IsEnabled = resolved == MailClients.ClassicOutlook;
        if (!DirectBox.IsEnabled) DirectBox.IsChecked = false;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var email = EmailBox.Text.Trim();
        if (email.Length > 0 && !Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
        {
            MessageBox.Show(this, "Die E-Mail-Adresse sieht unvollständig aus.", "Zeiterfassung", MessageBoxButton.OK, MessageBoxImage.Information);
            EmailBox.Focus();
            return;
        }
        store.Settings.Email = email;
        store.Settings.Name = NameBox.Text.Trim();
        store.Settings.DirectSend = DirectBox.IsChecked == true;
        store.Settings.MailClient = SelectedClient;
        store.Settings.ExportFolder = exportFolder;
        store.Save();
        DialogResult = true;
    }

    void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Sicherung erstellen",
            FileName = $"Zeiterfassung_Sicherung_{DateTime.Today:yyyy-MM-dd}.json",
            InitialDirectory = KnownFolders.Downloads,
            Filter = "Sicherung (*.json)|*.json",
            DefaultExt = ".json"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            store.ExportTo(dlg.FileName);
            Info.Text = "Sicherung gespeichert: " + dlg.FileName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this,
                "Windows hat das Speichern in diesem Ordner blockiert (z. B. durch den „Überwachten Ordnerzugriff“). Bitte einen anderen Ordner wählen, z. B. Downloads.",
                "Zeiterfassung", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void Restore_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Sicherung laden",
            Filter = "Sicherung (*.json)|*.json|Alle Dateien (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            int n = store.ImportFrom(dlg.FileName);
            Info.Text = $"Sicherung geladen: {n} {(n == 1 ? "Eintrag" : "Einträge")} ergänzt.";
            if (EmailBox.Text.Trim().Length == 0) EmailBox.Text = store.Settings.Email;
            if (NameBox.Text.Trim().Length == 0) NameBox.Text = store.Settings.Name;
            BuildKunden();
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException)
        {
            MessageBox.Show(this, "Diese Datei ist keine gültige Sicherung von Zeiterfassung.", "Zeiterfassung",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
