using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Zeiterfassung;

public class Entry
{
    public string Id { get; set; } = NewId();
    public string Kunde { get; set; } = "";
    public int Min { get; set; }
    public string? Notiz { get; set; } = "";

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];
}

public class AppSettings
{
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Kunden { get; set; } = new();
    public bool DirectSend { get; set; }
    public string MailClient { get; set; } = MailClients.Auto;
}

/// <summary>Dateiformat – identisch mit der Sicherung der iPhone-App, damit Daten übernommen werden können.</summary>
public class DataFile
{
    public string App { get; set; } = "Zeiterfassung";
    public int Version { get; set; } = 1;
    public string Created { get; set; } = "";
    public AppSettings Settings { get; set; } = new();
    public Dictionary<string, List<Entry>> Days { get; set; } = new();
}

/// <summary>Speichert alle Daten als JSON unter %APPDATA%\Zeiterfassung\daten.json.</summary>
public class Store
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zeiterfassung");

    static string FilePath => Path.Combine(Folder, "daten.json");

    public DataFile Data { get; private set; } = new();
    public AppSettings Settings => Data.Settings;

    public void Load()
    {
        if (!File.Exists(FilePath)) return;
        var file = JsonSerializer.Deserialize<DataFile>(File.ReadAllText(FilePath), Json);
        if (file != null) Data = Normalize(file);
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Data, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }

    static DataFile Normalize(DataFile f)
    {
        f.Settings ??= new AppSettings();
        f.Settings.Kunden ??= new List<string>();
        f.Days ??= new Dictionary<string, List<Entry>>();
        foreach (var list in f.Days.Values)
            foreach (var e in list) e.Notiz ??= "";
        return f;
    }

    public List<Entry> Day(string key) =>
        Data.Days.TryGetValue(key, out var list) ? list : new List<Entry>();

    public void Upsert(string key, Entry entry)
    {
        if (!Data.Days.TryGetValue(key, out var list)) Data.Days[key] = list = new List<Entry>();
        int i = list.FindIndex(x => x.Id == entry.Id);
        if (i >= 0) list[i] = entry; else list.Add(entry);
        Remember(entry.Kunde);
        Save();
    }

    public void Delete(string key, string id)
    {
        if (!Data.Days.TryGetValue(key, out var list)) return;
        list.RemoveAll(x => x.Id == id);
        if (list.Count == 0) Data.Days.Remove(key);
        Save();
    }

    /// <summary>Zuletzt genutzte Kunden zuerst, maximal 60.</summary>
    void Remember(string kunde)
    {
        var list = Settings.Kunden.Where(k => !string.Equals(k, kunde, StringComparison.CurrentCultureIgnoreCase)).ToList();
        list.Insert(0, kunde);
        Settings.Kunden = list.Take(60).ToList();
    }

    public Dictionary<string, int> CountByKunde()
    {
        var c = new Dictionary<string, int>();
        foreach (var e in Data.Days.Values.SelectMany(l => l))
            c[e.Kunde] = c.TryGetValue(e.Kunde, out var n) ? n + 1 : 1;
        return c;
    }

    public int RenameKunde(string oldName, string newName)
    {
        int n = 0;
        foreach (var e in Data.Days.Values.SelectMany(l => l).Where(e => e.Kunde == oldName))
        {
            e.Kunde = newName;
            n++;
        }
        var ks = Settings.Kunden.Select(k => k == oldName ? newName : k).ToList();
        Settings.Kunden = ks.Where((k, i) => ks.FindIndex(x => string.Equals(x, k, StringComparison.CurrentCultureIgnoreCase)) == i).ToList();
        Save();
        return n;
    }

    public void RemoveKunde(string name)
    {
        Settings.Kunden.Remove(name);
        Save();
    }

    public void ExportTo(string path)
    {
        Data.Created = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
        File.WriteAllText(path, JsonSerializer.Serialize(Data, Json));
    }

    /// <summary>Lädt eine Sicherung (auch aus der iPhone-App) und ergänzt fehlende Einträge.</summary>
    public int ImportFrom(string path)
    {
        var file = JsonSerializer.Deserialize<DataFile>(File.ReadAllText(path), Json);
        if (file == null || file.App == null || !file.App.StartsWith("Zeiterfassung") || file.Days == null)
            throw new InvalidDataException("Keine Sicherung von Zeiterfassung.");
        file = Normalize(file);

        int added = 0;
        foreach (var (key, entries) in file.Days)
        {
            if (!DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) continue;
            if (!Data.Days.TryGetValue(key, out var list)) Data.Days[key] = list = new List<Entry>();
            var ids = list.Select(e => e.Id).ToHashSet();
            foreach (var e in entries.Where(e => !string.IsNullOrWhiteSpace(e.Kunde) && !ids.Contains(e.Id)))
            {
                list.Add(e);
                added++;
            }
            if (list.Count == 0) Data.Days.Remove(key);
        }

        foreach (var k in file.Settings.Kunden)
            if (!Settings.Kunden.Any(x => string.Equals(x, k, StringComparison.CurrentCultureIgnoreCase)))
                Settings.Kunden.Add(k);
        if (string.IsNullOrWhiteSpace(Settings.Email)) Settings.Email = file.Settings.Email ?? "";
        if (string.IsNullOrWhiteSpace(Settings.Name)) Settings.Name = file.Settings.Name ?? "";

        Save();
        return added;
    }
}
