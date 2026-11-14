namespace MailTray;

/// <summary>Signale un échec de WebView2 au lieu de laisser une fenêtre grise.</summary>
internal static class WebViewCheck
{
    public static void Explain(Exception ex) =>
        MessageBox.Show("MailTray ne peut pas afficher cette fenêtre : le moteur WebView2 n'a pas pu la créer.\n\n" + ex.Message,
            "MailTray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
