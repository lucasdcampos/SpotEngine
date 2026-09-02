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

    public delegate int BrowseCallbackProc(IntPtr hwnd, int uMsg, IntPtr lParam, IntPtr lpData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        public string lpszTitle;
        public uint ulFlags;
        public BrowseCallbackProc lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [DllImport("shell32.dll")]
    private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO bi);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, IntPtr pszPath);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);

    public static string? OpenFile(string filter, string initialDir = "")
    {
        if (OperatingSystem.IsWindows()) return OpenFileWindows(filter, initialDir);
        if (OperatingSystem.IsMacOS()) return OpenFileMac(filter, initialDir);
        if (OperatingSystem.IsLinux()) return OpenFileLinux(filter, initialDir);
        return null;
    }

    public static string? SaveFile(string filter, string defExt = "", string initialDir = "")
    {
        if (OperatingSystem.IsWindows()) return SaveFileWindows(filter, defExt, initialDir);
        if (OperatingSystem.IsMacOS()) return SaveFileMac(filter, defExt, initialDir);
        if (OperatingSystem.IsLinux()) return SaveFileLinux(filter, defExt, initialDir);
        return null;
    }

    public static string? SelectFolder()
    {
        if (OperatingSystem.IsWindows()) return SelectFolderWindows();
        if (OperatingSystem.IsMacOS()) return SelectFolderMac();
        if (OperatingSystem.IsLinux()) return SelectFolderLinux();
        return null;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? OpenFileWindows(string filter, string initialDir)
    {
        string? result = null;
        var thread = new Thread(() =>
        {
            // This runs on a dedicated STA thread; an unhandled exception here would terminate the
            // whole process, so it is contained and the dialog simply returns no selection.
            IntPtr fileBuf = IntPtr.Zero;
            IntPtr titleBuf = IntPtr.Zero;
            try
            {
                var ofn = new OpenFileName();
                ofn.lStructSize = Marshal.SizeOf(ofn);
                ofn.lpstrFilter = filter.Replace("|", "\0") + "\0";
                if (!string.IsNullOrEmpty(initialDir))
                    ofn.lpstrInitialDir = initialDir;

                fileBuf = Marshal.AllocHGlobal(520);
                Marshal.WriteInt16(fileBuf, 0);
                ofn.lpstrFile = fileBuf;
                ofn.nMaxFile = 260;

                titleBuf = Marshal.AllocHGlobal(128);
                Marshal.WriteInt16(titleBuf, 0);
                ofn.lpstrFileTitle = titleBuf;
                ofn.nMaxFileTitle = 64;

                ofn.lpstrTitle = "Open File";

                if (GetOpenFileName(ref ofn))
                {
                    result = Marshal.PtrToStringAuto(ofn.lpstrFile);
                }
            }
            catch (Exception ex)
            {
                Spot.Core.Log.CoreError("Open file dialog failed: {0}", ex);
            }
            finally
            {
                if (fileBuf != IntPtr.Zero) Marshal.FreeHGlobal(fileBuf);
                if (titleBuf != IntPtr.Zero) Marshal.FreeHGlobal(titleBuf);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return result;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? SaveFileWindows(string filter, string defExt, string initialDir)
    {
        string? result = null;
        var thread = new Thread(() =>
        {
            // Contained like OpenFile: a fault on this STA thread must not bring the process down.
            IntPtr fileBuf = IntPtr.Zero;
            IntPtr titleBuf = IntPtr.Zero;
            try
            {
                var ofn = new OpenFileName();
                ofn.lStructSize = Marshal.SizeOf(ofn);
                ofn.lpstrFilter = filter.Replace("|", "\0") + "\0";
                if (!string.IsNullOrEmpty(defExt))
                    ofn.lpstrDefExt = defExt;
                if (!string.IsNullOrEmpty(initialDir))
                    ofn.lpstrInitialDir = initialDir;

                fileBuf = Marshal.AllocHGlobal(520);
                Marshal.WriteInt16(fileBuf, 0);
                ofn.lpstrFile = fileBuf;
                ofn.nMaxFile = 260;

                titleBuf = Marshal.AllocHGlobal(128);
                Marshal.WriteInt16(titleBuf, 0);
                ofn.lpstrFileTitle = titleBuf;
                ofn.nMaxFileTitle = 64;

                ofn.lpstrTitle = "Save File";
                // OFN_OVERWRITEPROMPT = 0x00000002
                ofn.Flags = 0x00000002;

                if (GetSaveFileName(ref ofn))
                {
                    result = Marshal.PtrToStringAuto(ofn.lpstrFile);
                }
            }
            catch (Exception ex)
            {
                Spot.Core.Log.CoreError("Save file dialog failed: {0}", ex);
            }
            finally
            {
                if (fileBuf != IntPtr.Zero) Marshal.FreeHGlobal(fileBuf);
                if (titleBuf != IntPtr.Zero) Marshal.FreeHGlobal(titleBuf);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return result;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? SelectFolderWindows()
    {
        string? result = null;
        var thread = new Thread(() =>
        {
            // Contained like the file dialogs: a fault here must not bring the process down.
            try
            {
                var bi = new BROWSEINFO();
                bi.lpszTitle = "Select Directory";
                bi.ulFlags = 0x00000001 | 0x00000040; // BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE

                IntPtr pidl = SHBrowseForFolder(ref bi);
                if (pidl != IntPtr.Zero)
                {
                    IntPtr pathPtr = Marshal.AllocHGlobal(260 * 2);
                    try
                    {
                        if (SHGetPathFromIDList(pidl, pathPtr))
                        {
                            result = Marshal.PtrToStringAuto(pathPtr);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pathPtr);
                        CoTaskMemFree(pidl);
                    }
                }
            }
            catch (Exception ex)
            {
                Spot.Core.Log.CoreError("Select folder dialog failed: {0}", ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return result;
    }

    private static string? RunCommand(string fileName, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return null;
            
            process.WaitForExit();
            if (process.ExitCode != 0) return null;
            
            string output = process.StandardOutput.ReadToEnd().Trim();
            return string.IsNullOrEmpty(output) ? null : output;
        }
        catch (Exception ex)
        {
            Spot.Core.Log.CoreError("Native dialog command failed ({0}): {1}", fileName, ex);
            return null;
        }
    }

    private static string? OpenFileMac(string filter, string initialDir)
    {
        string exts = GetMacExtensions(filter);
        string typeFilter = string.IsNullOrEmpty(exts) ? "" : $" of type {{{exts}}}";
        return RunCommand("osascript", $"-e 'POSIX path of (choose file with prompt \"Open File\"{typeFilter})'");
    }

    private static string? SaveFileMac(string filter, string defExt, string initialDir)
    {
        string defaultName = string.IsNullOrEmpty(defExt) ? "Untitled" : $"Untitled.{defExt}";
        return RunCommand("osascript", $"-e 'POSIX path of (choose file name with prompt \"Save File\" default name \"{defaultName}\")'");
    }

    private static string? SelectFolderMac()
    {
        return RunCommand("osascript", "-e 'POSIX path of (choose folder with prompt \"Select Directory\")'");
    }

    private static string? OpenFileLinux(string filter, string initialDir)
    {
        string zenityFilter = GetZenityFilter(filter);
        return RunCommand("zenity", $"--file-selection --title=\"Open File\" {zenityFilter}");
    }

    private static string? SaveFileLinux(string filter, string defExt, string initialDir)
    {
        string zenityFilter = GetZenityFilter(filter);
        return RunCommand("zenity", $"--file-selection --save --title=\"Save File\" --confirm-overwrite {zenityFilter}");
    }

    private static string? SelectFolderLinux()
    {
        return RunCommand("zenity", "--file-selection --directory --title=\"Select Directory\"");
    }

    private static string GetMacExtensions(string filter)
    {
        if (string.IsNullOrEmpty(filter)) return "";
        var parts = filter.Split('|');
        if (parts.Length < 2) return "";
        var exts = parts[1].Split(';');
        var quoted = new System.Collections.Generic.List<string>();
        foreach (var e in exts)
        {
            quoted.Add($"\"{e.TrimStart('*', '.')}\"");
        }
        return string.Join(", ", quoted);
    }

    private static string GetZenityFilter(string filter)
    {
        if (string.IsNullOrEmpty(filter)) return "";
        var parts = filter.Split('|');
        if (parts.Length < 2) return "";
        var exts = parts[1].Split(';');
        var cleaned = new System.Collections.Generic.List<string>();
        foreach (var e in exts)
        {
            cleaned.Add(e.Trim());
        }
        return $"--file-filter=\"{parts[0]} | {string.Join(" ", cleaned)}\"";
    }
}
