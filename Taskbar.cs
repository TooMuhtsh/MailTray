using System.Runtime.InteropServices;

namespace MailTray;

/// <summary>Pastille sur le bouton de la barre des tâches (ITaskbarList3.SetOverlayIcon).</summary>
internal static class Taskbar
{
    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, int flags);
        void RegisterTab(IntPtr tab, IntPtr main);
        void UnregisterTab(IntPtr tab);
        void SetTabOrder(IntPtr tab, IntPtr before);
        void SetTabActive(IntPtr tab, IntPtr main, uint reserved);
        void ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
        void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string? description);
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
    private class TaskbarList { }

    private static ITaskbarList3? _list;

    public static void SetOverlay(IntPtr hwnd, Icon? icon, string? description)
    {
        try
        {
            if (_list == null)
            {
                _list = (ITaskbarList3)new TaskbarList();
                _list.HrInit();
            }
            _list.SetOverlayIcon(hwnd, icon?.Handle ?? IntPtr.Zero, description);
        }
        catch { /* bouton pas encore créé : on réessaiera au prochain rafraîchissement */ }
    }
}
