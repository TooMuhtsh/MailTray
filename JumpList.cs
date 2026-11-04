using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace MailTray;

/// <summary>
/// Menu du clic droit sur l'icône de la barre des tâches (Jump List, section « Tâches »).
/// Chaque entrée relance l'exe avec une commande, transmise à l'instance qui tourne.
/// </summary>
internal static class JumpList
{
    public record JumpTask(string Title, string Arguments, string? Description = null);

    public static void Apply(IReadOnlyList<JumpTask?> tasks)
    {
        try
        {
            var list = (ICustomDestinationList)new CDestinationList();
            var riid = typeof(IObjectArray).GUID;
            list.BeginList(out _, ref riid, out _);

            var collection = (IObjectCollection)new CEnumerableObjectCollection();
            foreach (var t in tasks) collection.AddObject(t == null ? Separator() : Link(t));
            list.AddUserTasks((IObjectArray)collection);
            list.CommitList();
        }
        catch { /* le menu reste celui par défaut */ }
    }

    private static IShellLinkW Link(JumpTask t)
    {
        var link = (IShellLinkW)new CShellLink();
        string exe = Environment.ProcessPath ?? Application.ExecutablePath;
        link.SetPath(exe);
        link.SetArguments(t.Arguments);
        link.SetIconLocation(exe, 0);
        if (t.Description != null) link.SetDescription(t.Description);
        var store = (IPropertyStore)link;
        var key = PKEY_Title;
        var value = new PropVariant(t.Title);
        store.SetValue(ref key, ref value);
        store.Commit();
        value.Clear();
        return link;
    }

    private static IShellLinkW Separator()
    {
        var link = (IShellLinkW)new CShellLink();
        var store = (IPropertyStore)link;
        var key = PKEY_AppUserModel_IsDestListSeparator;
        var value = new PropVariant(true);
        store.SetValue(ref key, ref value);
        store.Commit();
        return link;
    }

    // ---------- Interop COM ----------

    private static PropertyKey PKEY_Title = new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
    private static PropertyKey PKEY_AppUserModel_IsDestListSeparator = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 6);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
        public PropertyKey(Guid f, uint p) { fmtid = f; pid = p; }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] private ushort vt;
        [FieldOffset(8)] private IntPtr ptr;
        [FieldOffset(8)] private short boolVal;

        public PropVariant(string s) : this() { vt = 31; ptr = Marshal.StringToCoTaskMemUni(s); } // VT_LPWSTR
        public PropVariant(bool b) : this() { vt = 11; boolVal = (short)(b ? -1 : 0); }          // VT_BOOL
        public void Clear() { if (vt == 31 && ptr != IntPtr.Zero) { Marshal.FreeCoTaskMem(ptr); ptr = IntPtr.Zero; } }
    }

    [ComImport, Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6")] private class CDestinationList { }
    [ComImport, Guid("2d3468c1-36a7-43b6-ac24-d3f02fd9607a")] private class CEnumerableObjectCollection { }
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class CShellLink { }

    [ComImport, Guid("6332debf-87b5-4670-90c0-5e57b408a49e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void BeginList(out uint pcMaxSlots, [In] ref Guid riid, [Out, MarshalAs(UnmanagedType.Interface)] out object ppv);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, [MarshalAs(UnmanagedType.Interface)] IObjectArray poa);
        void AppendKnownCategory(int category);
        void AddUserTasks([MarshalAs(UnmanagedType.Interface)] IObjectArray poa);
        void CommitList();
        void GetRemovedDestinations([In] ref Guid riid, [Out, MarshalAs(UnmanagedType.Interface)] out object ppv);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void AbortList();
    }

    [ComImport, Guid("92CA9DCD-5622-4bba-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint pcObjects);
        void GetAt(uint uiIndex, [In] ref Guid riid, [Out, MarshalAs(UnmanagedType.Interface)] out object ppv);
    }

    [ComImport, Guid("5632b1a4-e38a-400a-928a-d4cd63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        void GetCount(out uint pcObjects);
        void GetAt(uint uiIndex, [In] ref Guid riid, [Out, MarshalAs(UnmanagedType.Interface)] out object ppv);
        void AddObject([MarshalAs(UnmanagedType.Interface)] object pvObject);
        void AddFromArray([MarshalAs(UnmanagedType.Interface)] IObjectArray poaSource);
        void RemoveObjectAt(uint uiIndex);
        void Clear();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }
}
