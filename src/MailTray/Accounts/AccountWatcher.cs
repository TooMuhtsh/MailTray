using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Web.WebView2.Core;

namespace MailTray;

/// <summary>Un compte Google : sa fenêtre Gmail, ses compteurs et ses notifications.</summary>
internal sealed class AccountWatcher : IDisposable
{
    public CompteConfig Cfg { get; }
    public BrowserForm Window { get; }

    public int MailCount { get; private set; }
    public int ChatCount { get; private set; }
    public int Total => MailCount + ChatCount;
    public bool Disconnected { get; private set; } = true;

    /// <summary>Compteurs ou état changés.</summary>
    public event Action<AccountWatcher>? Changed;
    /// <summary>Notification à afficher : titre, texte, lien à ouvrir au clic (ou null), vrai si Chat.</summary>
    public event Action<AccountWatcher, string, string, string?, bool>? Notify;

    private readonly int _intervalMs;
    private int? _atomCount, _railMail, _railChat, _mailTitle, _mailBadge;
    private string _railChatLabel = "", _railMailLabel = "", _titleRaw = "", _atomStatus = "aucune relève";
    private DateTime _lastAtomOk = DateTime.MinValue, _lastPageNotif = DateTime.MinValue;
    private readonly DateTime _started = DateTime.Now;
    private readonly HashSet<string> _seen = new();
    private bool _firstAtom = true;
    private int _prevChat;

    public string MailUrl => $"https://mail.google.com/mail/u/{Cfg.Index}/";
    public string SignInUrl => "https://accounts.google.com/ServiceLogin?service=mail&continue=" + Uri.EscapeDataString(MailUrl);
    public string ChatUrl => $"https://mail.google.com/chat/u/{Cfg.Index}/";
    public string ComposeUrl => MailUrl + "#inbox?compose=new";
    private string AtomUrl => $"https://mail.google.com/mail/u/{Cfg.Index}/feed/atom";

    public AccountWatcher(CompteConfig cfg, int intervalMs)
    {
        Cfg = cfg;
        _intervalMs = intervalMs;
        Window = new BrowserForm($"Gmail : {cfg.Nom}", MailUrl);
        Window.PageMessage += OnPageMessage;
        Window.NotificationArrived += OnPageNotification;
    }

    public Task InitAsync(CoreWebView2Environment env) => Window.InitAsync(env, Cfg.Profil);

    /// <summary>Efface la session Google de ce compte (cookies, cache) de ce PC.</summary>
    public void DeleteSession()
    {
        try { Window.Core?.Profile.Delete(); } catch { }
    }

    public void Poll() => Window.Run($"window.__gtPollMail && window.__gtPollMail('{AtomUrl}')");

    // ---------- Signaux de la page ----------

    private static int? CountInTitle(string title)
    {
        var m = Regex.Match(title, @"\((\d+)\)");
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    private void OnPageMessage(JsonElement msg)
    {
        string type = msg.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        switch (type)
        {
            case "atom": OnAtom(msg); break;
            case "title":
                _titleRaw = msg.GetProperty("title").GetString() ?? "";
                _mailTitle = CountInTitle(_titleRaw);
                break;
            case "badge":
                int b = msg.GetProperty("count").GetInt32();
                _mailBadge = b < 0 ? null : b;
                break;
            case "railmail":
                int rm = msg.GetProperty("count").GetInt32();
                _railMail = rm < 0 ? null : rm;
                _railMailLabel = msg.GetProperty("label").GetString() ?? "";
                Poll(); // la pastille Mail a bougé : relève immédiate du flux
                break;
            case "railchat":
                int rc = msg.GetProperty("count").GetInt32();
                _railChat = rc < 0 ? null : rc;
                _railChatLabel = msg.GetProperty("label").GetString() ?? "";
                break;
        }
        Recompute();
    }

    private void OnAtom(JsonElement msg)
    {
        int status = msg.GetProperty("status").GetInt32();
        if (status != 200 || !msg.TryGetProperty("xml", out var xmlEl))
        {
            _atomStatus = status == 0 ? "erreur réseau ou session absente" : $"HTTP {status}";
            return;
        }
        try
        {
            XNamespace ns = "http://purl.org/atom/ns#";
            var feed = XDocument.Parse(xmlEl.GetString() ?? "").Root!;
            _atomCount = int.Parse(feed.Element(ns + "fullcount")?.Value ?? "0");
            _lastAtomOk = DateTime.Now;
            _atomStatus = "OK";

            var entries = feed.Elements(ns + "entry").Select(e => new
            {
                Id = e.Element(ns + "id")?.Value ?? "",
                Title = e.Element(ns + "title")?.Value ?? "(sans objet)",
                Author = e.Element(ns + "author")?.Element(ns + "name")?.Value ?? "",
                Link = e.Element(ns + "link")?.Attribute("href")?.Value
            }).ToList();

            var fresh = entries.Where(e => e.Id != "" && !_seen.Contains(e.Id)).ToList();
            foreach (var e in entries) _seen.Add(e.Id);

            if (!_firstAtom && fresh.Count > 0 && Cfg.Notifications)
            {
                // On laisse quelques secondes à Gmail pour notifier lui-même, pour éviter les doublons.
                Later(4000, () =>
                {
                    if (DateTime.Now - _lastPageNotif < TimeSpan.FromSeconds(8)) return;
                    if (_railMail == 0) return; // déjà lu dans Gmail, le flux est en retard
                    if (fresh.Count == 1)
                        Notify?.Invoke(this, fresh[0].Author, fresh[0].Title, fresh[0].Link, false);
                    else
                    {
                        var sb = new StringBuilder();
                        foreach (var e in fresh.Take(3)) sb.AppendLine($"{e.Author} : {e.Title}");
                        Notify?.Invoke(this, $"{fresh.Count} nouveaux mails", sb.ToString().TrimEnd(), null, false);
                    }
                });
            }
            _firstAtom = false;
        }
        catch (Exception ex)
        {
            _atomStatus = "flux illisible : " + ex.Message;
        }
    }

    private void OnPageNotification(string title, string body)
    {
        _lastPageNotif = DateTime.Now;
        if (Cfg.Notifications) Notify?.Invoke(this, title, body, null, false);
    }

    // ---------- Calcul ----------

    public void Recompute()
    {
        bool atomFresh = DateTime.Now - _lastAtomOk < TimeSpan.FromMilliseconds(_intervalMs * 3 + 10000);
        Disconnected = !atomFresh && _railMail == null && _mailTitle == null && _mailBadge == null;

        // La pastille Mail de la page fait foi : c'est ce que l'utilisateur voit dans Gmail.
        // Le flux de Google peut garder un mail déjà lu pendant un moment.
        MailCount = _railMail ?? (atomFresh ? _atomCount : null) ?? _mailBadge ?? _mailTitle ?? 0;
        ChatCount = Cfg.CompterChat ? _railChat ?? 0 : 0;

        // Chat qui monte sans notification de la page : on prévient quand même.
        if (ChatCount > _prevChat && DateTime.Now - _started > TimeSpan.FromSeconds(30) && Cfg.Notifications)
        {
            int diff = ChatCount - _prevChat;
            Later(4000, () =>
            {
                if (DateTime.Now - _lastPageNotif < TimeSpan.FromSeconds(8)) return;
                Notify?.Invoke(this, "Google Chat", diff == 1 ? "Nouveau message" : $"{diff} nouveaux messages", ChatUrl, true);
            });
        }
        _prevChat = ChatCount;
        Changed?.Invoke(this);
    }

    private static void Later(int ms, Action a)
    {
        var t = new System.Windows.Forms.Timer { Interval = ms };
        t.Tick += (_, _) => { t.Stop(); t.Dispose(); a(); };
        t.Start();
    }

    public string Diagnostic()
    {
        string Fmt(int? v) => v?.ToString() ?? "rien";
        return
            $"[{Cfg.Nom}] session « {(Cfg.Profil == "" ? "par défaut" : Cfg.Profil)} », compte u/{Cfg.Index}\n" +
            $"  Mails affichés : {MailCount}\n" +
            $"    Flux des non-lus : {Fmt(_atomCount)} ({_atomStatus}, dernière réussite {(_lastAtomOk == DateTime.MinValue ? "jamais" : _lastAtomOk.ToString("HH:mm:ss"))})\n" +
            $"    Pastille Mail : {Fmt(_railMail)}  « {_railMailLabel} »\n" +
            $"    Titre : {Fmt(_mailTitle)}  « {_titleRaw} »\n" +
            $"  Chat affiché : {ChatCount}{(Cfg.CompterChat ? "" : " (non compté)")}\n" +
            $"    Pastille Chat : {Fmt(_railChat)}  « {_railChatLabel} »\n" +
            $"  Dernière notification de la page : {(_lastPageNotif == DateTime.MinValue ? "aucune" : _lastPageNotif.ToString("HH:mm:ss"))}\n";
    }

    public void Dispose()
    {
        Window.AllowClose = true;
        try { Window.Close(); } catch { }
        Window.Dispose();
    }
}
