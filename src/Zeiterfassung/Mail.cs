using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Zeiterfassung;

public static class MailClients
{
    public const string Auto = "auto";
    public const string NewOutlook = "neu";
    public const string ClassicOutlook = "klassisch";
    public const string Other = "anderes";
}

public static class Mail
{
    static string OlkPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "WindowsApps", "olk.exe");

    // Das neue Outlook (App-Paket) darf .eml-Dateien nur aus dem TEMP-Ordner öffnen, nicht aus %LOCALAPPDATA%Zeiterfassung.
    static string DraftFolder => Path.GetTempPath();

    public static bool HasNewOutlook => File.Exists(OlkPath);

    /// <summary>„Neues Outlook“ ist aktiv, wenn der Umschalter in Outlook gesetzt ist oder nur das neue installiert ist.</summary>
    static bool UsesNewOutlook()
    {
        if (!HasNewOutlook) return false;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Office\16.0\Outlook\Preferences");
        if (key?.GetValue("UseNewOutlook") is int v) return v == 1;
        return Type.GetTypeFromProgID("Outlook.Application") == null;
    }

    public static string Resolve(string? setting) => setting switch
    {
        MailClients.NewOutlook or MailClients.ClassicOutlook or MailClients.Other => setting,
        _ => UsesNewOutlook() ? MailClients.NewOutlook
           : Type.GetTypeFromProgID("Outlook.Application") != null ? MailClients.ClassicOutlook
           : MailClients.Other
    };

    /// <summary>Klassisches Outlook per COM: Mail mit Anhang anzeigen oder direkt senden.</summary>
    public static (bool Ok, string Error) ViaClassicOutlook(string to, string subject, string body, string attachment, bool sendDirectly)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Outlook.Application");
            if (type == null) return (false, "Das klassische Outlook ist nicht installiert.");
            dynamic app = Activator.CreateInstance(type)!;
            dynamic mail = app.CreateItem(0); // olMailItem
            mail.To = to;
            mail.Subject = subject;
            mail.Body = body;
            mail.Attachments.Add(attachment);
            if (sendDirectly) mail.Send();
            else mail.Display(false);
            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Neues Outlook: Es hat keine Programmierschnittstelle, öffnet aber eine .eml-Datei mit „X-Unsent: 1“
    /// als bearbeitbaren Entwurf – mit Empfänger, Betreff, Text und Anhang.
    /// </summary>
    public static (bool Ok, string Error) ViaNewOutlook(string to, string subject, string body, string attachment)
    {
        if (!HasNewOutlook) return (false, "Das neue Outlook ist nicht installiert.");
        try
        {
            foreach (var old in Directory.EnumerateFiles(DraftFolder, "Zeiterfassung_*.eml"))
                if (File.GetLastWriteTime(old) < DateTime.Now.AddDays(-2)) TryDelete(old);

            var eml = Path.Combine(DraftFolder, Path.GetFileNameWithoutExtension(attachment) + $"_{DateTime.Now:HHmmss}.eml");
            File.WriteAllText(eml, BuildEml(to, subject, body, attachment), new UTF8Encoding(false));
            Process.Start(new ProcessStartInfo(OlkPath, $"\"{eml}\"") { UseShellExecute = true });
            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    static string BuildEml(string to, string subject, string body, string attachment)
    {
        var boundary = "ZE-" + Guid.NewGuid().ToString("N");
        var name = Path.GetFileName(attachment);
        var sb = new StringBuilder();
        sb.Append("X-Unsent: 1\r\n");
        sb.Append($"To: {to}\r\n");
        sb.Append($"Subject: {EncodeHeader(subject)}\r\n");
        sb.Append($"Date: {DateTime.UtcNow:R}\r\n");
        sb.Append("MIME-Version: 1.0\r\n");
        sb.Append($"Content-Type: multipart/mixed; boundary=\"{boundary}\"\r\n\r\n");

        sb.Append($"--{boundary}\r\n");
        sb.Append("Content-Type: text/plain; charset=utf-8\r\n");
        sb.Append("Content-Transfer-Encoding: base64\r\n\r\n");
        sb.Append(Base64Lines(Encoding.UTF8.GetBytes(body)));

        sb.Append($"--{boundary}\r\n");
        sb.Append($"Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet; name=\"{name}\"\r\n");
        sb.Append($"Content-Disposition: attachment; filename=\"{name}\"\r\n");
        sb.Append("Content-Transfer-Encoding: base64\r\n\r\n");
        sb.Append(Base64Lines(File.ReadAllBytes(attachment)));

        sb.Append($"--{boundary}--\r\n");
        return sb.ToString();
    }

    static string EncodeHeader(string s) => "=?utf-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(s)) + "?=";

    static string Base64Lines(byte[] data) =>
        Convert.ToBase64String(data, Base64FormattingOptions.InsertLineBreaks) + "\r\n";

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* wird beim nächsten Mal erneut versucht */ }
    }

    /// <summary>Anderes Mailprogramm: mailto öffnen und Datei im Explorer markieren (Anhang muss man selbst hineinziehen).</summary>
    public static void Fallback(string to, string subject, string body, string attachment)
    {
        var uri = $"mailto:{Uri.EscapeDataString(to)}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { /* kein Mailprogramm */ }
        ShowInExplorer(attachment);
    }

    public static void ShowInExplorer(string path) =>
        Process.Start("explorer.exe", $"/select,\"{path}\"");
}
