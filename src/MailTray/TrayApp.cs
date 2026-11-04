using Microsoft.Web.WebView2.Core;

namespace MailTray;

/// <summary>Cœur de l'app : comptes, icônes du tray, notifications.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private Config _cfg = Config.Load();
    private CoreWebView2Environment? _env;
    private readonly List<AccountWatcher> _watchers = new();
    private readonly List<TrayIcon> _icons = new();
    private readonly System.Windows.Forms.Timer _poll = new();
    private readonly Control _ui = new();   // pour revenir sur le thread de l'interface
    private SettingsWindow? _settings;
    private bool _firstBuild = true;

    /// <summary>Une icône du tray et les comptes qu'elle additionne.</summary>
    private sealed class TrayIcon
    {
        public NotifyIcon Icon = new();
        public List<AccountWatcher> Accounts = new();
        public AccountWatcher? BalloonAccount;
        public string? BalloonUrl;
        public bool BalloonUpdate;
        public Icon? Current;
    }

    public TrayApp()
    {
        _ui.CreateControl();
        _ = _ui.Handle;

        if (!_cfg.PremierLancementFait)
        {
            Config.StartWithWindows = true;
            _cfg.PremierLancementFait = true;
            _cfg.Save();
        }

        _poll.Tick += (_, _) => { foreach (var w in _watchers) w.Poll(); RefreshIcons(); };
        _ = BuildAsync();
        WatchUpdates();
        Task.Run(CleanOldVersion);
        // Option Bitwarden retirée en 0.8 : on efface l'extension téléchargée par la 0.7.
        try { Directory.Delete(Path.Combine(Config.Folder, "extensions"), true); } catch { }
    }

    // ---------- Mise à jour ----------
    // Une nouvelle version se dépose à côté de l'exe sous le nom MailTray.update.exe.

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;
    private static string UpdatePath => Path.Combine(Path.GetDirectoryName(ExePath)!, "MailTray.update.exe");
    private FileSystemWatcher? _updateWatcher;
    private bool _updateAnnounced;

    private void WatchUpdates()
    {
        try
        {
            _updateWatcher = new FileSystemWatcher(Path.GetDirectoryName(ExePath)!, "MailTray.update.exe")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            FileSystemEventHandler h = (_, _) => _ui.BeginInvoke(() => Later(3000, AnnounceUpdate));
            _updateWatcher.Created += h;
            _updateWatcher.Changed += h;
            _updateWatcher.Renamed += (_, _) => _ui.BeginInvoke(() => Later(3000, AnnounceUpdate));
        }
        catch { }
        Later(15000, AnnounceUpdate); // version déposée pendant que l'app était fermée
    }

    private static void Later(int ms, Action a)
    {
        var t = new System.Windows.Forms.Timer { Interval = ms };
        t.Tick += (_, _) => { t.Stop(); t.Dispose(); a(); };
        t.Start();
    }

    private void AnnounceUpdate()
    {
        if (_updateAnnounced || !File.Exists(UpdatePath)) return;
        var ti = _icons.FirstOrDefault();
        if (ti == null) return;
        _updateAnnounced = true;
        string v = "";
        try { v = System.Diagnostics.FileVersionInfo.GetVersionInfo(UpdatePath).ProductVersion ?? ""; } catch { }
        // Lancée depuis le fichier de mise à jour lui-même, ou version pas plus récente : on ignore (et on nettoie).
        if (string.Equals(Path.GetFullPath(UpdatePath), Path.GetFullPath(ExePath), StringComparison.OrdinalIgnoreCase)) { _updateAnnounced = false; return; }
        if (Version.TryParse(v.Split('+')[0], out var nv) && Version.TryParse(Application.ProductVersion.Split('+')[0], out var cv) && nv <= cv)
        {
            try { File.Delete(UpdatePath); } catch { }
            _updateAnnounced = false;
            return;
        }
        ti.BalloonUpdate = true;
        ti.Icon.ShowBalloonTip(15000, "MailTray : nouvelle version " + v,
            "Clique ici pour mettre à jour et redémarrer (ou menu de l'icône).", ToolTipIcon.Info);
    }

    private static string OldPath => Path.Combine(Path.GetDirectoryName(ExePath)!, "MailTray.old.exe");

    /// <summary>
    /// Windows interdit d'écraser un exe en cours d'exécution, mais autorise à le renommer.
    /// On renomme donc l'exe actuel en .old, on met la nouvelle version à sa place, on relance.
    /// </summary>
    private void ApplyUpdate()
    {
        if (!File.Exists(UpdatePath)) return;
        try
        {
            if (File.Exists(OldPath)) File.Delete(OldPath);
            File.Move(ExePath, OldPath);
            try { File.Move(UpdatePath, ExePath); }
            catch { File.Move(OldPath, ExePath); throw; } // retour arrière si la nouvelle version ne peut pas prendre la place
        }
        catch (Exception ex)
        {
            MessageBox.Show("La mise à jour n'a pas pu être installée.\n\n" + ex.Message, "MailTray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Program.ReleaseInstance();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ExePath, "--apres-maj") { UseShellExecute = false });
        ExitApp();
    }

    /// <summary>Supprime l'ancienne version laissée par une mise à jour.</summary>
    private static void CleanOldVersion()
    {
        for (int i = 0; i < 10 && File.Exists(OldPath); i++)
        {
            try { File.Delete(OldPath); } catch { Thread.Sleep(500); }
        }
    }

    // ---------- Construction ----------

    private async Task BuildAsync()
    {
        try
        {
            _env ??= await CoreWebView2Environment.CreateAsync(null, Path.Combine(Config.Folder, "WebView2"),
                new CoreWebView2EnvironmentOptions
                {
                    // Les fenêtres tournent cachées : on évite que le navigateur les mette en veille.
                    AdditionalBrowserArguments =
                        "--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows --disable-features=IntensiveWakeUpThrottling,CalculateNativeWinOcclusion"
                });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Impossible de démarrer WebView2.\n\n" + ex.Message, "MailTray", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ExitApp();
            return;
        }

        int interval = Math.Max(5, _cfg.IntervalleSecondes) * 1000;
        foreach (var c in _cfg.Comptes.Where(c => c.Actif))
        {
            var w = new AccountWatcher(c, interval);
            w.Changed += _ => RefreshIcons();
            w.Notify += OnNotify;
            w.Window.Shown2 += RefreshIcons;
            w.Window.IconChanged += RefreshIcons;
            _watchers.Add(w);
        }
        BuildIcons();
        UpdateJumpList();

        foreach (var w in _watchers)
        {
            try { await w.InitAsync(_env); } catch { }
        }

        if (_firstBuild && _watchers.Count > 0 && !Environment.GetCommandLineArgs().Contains("--tray"))
            _watchers[0].Window.ShowAndActivate();
        if (_watchers.Count == 0) OpenSettings();
        _firstBuild = false;

        _poll.Interval = interval;
        _poll.Start();
        var first = new System.Windows.Forms.Timer { Interval = 8000 };
        first.Tick += (_, _) => { first.Stop(); first.Dispose(); foreach (var w in _watchers) w.Poll(); };
        first.Start();
        RefreshIcons();
    }

    private void BuildIcons()
    {
        if (_cfg.ModeIcone == Config.ModeParCompte && _watchers.Count > 0)
            foreach (var w in _watchers) _icons.Add(NewIcon(new() { w }));
        else
            _icons.Add(NewIcon(_watchers.Where(w => w.Cfg.DansBadge).ToList() is { Count: > 0 } l ? l : _watchers.ToList()));
    }

    private TrayIcon NewIcon(List<AccountWatcher> accounts)
    {
        var ti = new TrayIcon { Accounts = accounts };
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => FillMenu(menu, ti);
        FillMenu(menu, ti);
        ti.Icon.ContextMenuStrip = menu;
        ti.Icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Toggle(PickAccount(ti)); };
        ti.Icon.BalloonTipClicked += (_, _) =>
        {
            if (ti.BalloonUpdate) { ApplyUpdate(); return; }
            var w = ti.BalloonAccount ?? PickAccount(ti);
            if (w == null) return;
            w.Window.ShowAndActivate();
            if (ti.BalloonUrl != null) w.Window.Navigate(ti.BalloonUrl);
        };
        ti.Icon.Icon = BadgeIcon.Tray(0, false, true);
        ti.Icon.Text = "MailTray";
        ti.Icon.Visible = true;
        return ti;
    }

    private void FillMenu(ContextMenuStrip menu, TrayIcon ti)
    {
        menu.Items.Clear();
        // Les comptes de l'icône, ou tous les comptes si l'icône unique en ignore certains.
        var accounts = _cfg.ModeIcone == Config.ModeParCompte ? ti.Accounts : _watchers;
        if (accounts.Count == 1)
            AddAccountItems(menu.Items, accounts[0]);
        else
            foreach (var w in accounts)
            {
                var sub = new ToolStripMenuItem($"{w.Cfg.Nom}  ({w.MailCount} mail{(w.MailCount > 1 ? "s" : "")}, {w.ChatCount} Chat)");
                AddAccountItems(sub.DropDownItems, w);
                menu.Items.Add(sub);
            }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Paramètres…", null, (_, _) => OpenSettings());
        menu.Items.Add("À propos…", null, (_, _) => OpenSettings("about"));
        menu.Items.Add("Diagnostic", null, (_, _) => ShowDiagnostic());
        if (File.Exists(UpdatePath))
            menu.Items.Add(new ToolStripMenuItem("Mettre à jour et redémarrer", null, (_, _) => ApplyUpdate()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => ExitApp());
    }

    private static void AddAccountItems(ToolStripItemCollection items, AccountWatcher w)
    {
        items.Add("Ouvrir Gmail", null, (_, _) =>
        {
            w.Window.ShowAndActivate();
            if (ReferenceEquals(w.Window.Icon, BrowserForm.ChatIcon)) w.Window.Navigate(w.MailUrl);
        });
        items.Add("Nouveau message", null, (_, _) => { w.Window.ShowAndActivate(); w.Window.Navigate(w.ComposeUrl); });
        items.Add("Ouvrir Chat", null, (_, _) => { w.Window.ShowAndActivate(); w.Window.Navigate(w.ChatUrl); });
    }

    private static AccountWatcher? PickAccount(TrayIcon ti) =>
        ti.Accounts.FirstOrDefault(w => w.Total > 0) ?? ti.Accounts.FirstOrDefault();

    private static void Toggle(AccountWatcher? w)
    {
        if (w == null) return;
        var f = w.Window;
        if (f.Visible && f.WindowState != FormWindowState.Minimized && Form.ActiveForm == f) f.Hide();
        else f.ShowAndActivate();
    }

    // ---------- Commandes (raccourcis du clic droit, double-clic sur l'exe) ----------

    public static string AccountId(CompteConfig c) => c.Profil == "" ? "principal" : c.Profil;

    public void CommandFromOtherThread(string[] args)
    {
        try { _ui.BeginInvoke(() => HandleCommand(args)); } catch { }
    }

    private void HandleCommand(string[] args)
    {
        if (args.Contains("--parametres")) { OpenSettings(); return; }
        int i = Array.IndexOf(args, "--ouvrir");
        string id = i >= 0 && i + 1 < args.Length ? args[i + 1] : "";
        var w = _watchers.FirstOrDefault(x => AccountId(x.Cfg) == id) ?? _watchers.FirstOrDefault();
        w?.Window.ShowAndActivate();
    }

    /// <summary>Ouvre la fenêtre d'un compte (bouton « Ouvrir » ou « Se connecter » des Paramètres).</summary>
    public void OpenAccount(string id)
    {
        var w = _watchers.FirstOrDefault(x => AccountId(x.Cfg) == id);
        if (w == null) return;
        w.Window.ShowAndActivate();
        if (w.Disconnected) w.Window.Navigate(w.SignInUrl); // « Se connecter » : page de connexion Google directement
    }

    /// <summary>État des comptes pour les Paramètres.</summary>
    public object AccountStates() => _watchers.Select(w => new
    {
        id = AccountId(w.Cfg),
        connecte = !w.Disconnected,
        mails = w.MailCount,
        chat = w.ChatCount
    }).ToList();

    /// <summary>Clic droit sur l'icône de la barre des tâches : un raccourci par compte, puis Paramètres.</summary>
    private void UpdateJumpList()
    {
        var tasks = new List<JumpList.JumpTask?>();
        foreach (var w in _watchers)
            tasks.Add(new JumpList.JumpTask(w.Cfg.Nom, $"--ouvrir \"{AccountId(w.Cfg)}\"", $"Ouvrir {w.Cfg.Nom}"));
        tasks.Add(null);
        tasks.Add(new JumpList.JumpTask("Paramètres", "--parametres"));
        JumpList.Apply(tasks);
    }

    // ---------- Notifications ----------

    private void OnNotify(AccountWatcher w, string title, string text, string? url, bool chat)
    {
        var ti = _icons.FirstOrDefault(i => i.Accounts.Contains(w)) ?? _icons.FirstOrDefault();
        if (ti == null) return;
        ti.BalloonAccount = w;
        ti.BalloonUrl = url;
        ti.BalloonUpdate = false;
        // Avec plusieurs comptes, on indique lequel.
        if (_watchers.Count > 1) title = $"{w.Cfg.Nom} : {title}";
        ti.Icon.ShowBalloonTip(6000, Trim(title, 60), string.IsNullOrWhiteSpace(text) ? " " : Trim(text, 250), ToolTipIcon.None);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    // ---------- Affichage ----------

    private void RefreshIcons()
    {
        foreach (var ti in _icons)
        {
            int mail = ti.Accounts.Sum(w => w.MailCount);
            int chat = ti.Accounts.Sum(w => w.ChatCount);
            bool disconnected = ti.Accounts.Count == 0 || ti.Accounts.All(w => w.Disconnected);

            var old = ti.Current;
            ti.Current = BadgeIcon.Tray(mail + chat, mail == 0 && chat > 0, disconnected);
            ti.Icon.Icon = ti.Current;
            old?.Dispose();

            string tip;
            if (ti.Accounts.Count == 0) tip = "MailTray : aucun compte actif";
            else if (disconnected) tip = "MailTray : connecte-toi à Gmail";
            else if (ti.Accounts.Count == 1)
                tip = $"{ti.Accounts[0].Cfg.Nom}\nMails : {mail}\nChat : {(ti.Accounts[0].Cfg.CompterChat ? chat.ToString() : "non compté")}";
            else
                tip = string.Join("\n", ti.Accounts.Select(w => $"{w.Cfg.Nom} : {w.MailCount} + {w.ChatCount} Chat"));
            ti.Icon.Text = tip.Length > 127 ? tip[..127] : tip;
        }

        // Pastille sur le bouton de la barre des tâches : le même chiffre que l'icône du tray qui porte ce compte
        // (le total en mode « une seule icône », le compte seul en mode « une icône par compte »).
        // Les fenêtres cachées la reçoivent aussi, pour être à jour dès qu'elles réapparaissent.
        foreach (var w in _watchers)
        {
            if (!w.Window.IsHandleCreated) continue;
            var ti = _icons.FirstOrDefault(i => i.Accounts.Contains(w));
            var accounts = ti?.Accounts ?? new List<AccountWatcher> { w };
            int mail = accounts.Sum(x => x.MailCount), chat = accounts.Sum(x => x.ChatCount), total = mail + chat;
            using var overlay = total > 0 ? BadgeIcon.Overlay(total, mail == 0) : null;
            Taskbar.SetOverlay(w.Window.Handle, overlay, total > 0 ? $"{total} non lus" : null);
        }
    }

    // ---------- Fenêtres ----------

    private void OpenSettings(string section = "general")
    {
        if (_settings is { IsDisposed: false }) { _settings.ShowSection(section); return; }
        _settings = new SettingsWindow(_cfg, section, AccountStates, OpenAccount);
        _settings.Saved += (newCfg, deleteProfiles) =>
        {
            // Sessions des comptes supprimés : effacées de ce PC si demandé.
            foreach (var w in _watchers.Where(w => w.Cfg.Profil != "" && deleteProfiles.Contains(w.Cfg.Profil)))
                w.DeleteSession();
            _cfg = newCfg;
            _cfg.Save();
            Rebuild();
        };
        _settings.Show();
    }

    private void ShowDiagnostic()
    {
        var text = $"MailTray {Application.ProductVersion}\nMode d'icône : {_cfg.ModeIcone}\n\n"
                 + string.Join("\n", _watchers.Select(w => w.Diagnostic()))
                 + $"\nRéglages : {Config.FilePath}";
        try { Clipboard.SetText(text); } catch { }
        MessageBox.Show(text + "\n\n(Texte copié dans le presse-papiers.)", "MailTray : diagnostic");
    }

    private void Teardown()
    {
        _poll.Stop();
        foreach (var ti in _icons)
        {
            ti.Icon.Visible = false;
            ti.Icon.Dispose();
            ti.Current?.Dispose();
        }
        _icons.Clear();
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }

    private void Rebuild()
    {
        Teardown();
        _ = BuildAsync();
    }

    private void ExitApp()
    {
        Teardown();
        _settings?.Close();
        ExitThread();
    }
}
