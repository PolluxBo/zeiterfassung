using System.IO;
using System.Windows;

namespace Zeiterfassung;

public static class FolderChoice
{
    public static string DefaultExportFolder => Path.Combine(KnownFolders.Downloads, "Zeiterfassung");

    public static string ExportFolder(AppSettings s) =>
        string.IsNullOrWhiteSpace(s.ExportFolder) ? DefaultExportFolder : s.ExportFolder;

    /// <summary>Ordner auswählen und prüfen, ob die App dort schreiben darf. Gibt null zurück, wenn abgebrochen oder blockiert.</summary>
    public static string? Pick(Window owner, string current)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Ablageordner für die Excel-Auswertungen wählen",
            InitialDirectory = Directory.Exists(current) ? current : KnownFolders.Downloads
        };
        if (dlg.ShowDialog(owner) != true) return null;

        var folder = dlg.FolderName;
        if (CanWrite(folder)) return folder;

        MessageBox.Show(owner,
            $"Die Zeiterfassung darf im Ordner\n{folder}\nnicht speichern.\n\n" +
            "Meist ist das der „Überwachte Ordnerzugriff“ von Windows-Sicherheit, der z. B. „Dokumente“, „Desktop“ und „Bilder“ schützt. " +
            "Bitte einen anderen Ordner wählen oder die App unter Windows-Sicherheit → Viren- & Bedrohungsschutz → Ransomware-Schutz → " +
            "„App durch überwachten Ordnerzugriff zulassen“ freigeben und es dann erneut versuchen.",
            "Zeiterfassung", MessageBoxButton.OK, MessageBoxImage.Warning);
        return null;
    }

    static bool CanWrite(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".zeiterfassung_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
