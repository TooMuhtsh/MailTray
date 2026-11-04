using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MailTray;

/// <summary>Paramètres et « À propos » : page web Material Design 3 affichée dans WebView2.</summary>
internal sealed class SettingsWindow : Form
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Nouvelle configuration et sessions (profils) à effacer.</summary>
    public event Action<Config, List<string>>? Saved;

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private Config _cfg;
    private string _section;
    private readonly Func<object> _states;
    private readonly Action<string> _openAccount;
    private readonly System.Windows.Forms.Timer _statesTimer = new() { Interval = 2000 };

    public SettingsWindow(Config cfg, string section, Func<object> states, Action<string> openAccount)
    {
        _cfg = cfg;
        _states = states;
        _openAccount = openAccount;
        _statesTimer.Tick += (_, _) => SendStates();
        FormClosed += (_, _) => _statesTimer.Dispose();
        _section = section;
        Text = "MailTray : paramètres";
        Icon = BrowserForm.MailIcon;
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1000;
        Height = 720;
        MinimumSize = new Size(760, 520);
        Controls.Add(_web);
        Load += async (_, _) => await InitAsync();
    }

    public void ShowSection(string section)
    {
        _section = section;
        SendInit();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    private async Task InitAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Config.Folder, "WebView2-ui"));
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Impossible d'ouvrir les paramètres.\n\n" + ex.Message, Text);
            Close();
            return;
        }
        var core = _web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.WebMessageReceived += (_, e) => OnMessage(e.WebMessageAsJson);
        core.NavigateToString(LoadHtml());
    }

    private static string LoadHtml()
    {
        using var s = typeof(SettingsWindow).Assembly.GetManifestResourceStream("MailTray.settings.html");
        using var r = new StreamReader(s!);
        return r.ReadToEnd();
    }

    private void SendInit()
    {
        if (_web.CoreWebView2 == null) return;
        var payload = new
        {
            type = "init",
            version = Application.ProductVersion,
            dataFolder = Config.Folder,
            startup = Config.StartWithWindows,
            section = _section,
            etats = _states(),
            config = _cfg
        };
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, Json));
        _statesTimer.Start();
    }

    /// <summary>État des comptes (connecté, compteurs) pour les boutons Ouvrir et Se connecter.</summary>
    private void SendStates()
    {
        if (_web.CoreWebView2 == null || !Visible) return;
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "etat", etats = _states() }, Json));
    }

    private void OnMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "ready":
                    SendInit();
                    break;
                case "open":
                    _openAccount(root.GetProperty("id").GetString() ?? "");
                    break;
                case "openData":
                    Directory.CreateDirectory(Config.Folder);
                    BrowserForm.OpenExternal(Config.Folder);
                    break;
                case "save":
                    var cfg = root.GetProperty("config").Deserialize<Config>(Json) ?? _cfg;
                    cfg.PremierLancementFait = true;
                    cfg.Compte = null; cfg.Notifications = null; cfg.CompterChat = null;
                    if (cfg.ModeIcone != Config.ModeParCompte) cfg.ModeIcone = Config.ModeUnique;
                    cfg.IntervalleSecondes = Math.Clamp(cfg.IntervalleSecondes, 5, 300);
                    bool startup = root.GetProperty("startup").GetBoolean();
                    if (startup != Config.StartWithWindows) Config.StartWithWindows = startup;
                    var del = root.GetProperty("deleteProfiles").Deserialize<List<string>>() ?? new();
                    _cfg = cfg;
                    Saved?.Invoke(cfg, del);
                    _section = "";
                    SendInit(); // la page repart de l'état enregistré, sur la même section
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Erreur dans les paramètres : " + ex.Message, Text);
        }
    }
}
