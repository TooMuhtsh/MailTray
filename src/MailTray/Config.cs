using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace MailTray;

/// <summary>Réglages d'un compte Google.</summary>
internal sealed class CompteConfig
{
    public string Nom { get; set; } = "Compte";
    public int Index { get; set; }                       // u/N dans la session du compte (0 en général)
    public string Profil { get; set; } = "";           // session isolée du compte ("" = session par défaut)

    public static string NewProfile() => "compte-" + Guid.NewGuid().ToString("N")[..8];
    public bool Actif { get; set; } = true;
    public bool DansBadge { get; set; } = true;          // compté dans l'icône unique
    public bool CompterChat { get; set; } = true;
    public bool Notifications { get; set; } = true;

    public CompteConfig Clone() => (CompteConfig)MemberwiseClone();
}

/// <summary>Réglages stockés dans %APPDATA%\MailTray\config.json.</summary>
internal sealed class Config
{
    public const string ModeUnique = "unique";
    public const string ModeParCompte = "parCompte";

    public List<CompteConfig> Comptes { get; set; } = new();
    public string ModeIcone { get; set; } = ModeUnique;
    public int IntervalleSecondes { get; set; } = 10;    // relève de secours du flux des mails
    public bool PremierLancementFait { get; set; }

    // Anciens réglages (0.1 à 0.3), repris une fois puis effacés.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Compte { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Notifications { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? CompterChat { get; set; }

    /// <summary>Dossier des données. Peut rester l'ancien dossier GmailTray si la migration a échoué.</summary>
    public static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MailTray");

    public static string FilePath => Path.Combine(Folder, "config.json");

    public static Config Load()
    {
        Config c;
        try
        {
            c = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new Config()
                : new Config();
        }
        catch { c = new Config(); }

        bool changed = false;
        if (c.Comptes.Count == 0)
        {
            c.Comptes.Add(new CompteConfig
            {
                Nom = "Compte principal",
                Index = c.Compte ?? 0,
                Notifications = c.Notifications ?? true,
                CompterChat = c.CompterChat ?? true
            });
            changed = true;
        }
        if (c.Compte != null || c.Notifications != null || c.CompterChat != null)
        {
            c.Compte = null; c.Notifications = null; c.CompterChat = null;
            changed = true;
        }
        // 0.5 : une session isolée par compte. Le premier garde la session existante.
        var used = new HashSet<string>();
        for (int i = 0; i < c.Comptes.Count; i++)
        {
            var a = c.Comptes[i];
            if (i > 0 && a.Profil == "") { a.Profil = CompteConfig.NewProfile(); a.Index = 0; changed = true; }
            if (!used.Add(a.Profil)) { a.Profil = CompteConfig.NewProfile(); a.Index = 0; changed = true; used.Add(a.Profil); }
        }
        if (c.IntervalleSecondes == 30) { c.IntervalleSecondes = 10; changed = true; } // ancien défaut
        if (c.ModeIcone != ModeParCompte) c.ModeIcone = ModeUnique;
        if (changed) c.Save();
        return c;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    // Lancement avec Windows : clé Run de l'utilisateur courant.
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "MailTray";

    public static bool StartWithWindows
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                return k?.GetValue(RunName) is string s && s.Contains(Environment.ProcessPath ?? "", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
        set
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value) k.SetValue(RunName, $"\"{Environment.ProcessPath}\" --tray");
                else k.DeleteValue(RunName, false);
            }
            catch { }
        }
    }
}
