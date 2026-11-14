using Microsoft.Win32;

namespace MailTray;

/// <summary>
/// Explique un échec de WebView2 au lieu de laisser une fenêtre grise.
/// Cas connu : un réglage de compatibilité Windows « haute résolution » posé sur msedgewebview2.exe
/// empêche WebView2 d'aligner sa mise à l'échelle sur celle de l'app, et toute fenêtre échoue (0x8007139F).
/// </summary>
internal static class WebViewCheck
{
    private const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const int InvalidState = unchecked((int)0x8007139F);

    /// <summary>Réglages de compatibilité « DPI » posés sur le moteur WebView2 (nom de valeur = chemin de l'exe).</summary>
    private static List<string> DpiOverrides(RegistryKey root)
    {
        var found = new List<string>();
        try
        {
            using var key = root.OpenSubKey(LayersKey);
            if (key == null) return found;
            foreach (var name in key.GetValueNames())
            {
                if (!name.EndsWith("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase)) continue;
                if ((key.GetValue(name) as string ?? "").Contains("DPI", StringComparison.OrdinalIgnoreCase))
                    found.Add(name);
            }
        }
        catch { }
        return found;
    }

    /// <summary>Explique un échec de WebView2. Vrai si l'app doit redémarrer.</summary>
    public static bool Explain(Exception ex)
    {
        var user = DpiOverrides(Registry.CurrentUser);
        var machine = DpiOverrides(Registry.LocalMachine);
        if (ex.HResult == InvalidState && user.Count + machine.Count > 0)
        {
            const string intro =
                "Windows applique un réglage de compatibilité « haute résolution » au moteur WebView2 " +
                "(msedgewebview2.exe). Ce réglage empêche MailTray d'afficher Gmail et les paramètres.\n\n" +
                "Il se pose quand on coche « Remplacer le comportement de mise à l'échelle haute résolution » " +
                "dans les propriétés de ce fichier, ou automatiquement par l'assistant de compatibilité de Windows.";
            if (machine.Count == 0)
            {
                var answer = MessageBox.Show(intro + "\n\nRetirer ce réglage et redémarrer MailTray ?",
                    "MailTray", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return false;
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(LayersKey, writable: true);
                    foreach (var name in user) key?.DeleteValue(name, throwOnMissingValue: false);
                    return true;
                }
                catch (Exception e)
                {
                    MessageBox.Show("Le réglage n'a pas pu être retiré.\n\n" + e.Message, "MailTray",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }
            MessageBox.Show(intro + "\n\nCe réglage est posé pour tous les utilisateurs du PC. Pour le retirer, ouvre " +
                "les propriétés de ce fichier en administrateur, onglet Compatibilité, et décoche le réglage de mise à l'échelle :\n\n" +
                string.Join("\n", machine), "MailTray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        MessageBox.Show("Gmail ne peut pas s'afficher : le moteur WebView2 refuse de créer la fenêtre.\n\n" +
            ex.Message, "MailTray", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return false;
    }
}
