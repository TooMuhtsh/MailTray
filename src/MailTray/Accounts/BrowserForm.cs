using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MailTray;

/// <summary>Fenêtre Gmail d'un compte. Fermer la fenêtre la range dans le tray.</summary>
internal sealed class BrowserForm : Form
{
    public string StartUrl { get; }
    public bool AllowClose { get; set; }
    public bool Ready { get; private set; }
    public event Action<JsonElement>? PageMessage;
    public event Action? Shown2;
    public event Action? IconChanged;
    public event Action<string, string>? NotificationArrived;

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };

    public static readonly Icon MailIcon = LoadIcon("MailTray.app.ico");
    public static readonly Icon ChatIcon = LoadIcon("MailTray.chat.ico");

    public CoreWebView2? Core => Ready ? _web.CoreWebView2 : null;

    public BrowserForm(string title, string startUrl, int width = 1280, int height = 860)
    {
        StartUrl = startUrl;
        Text = title;
        Icon = MailIcon;
        Width = width;
        Height = height;
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_web);

        FormClosing += (_, e) =>
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
        VisibleChanged += (_, _) => { if (Visible) Shown2?.Invoke(); };
    }

    private static Icon LoadIcon(string name)
    {
        using var s = typeof(BrowserForm).Assembly.GetManifestResourceStream(name);
        return s != null ? new Icon(s) : SystemIcons.Application;
    }

    /// <summary>Vrai quand la fenêtre affiche l'onglet Chat de Gmail.</summary>
    public static bool IsChatUrl(string url) =>
        url.Contains("/chat/", StringComparison.OrdinalIgnoreCase) || url.Contains("#chat", StringComparison.OrdinalIgnoreCase);

    public async Task InitAsync(CoreWebView2Environment env, string profile)
    {
        // Le contrôle a besoin d'un handle, même si la fenêtre n'est jamais affichée.
        _ = Handle;
        _ = _web.Handle;
        if (string.IsNullOrEmpty(profile))
            await _web.EnsureCoreWebView2Async(env);
        else
        {
            // Session isolée : cookies et connexion Google propres à ce compte.
            var options = env.CreateCoreWebView2ControllerOptions();
            options.ProfileName = profile;
            await _web.EnsureCoreWebView2Async(env, options);
        }
        var core = _web.CoreWebView2;

        core.Settings.IsStatusBarEnabled = false;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(Scripts.Hook("mail"));

        // Jamais de fenêtre « nom d'utilisateur / mot de passe » du navigateur : seule la connexion Google est utilisée.
        core.BasicAuthenticationRequested += (_, e) => e.Cancel = true;

        core.WebMessageReceived += (_, e) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                PageMessage?.Invoke(doc.RootElement.Clone());
            }
            catch { }
        };

        // Notifications de la page (Chat surtout) : autorisées, interceptées et affichées par l'app.
        core.PermissionRequested += (_, e) =>
        {
            if (e.PermissionKind == CoreWebView2PermissionKind.Notifications)
                e.State = CoreWebView2PermissionState.Allow;
        };
        try
        {
            await core.Profile.SetPermissionStateAsync(CoreWebView2PermissionKind.Notifications,
                "https://mail.google.com", CoreWebView2PermissionState.Allow);
        }
        catch { }
        core.NotificationReceived += (_, e) =>
        {
            e.Handled = true;
            NotificationArrived?.Invoke(e.Notification.Title ?? "", e.Notification.Body ?? "");
        };

        // Icône Chat quand l'onglet Chat est ouvert, icône Gmail sinon.
        core.SourceChanged += (_, _) =>
        {
            var wanted = IsChatUrl(core.Source) ? ChatIcon : MailIcon;
            if (!ReferenceEquals(Icon, wanted)) { Icon = wanted; IconChanged?.Invoke(); }
        };

        core.NewWindowRequested += (_, e) =>
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) return;
            bool google = uri.Host.EndsWith("google.com", StringComparison.OrdinalIgnoreCase)
                       || uri.Host.EndsWith("googleusercontent.com", StringComparison.OrdinalIgnoreCase);
            bool redirect = uri.Host.Equals("www.google.com", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath == "/url";
            // Pas encore connecté, ou lien de connexion Google : on reste dans la fenêtre du compte,
            // pour que la connexion se fasse dans sa session et s'affiche ici.
            string src = core.Source ?? "";
            bool inMailbox = src.StartsWith("https://mail.google.com/mail/", StringComparison.OrdinalIgnoreCase)
                          || src.StartsWith("https://mail.google.com/chat/", StringComparison.OrdinalIgnoreCase);
            bool signIn = uri.Host.Equals("accounts.google.com", StringComparison.OrdinalIgnoreCase);
            if (google && !redirect && (signIn || !inMailbox))
            {
                e.Handled = true;
                core.Navigate(e.Uri);
                return;
            }
            if (uri.Scheme is "http" or "https" && (!google || redirect))
            {
                // Lien externe : navigateur par défaut.
                e.Handled = true;
                OpenExternal(e.Uri);
            }
            // Sinon (connexion Google, rédaction, pièces jointes) : fenêtre WebView2 par défaut.
        };

        core.Navigate(StartUrl);
        Ready = true;
    }

    public static void OpenExternal(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }

    public void Navigate(string url)
    {
        if (Ready) _web.CoreWebView2.Navigate(url);
    }

    public void Run(string script)
    {
        if (Ready) _ = _web.CoreWebView2.ExecuteScriptAsync(script);
    }

    public void ShowAndActivate()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }
}
