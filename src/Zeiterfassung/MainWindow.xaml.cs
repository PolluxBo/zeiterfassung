using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Zeiterfassung;

public record DayVm(string Key, string Wd, string DayNum, string Sum, bool IsSelected, bool IsToday);

public record EntryVm(string Key, string Id, string Kunde, string Time, string Notiz, bool IsEditing)
{
    public bool HasNotiz => Notiz.Length > 0;
    public bool NotizEmpty => Notiz.Length == 0;
    public string NotizText => HasNotiz ? Notiz : "keine Tätigkeit angegeben";
}

public record WeekDayVm(string Key, string Title, string Sum, List<EntryVm> Entries, bool IsEmpty, bool IsSelected);

public record PerKVm(string Kunde, string Time);

public partial class MainWindow : Window
{
    static readonly int[] Presets = { 15, 30, 60, 90, 120, 240, 480 };

    readonly Store store = new();
    readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    DateTime sel;
    DateTime monday;
    int dur = 60;
    string? editId;

    // Standard ist DownloadsZeiterfassung (nicht „Dokumente“: dort blockiert der Überwachte Ordnerzugriff unsignierte Programme).
    string ExportFolder => FolderChoice.ExportFolder(store.Settings);

    public MainWindow()
    {
        InitializeComponent();
        try { store.Load(); }
        catch (Exception ex)
        {
            MessageBox.Show("Die gespeicherten Daten konnten nicht gelesen werden:\n" + ex.Message,
                "Zeiterfassung", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        sel = D.LastWorkday(DateTime.Today);
        monday = D.Monday(sel);
        statusTimer.Tick += (_, _) => { StatusText.Text = ""; statusTimer.Stop(); };
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Zeiterfassung · Version {v?.Major}.{v?.Minor}.{v?.Build}";

        SetDur(60);
        Refresh();
        Loaded += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(store.Settings.Email)) OpenSettings();
            KundeBox.Focus();
        };
        Activated += (_, _) => RefreshWeekBar();
    }

    // ---------- Anzeige ----------

    List<string> WeekKeys() => Enumerable.Range(0, 5).Select(i => D.Key(monday.AddDays(i))).ToList();

    static int Sum(IEnumerable<Entry> es) => es.Sum(e => e.Min);

    EntryVm ToVm(string key, Entry e) =>
        new(key, e.Id, e.Kunde, D.Hm(e.Min) + " h", e.Notiz ?? "", e.Id == editId);

    void Refresh()
    {
        RefreshWeekBar();
        var keys = WeekKeys();
        string selKey = D.Key(sel);

        // Tagesübersicht
        var es = store.Day(selKey);
        DayTitle.Text = D.Long(sel);
        FormDay.Text = "für " + D.Short(sel);
        DayEntries.ItemsSource = es.Select(e => ToVm(selKey, e)).ToList();
        DayEmpty.Visibility = es.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EditHint.Visibility = es.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DayTotal.Text = D.Hm(Sum(es)) + " h";
        SetPerK(DayPerKPanel, DayPerK, es);

        // Wochenübersicht
        WeekKw.Text = $"KW {D.Kw(monday)}";
        WeekList.ItemsSource = keys.Select(k =>
        {
            var d = D.FromKey(k);
            var list = store.Day(k);
            return new WeekDayVm(k, $"{D.WdLong(d)}, {D.Dm(d)}", D.Hm(Sum(list)) + " h",
                list.Select(e => ToVm(k, e)).ToList(), list.Count == 0, k == selKey);
        }).ToList();
        var weekEntries = keys.SelectMany(k => store.Day(k)).ToList();
        WeekTotal2.Text = D.Hm(Sum(weekEntries)) + " h";
        SetPerK(WeekPerKPanel, WeekPerK, weekEntries);

        RefreshKunden();
        RefreshSend();
    }

    void RefreshWeekBar()
    {
        string selKey = D.Key(sel), today = D.Key(DateTime.Today);
        var keys = WeekKeys();
        KwLabel.Content = $"KW {D.Kw(monday)} · {D.Dm(monday)}–{D.Dmy(monday.AddDays(4))}";
        DaysList.ItemsSource = keys.Select(k =>
        {
            var d = D.FromKey(k);
            int m = Sum(store.Day(k));
            return new DayVm(k, D.WdShort(d).ToUpperInvariant(), d.Day + ".", m > 0 ? D.Hm(m) : "–", k == selKey, k == today);
        }).ToList();
        WeekTotal.Text = D.Hm(keys.Sum(k => Sum(store.Day(k)))) + " h";
    }

    static void SetPerK(FrameworkElement panel, ItemsControl list, List<Entry> es)
    {
        var groups = es.GroupBy(e => e.Kunde)
            .Select(g => (Kunde: g.Key, Min: g.Sum(e => e.Min)))
            .OrderByDescending(x => x.Min)
            .ToList();
        panel.Visibility = groups.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        list.ItemsSource = groups.Select(g => new PerKVm(g.Kunde, D.Hm(g.Min) + " h")).ToList();
    }

    void RefreshKunden()
    {
        var text = KundeBox.Text;
        KundeBox.ItemsSource = store.Settings.Kunden.ToList();
        KundeBox.Text = text;

        KundenChips.Children.Clear();
        foreach (var k in store.Settings.Kunden.Take(8))
        {
            var b = new Button { Content = k, Style = (Style)FindResource("Chip") };
            b.Click += (_, _) => { KundeBox.Text = k; NotizBox.Focus(); };
            KundenChips.Children.Add(b);
        }
    }

    void RefreshSend()
    {
        var (keys, _, _, _) = CurrentRange();
        var es = keys.SelectMany(k => store.Day(k)).ToList();
        RangeInfo.Text = $"{es.Count} {(es.Count == 1 ? "Eintrag" : "Einträge")} · {D.Hm(Sum(es))} h";
        bool hasMail = !string.IsNullOrWhiteSpace(store.Settings.Email);
        AddrText.Text = hasMail ? store.Settings.Email : "keine Adresse hinterlegt (Einstellungen)";
        SendBtn.IsEnabled = es.Count > 0;
        SaveXlsxBtn.IsEnabled = es.Count > 0;
        FolderText.Text = ExportFolder;
        var client = Mail.Resolve(store.Settings.MailClient);
        SendBtn.Content = client == MailClients.Other ? "Per E-Mail senden" : "Per Outlook senden";
        SendHint.Text = client switch
        {
            MailClients.ClassicOutlook when store.Settings.DirectSend => "Die Mail wird ohne Vorschau direkt über das klassische Outlook gesendet.",
            MailClients.ClassicOutlook => "Das klassische Outlook öffnet eine fertige Mail mit Empfänger, Betreff und Excel-Anhang. Du klickst nur noch auf Senden.",
            MailClients.NewOutlook => "Das neue Outlook öffnet einen fertigen Entwurf mit Empfänger, Betreff und Excel-Anhang. Du klickst nur noch auf Senden.",
            _ => "Dein Mailprogramm öffnet eine Mail mit Empfänger und Betreff. Die Excel-Datei wird im Explorer markiert und muss in die Mail gezogen werden."
        };
    }

    void SetDur(int m)
    {
        dur = Math.Clamp(m, 15, 24 * 60);
        DurText.Text = D.Hm(dur) + " h";
        DurChips.Children.Clear();
        foreach (var p in Presets)
        {
            var b = new Button { Content = D.Hm(p), Style = (Style)FindResource(p == dur ? "ChipOn" : "Chip"), FontFamily = (System.Windows.Media.FontFamily)FindResource("NumFont") };
            b.Click += (_, _) => SetDur(p);
            DurChips.Children.Add(b);
        }
    }

    void ShowStatus(string msg)
    {
        StatusText.Text = msg;
        statusTimer.Stop();
        statusTimer.Start();
    }

    // ---------- Navigation ----------

    void Day_Click(object sender, RoutedEventArgs e)
    {
        sel = D.FromKey(((DayVm)((FrameworkElement)sender).DataContext).Key);
        CancelEdit();
        Refresh();
    }

    void ShiftWeek(int weeks)
    {
        int idx = (sel - D.Monday(sel)).Days;
        monday = monday.AddDays(7 * weeks);
        sel = monday.AddDays(idx);
        CancelEdit();
        Refresh();
    }

    void PrevWeek_Click(object sender, RoutedEventArgs e) => ShiftWeek(-1);
    void NextWeek_Click(object sender, RoutedEventArgs e) => ShiftWeek(1);

    void Today_Click(object sender, RoutedEventArgs e)
    {
        sel = D.LastWorkday(DateTime.Today);
        monday = D.Monday(sel);
        CancelEdit();
        Refresh();
    }

    void WeekDay_Click(object sender, RoutedEventArgs e)
    {
        sel = D.FromKey(((WeekDayVm)((FrameworkElement)sender).DataContext).Key);
        CancelEdit();
        Refresh();
        DayCard.BringIntoView();
    }

    void WeekEntry_Click(object sender, RoutedEventArgs e)
    {
        var vm = (EntryVm)((FrameworkElement)sender).DataContext;
        sel = D.FromKey(vm.Key);
        StartEdit(vm.Key, vm.Id);
    }

    void EditEntry_Click(object sender, RoutedEventArgs e)
    {
        var vm = (EntryVm)((FrameworkElement)sender).DataContext;
        StartEdit(vm.Key, vm.Id);
    }

    // ---------- Erfassen / Ändern ----------

    void StartEdit(string key, string id)
    {
        var entry = store.Day(key).FirstOrDefault(x => x.Id == id);
        if (entry == null) return;
        editId = id;
        KundeBox.Text = entry.Kunde;
        NotizBox.Text = entry.Notiz ?? "";
        SetDur(entry.Min);
        FormTitle.Text = "EINTRAG BEARBEITEN";
        SaveBtn.Content = "Änderung speichern";
        CancelBtn.Visibility = DeleteBtn.Visibility = Visibility.Visible;
        Refresh();
        KundeBox.Focus();
    }

    void CancelEdit()
    {
        editId = null;
        KundeBox.Text = "";
        NotizBox.Text = "";
        SetDur(60);
        FormTitle.Text = "NEUER EINTRAG";
        SaveBtn.Content = "Eintrag speichern";
        CancelBtn.Visibility = DeleteBtn.Visibility = Visibility.Collapsed;
    }

    void Minus_Click(object sender, RoutedEventArgs e) => SetDur(dur - 15);
    void Plus_Click(object sender, RoutedEventArgs e) => SetDur(dur + 15);

    void Save_Click(object sender, RoutedEventArgs e) => SaveEntry();

    void SaveEntry()
    {
        var kunde = KundeBox.Text.Trim();
        if (kunde.Length == 0)
        {
            MessageBox.Show(this, "Bitte einen Kunden eintragen.", "Zeiterfassung", MessageBoxButton.OK, MessageBoxImage.Information);
            KundeBox.Focus();
            return;
        }
        bool wasEdit = editId != null;
        try
        {
            store.Upsert(D.Key(sel), new Entry { Id = editId ?? Entry.NewId(), Kunde = kunde, Min = dur, Notiz = NotizBox.Text.Trim() });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Speichern fehlgeschlagen:\n" + ex.Message, "Zeiterfassung", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        CancelEdit();
        Refresh();
        ShowStatus(wasEdit ? "Eintrag geändert" : "Eintrag gespeichert");
        KundeBox.Focus();
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelEdit();
        Refresh();
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (editId == null) return;
        var r = MessageBox.Show(this, "Diesen Eintrag wirklich löschen?", "Zeiterfassung", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (r != MessageBoxResult.Yes) return;
        store.Delete(D.Key(sel), editId);
        CancelEdit();
        Refresh();
        ShowStatus("Eintrag gelöscht");
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SaveEntry();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && editId != null)
        {
            CancelEdit();
            Refresh();
            e.Handled = true;
        }
    }

    // ---------- Auswertung ----------

    (List<string> Keys, string Period, string FileName, string Subject) CurrentRange()
    {
        if (RangeWeek?.IsChecked == true)
        {
            int kw = D.Kw(monday);
            string period = $"KW {kw}: {D.Dmy(monday)} – {D.Dmy(monday.AddDays(4))}";
            return (WeekKeys(), period, $"Zeiterfassung_KW{kw:00}_{D.KwYear(monday)}.xlsx", $"Zeiterfassung – {period}");
        }
        return (new List<string> { D.Key(sel) }, D.Long(sel), $"Zeiterfassung_{D.Key(sel)}.xlsx", $"Zeiterfassung – {D.Long(sel)}");
    }

    void Range_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) RefreshSend();
    }

    string MailBody(List<string> keys, string period)
    {
        var lines = new List<string> { "Hallo,", "", $"anbei die Zeiterfassung für {period}.", "" };
        int total = 0;
        foreach (var k in keys)
        {
            var es = store.Day(k);
            if (es.Count == 0) continue;
            lines.Add(D.Short(D.FromKey(k)));
            foreach (var e in es)
            {
                total += e.Min;
                lines.Add($"  • {e.Kunde}: {D.Hm(e.Min)} h" + (string.IsNullOrEmpty(e.Notiz) ? "" : $" – {e.Notiz}"));
            }
        }
        lines.Add("");
        lines.Add($"Summe: {D.Hm(total)} h");
        lines.Add("");
        lines.Add("Viele Grüße");
        if (!string.IsNullOrWhiteSpace(store.Settings.Name)) lines.Add(store.Settings.Name);
        return string.Join("\r\n", lines);
    }

    string? WriteExcel(string path)
    {
        var (keys, period, _, _) = CurrentRange();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ExcelExport.Write(path, keys, store, period);
            return path;
        }
        catch (IOException ex) when (IsFileLocked(ex))
        {
            MessageBox.Show(this, $"Die Datei „{Path.GetFileName(path)}“ ist noch geöffnet (z. B. in Excel). Bitte schließen und erneut versuchen.",
                "Zeiterfassung", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowBlocked(Path.GetDirectoryName(path)!);
            return null;
        }
    }

    // 0x80070020 = Freigabeverletzung, 0x80070021 = Sperrverletzung
    static bool IsFileLocked(IOException ex) => (ex.HResult & 0xFFFF) is 0x20 or 0x21;

    void ShowBlocked(string folder) =>
        MessageBox.Show(this,
            $"Windows hat das Speichern im Ordner\n{folder}\nblockiert.\n\n" +
            "Meist ist das der „Überwachte Ordnerzugriff“ von Windows-Sicherheit, der z. B. „Dokumente“ und „Desktop“ schützt. " +
            "Bitte einen anderen Ordner wählen (z. B. Downloads) oder Zeiterfassung unter Windows-Sicherheit → Viren- & Bedrohungsschutz → " +
            "Ransomware-Schutz → „App durch überwachten Ordnerzugriff zulassen“ freigeben.",
            "Zeiterfassung", MessageBoxButton.OK, MessageBoxImage.Warning);

    bool EnsureExportFolder()
    {
        try
        {
            Directory.CreateDirectory(ExportFolder);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowBlocked(ExportFolder);
            return false;
        }
    }

    void Send_Click(object sender, RoutedEventArgs e)
    {
        var email = store.Settings.Email.Trim();
        if (email.Length == 0)
        {
            MessageBox.Show(this, "Bitte zuerst in den Einstellungen die Empfänger-Adresse eintragen.", "Zeiterfassung",
                MessageBoxButton.OK, MessageBoxImage.Information);
            OpenSettings();
            return;
        }

        var (keys, period, fileName, subject) = CurrentRange();
        var path = WriteExcel(Path.Combine(ExportFolder, fileName));
        if (path == null) return;

        var body = MailBody(keys, period);
        var client = Mail.Resolve(store.Settings.MailClient);
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            if (client == MailClients.ClassicOutlook)
            {
                var (ok, error) = Mail.ViaClassicOutlook(email, subject, body, path, store.Settings.DirectSend);
                if (ok)
                {
                    ShowStatus(store.Settings.DirectSend ? $"Gesendet an {email}" : "Mail im klassischen Outlook geöffnet");
                    return;
                }
                // Klassisches Outlook ist auf das neue umgestellt oder startet nicht – dann das neue versuchen.
                if (Mail.HasNewOutlook && Mail.ViaNewOutlook(email, subject, body, path).Ok)
                {
                    ShowStatus("Entwurf im neuen Outlook geöffnet");
                    return;
                }
                FallbackWithNotice(email, subject, body, path, "Das klassische Outlook konnte die Mail nicht erstellen.\n" + error);
                return;
            }

            if (client == MailClients.NewOutlook)
            {
                var (ok, error) = Mail.ViaNewOutlook(email, subject, body, path);
                if (ok)
                {
                    ShowStatus("Entwurf im neuen Outlook geöffnet – bitte auf Senden klicken");
                    return;
                }
                FallbackWithNotice(email, subject, body, path, "Das neue Outlook konnte nicht gestartet werden.\n" + error);
                return;
            }

            Mail.Fallback(email, subject, body, path);
            ShowStatus("Mailprogramm geöffnet – Excel-Datei bitte in die Mail ziehen");
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    void FallbackWithNotice(string email, string subject, string body, string path, string reason)
    {
        Mail.Fallback(email, subject, body, path);
        MessageBox.Show(this,
            reason + "\n\nDas Standard-Mailprogramm wurde geöffnet und die Excel-Datei im Explorer markiert. " +
            "Bitte die Datei in die Mail ziehen.\n\nUnter Einstellungen → Mailprogramm kannst du festlegen, welches Outlook verwendet wird.",
            "Zeiterfassung", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    void SaveXlsx_Click(object sender, RoutedEventArgs e)
    {
        var (_, _, fileName, _) = CurrentRange();
        EnsureExportFolder();
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Excel-Datei speichern",
            FileName = fileName,
            InitialDirectory = ExportFolder,
            Filter = "Excel-Arbeitsmappe (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx"
        };
        if (dlg.ShowDialog(this) != true) return;
        var path = WriteExcel(dlg.FileName);
        if (path == null) return;
        ShowStatus("Excel-Datei gespeichert");
        Mail.ShowInExplorer(path);
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureExportFolder()) return;
        Process.Start("explorer.exe", $"\"{ExportFolder}\"");
    }

    void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = FolderChoice.Pick(this, ExportFolder);
        if (folder == null) return;
        store.Settings.ExportFolder = folder;
        store.Save();
        RefreshSend();
        ShowStatus("Ablageordner geändert");
    }

    // ---------- Einstellungen ----------

    void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    void OpenSettings()
    {
        new SettingsWindow(store) { Owner = this }.ShowDialog();
        Refresh();
    }
}
