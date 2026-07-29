using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Spot.Editor.Utils;

public static class FileDialogs
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int flagsEx;
    }

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetOpenFileName(ref OpenFileName ofn);

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetSaveFileName(ref OpenFileName ofn);

    public static string? OpenFile(string filter)
    {
        string? result = null;
        var thread = new Thread(() =>
        {
            var ofn = new OpenFileName();
            ofn.lStructSize = Marshal.SizeOf(ofn);
            ofn.lpstrFilter = filter.Replace("|", "\0") + "\0";
            
            ofn.lpstrFile = Marshal.AllocHGlobal(520);
            Marshal.WriteInt16(ofn.lpstrFile, 0);
            ofn.nMaxFile = 260;
            
            ofn.lpstrFileTitle = Marshal.AllocHGlobal(128);
            Marshal.WriteInt16(ofn.lpstrFileTitle, 0);
            ofn.nMaxFileTitle = 64;
            
            ofn.lpstrTitle = "Open File";

            if (GetOpenFileName(ref ofn))
            {
                result = Marshal.PtrToStringAuto(ofn.lpstrFile);
            }

            Marshal.FreeHGlobal(ofn.lpstrFile);
            Marshal.FreeHGlobal(ofn.lpstrFileTitle);
        });
        
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return result;
    }

    public static string? SaveFile(string filter, string defExt = "")
    {
        string? result = null;
        var thread = new Thread(() =>
        {
            var ofn = new OpenFileName();
            ofn.lStructSize = Marshal.SizeOf(ofn);
            ofn.lpstrFilter = filter.Replace("|", "\0") + "\0";
            if (!string.IsNullOrEmpty(defExt))
                ofn.lpstrDefExt = defExt;
            
            ofn.lpstrFile = Marshal.AllocHGlobal(520);
            Marshal.WriteInt16(ofn.lpstrFile, 0);
            ofn.nMaxFile = 260;
            
            ofn.lpstrFileTitle = Marshal.AllocHGlobal(128);
            Marshal.WriteInt16(ofn.lpstrFileTitle, 0);
            ofn.nMaxFileTitle = 64;
            
            ofn.lpstrTitle = "Save File";
            // OFN_OVERWRITEPROMPT = 0x00000002
            ofn.Flags = 0x00000002;

            if (GetSaveFileName(ref ofn))
            {
                result = Marshal.PtrToStringAuto(ofn.lpstrFile);
            }

            Marshal.FreeHGlobal(ofn.lpstrFile);
            Marshal.FreeHGlobal(ofn.lpstrFileTitle);
        });
        
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return result;
    }
}
