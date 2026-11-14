using Microsoft.Win32;

namespace MailTray;

/// <summary>
/// Explique un échec de WebView2 au lieu de laisser une fenêtre grise.
/// Cas connu : un réglage de compatibilité Windows « haute résolution » posé sur msedgewebview2.exe
/// empêche WebView2 d'aligner sa mise à l'échelle sur celle de l'app, et toute fenêtre échoue (0x8007139F).
/// MailTray ne modifie pas ce réglage : il renvoie vers l'aide du README.
/// </summary>
internal static class WebViewCheck
{
    private const string LayersKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string HelpUrl = "https://github.com/TooMuhtsh/MailTray#fen%C3%AAtre-gmail-grise-ou-param%C3%A8tres-qui-ne-souvrent-pas";

    /// <summary>Vrai si un réglage de compatibilité « DPI » est posé sur le moteur WebView2.</summary>
    private static bool HasDpiOverride(RegistryKey root)
    {
        try
        {
            using var key = root.OpenSubKey(LayersKey);
            return key != null && key.GetValueNames().Any(name =>
                name.EndsWith("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase)
                && (key.GetValue(name) as string ?? "").Contains("DPI", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    /// <summary>Signale un échec de WebView2, avec un lien vers l'aide.</summary>
    public static void Explain(Exception ex)
    {
        string cause = HasDpiOverride(Registry.CurrentUser) || HasDpiOverride(Registry.LocalMachine)
            ? "Un réglage de compatibilité Windows sur le moteur WebView2 en est la cause probable."
            : ex.Message;
        var answer = MessageBox.Show(
            "Gmail ne peut pas s'afficher : le moteur WebView2 n'a pas pu créer la fenêtre.\n\n" + cause +
            "\n\nOuvrir l'aide ?", "MailTray", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer == DialogResult.Yes) BrowserForm.OpenExternal(HelpUrl);
    }
}
