using System.Diagnostics;

namespace Zeiterfassung;

public static class Mail
{
    /// <summary>Erstellt eine Outlook-Mail mit Anhang und zeigt sie an (oder sendet direkt).</summary>
    public static (bool Ok, string Error) ViaOutlook(string to, string subject, string body, string attachment, bool sendDirectly)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Outlook.Application");
            if (type == null) return (false, "Outlook ist nicht installiert.");
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

    /// <summary>Ersatzweg ohne Outlook: Standard-Mailprogramm öffnen und Datei im Explorer markieren.</summary>
    public static void Fallback(string to, string subject, string body, string attachment)
    {
        var uri = $"mailto:{Uri.EscapeDataString(to)}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { /* kein Mailprogramm */ }
        ShowInExplorer(attachment);
    }

    public static void ShowInExplorer(string path) =>
        Process.Start("explorer.exe", $"/select,\"{path}\"");
}
