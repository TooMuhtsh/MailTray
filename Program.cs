using System.IO.Pipes;

namespace MailTray;

internal static class Program
{
    private const string MutexName = "MailTray.Instance";
    private const string PipeName = "MailTray.Commande";
    private static Mutex? _mutex;

    /// <summary>Libère le verrou d'instance unique (avant de lancer la nouvelle version).</summary>
    public static void ReleaseInstance()
    {
        try { _mutex?.ReleaseMutex(); _mutex?.Dispose(); } catch { }
        _mutex = null;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        bool afterUpdate = args.Contains("--apres-maj");
        if (!Legacy.Prepare(args)) return;
        _mutex = new Mutex(false, MutexName);
        bool owned;
        try { owned = _mutex.WaitOne(afterUpdate ? 15000 : 0); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned)
        {
            // Une instance tourne déjà : on lui transmet la commande (raccourci, double-clic sur l'exe).
            SendToRunning(args.Length > 0 ? string.Join("\u001f", args) : "--afficher");
            return;
        }

        ApplicationConfiguration.Initialize();
        var app = new TrayApp();

        var listener = new Thread(() => Listen(app)) { IsBackground = true };
        listener.Start();

        Application.Run(app);
        ReleaseInstance();
    }

    private static void SendToRunning(string command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(3000);
            using var w = new StreamWriter(pipe);
            w.WriteLine(command);
        }
        catch { }
    }

    private static void Listen(TrayApp app)
    {
        while (true)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                pipe.WaitForConnection();
                using var r = new StreamReader(pipe);
                var line = r.ReadLine();
                if (!string.IsNullOrEmpty(line)) app.CommandFromOtherThread(line.Split('\u001f'));
            }
            catch { Thread.Sleep(500); }
        }
    }
}
