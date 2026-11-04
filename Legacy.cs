using System.Diagnostics;
using Microsoft.Win32;

namespace MailTray;

/// <summary>
/// Reprise d'une installation de GmailTray, l'ancien nom de l'app :
/// exe renommé en MailTray.exe, dossier des données déplacé, lancement avec Windows repris.
/// </summary>
internal static class Legacy
{
    private const string OldName = "GmailTray";

    /// <summary>Faux si l'app doit s'arrêter (elle s'est relancée sous son nouveau nom).</summary>
    public static bool Prepare(string[] args)
    {
        if (!WaitOldInstance()) return false;

        string? exe = Environment.ProcessPath;
        if (exe != null && Path.GetFileName(exe).Equals(OldName + ".exe", StringComparison.OrdinalIgnoreCase))
        {
            string target = Path.Combine(Path.GetDirectoryName(exe)!, "MailTray.exe");
            if (!File.Exists(target))
            {
                try
                {
                    File.Move(exe, target);
                    var psi = new ProcessStartInfo(target) { UseShellExecute = false };
                    foreach (var a in args.Where(a => a != "--apres-maj")) psi.ArgumentList.Add(a);
                    psi.ArgumentList.Add("--apres-maj");
                    Process.Start(psi);
                    return false;
                }
                catch { /* on continue sous l'ancien nom */ }
            }
        }

        MoveDataFolder();
        MoveStartup();
        try { File.Delete(Path.Combine(Path.GetDirectoryName(exe ?? "")!, OldName + ".old.exe")); } catch { }
        return true;
    }

    /// <summary>Attend qu'une ancienne GmailTray encore ouverte se ferme (cas de la mise à jour).</summary>
    private static bool WaitOldInstance()
    {
        try
        {
            using var m = new Mutex(false, OldName + ".Instance");
            bool owned;
            try { owned = m.WaitOne(15000); }
            catch (AbandonedMutexException) { owned = true; }
            if (owned) { m.ReleaseMutex(); return true; }
        }
        catch { return true; }
        MessageBox.Show("GmailTray est encore ouverte. Quitte-la, puis relance MailTray.", "MailTray",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private static void MoveDataFolder()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string oldDir = Path.Combine(root, OldName);
        if (Directory.Exists(Config.Folder) || !Directory.Exists(oldDir)) return;
        for (int i = 0; i < 20; i++)
        {
            try { Directory.Move(oldDir, Config.Folder); return; }
            catch { Thread.Sleep(500); } // WebView2 de l'ancienne version encore en train de se fermer
        }
        Config.Folder = oldDir; // on garde l'ancien dossier, sessions comprises
    }

    private static void MoveStartup()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Config.RunKey, true);
            if (k?.GetValue(OldName) == null) return;
            k.DeleteValue(OldName, false);
            Config.StartWithWindows = true;
        }
        catch { }
    }
}
