using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MailTray;

/// <summary>Dessine l'icône du tray (enveloppe + pastille) et la pastille de la barre des tâches.</summary>
internal static class BadgeIcon
{
    private static readonly Color Red = Color.FromArgb(234, 67, 53);
    private static readonly Color Blue = Color.FromArgb(26, 115, 232);

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);

    public static string Label(int n) => n > 99 ? "99+" : n.ToString();

    /// <summary>Icône du tray. chatOnly : pastille bleue si seul le Chat a des non-lus.</summary>
    public static Icon Tray(int total, bool chatOnly, bool error)
    {
        int s = 32;
        using var bmp = new Bitmap(s, s);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            // Icône de l app (grisée si déconnecté)
            using (var logo = new Bitmap(BrowserForm.MailIcon.ToBitmap(), new Size(s, s)))
            {
                if (error)
                {
                    var cm = new System.Drawing.Imaging.ColorMatrix(new[]
                    {
                        new[] { .3f, .3f, .3f, 0f, 0f }, new[] { .59f, .59f, .59f, 0f, 0f },
                        new[] { .11f, .11f, .11f, 0f, 0f }, new[] { 0f, 0f, 0f, .6f, 0f }, new[] { 0f, 0f, 0f, 0f, 1f }
                    });
                    using var ia = new System.Drawing.Imaging.ImageAttributes();
                    ia.SetColorMatrix(cm);
                    g.DrawImage(logo, new Rectangle(0, 0, s, s), 0, 0, s, s, GraphicsUnit.Pixel, ia);
                }
                else g.DrawImage(logo, 0, 0, s, s);
            }

            if (total > 0) DrawBadge(g, total, chatOnly ? Blue : Red, new Rectangle(s - 22, s - 18, 22, 18));
        }
        return ToIcon(bmp);
    }

    /// <summary>Petite pastille posée sur le bouton de la barre des tâches.</summary>
    public static Icon Overlay(int total, bool chatOnly)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            DrawBadge(g, total, chatOnly ? Blue : Red, new Rectangle(0, 0, 16, 16));
        }
        return ToIcon(bmp);
    }

    private static void DrawBadge(Graphics g, int n, Color color, Rectangle r)
    {
        string text = Label(n);
        using (var b = new SolidBrush(color)) g.FillEllipse(b, r);
        float size = text.Length switch { 1 => r.Height * 0.70f, 2 => r.Height * 0.58f, _ => r.Height * 0.44f };
        using var font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, Brushes.White, new RectangleF(r.X, r.Y + 0.5f, r.Width, r.Height), sf);
    }

    private static Icon ToIcon(Bitmap bmp)
    {
        IntPtr h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }
}
