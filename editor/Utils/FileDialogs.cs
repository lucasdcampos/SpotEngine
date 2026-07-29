using System;
using System.Threading;
using System.Windows.Forms;

namespace Spot.Editor.Utils;

public static class FileDialogs
{
    public static string? OpenFile(string filter)
    {
        string? result = null;
        var thread = new Thread(() =>
        {
            using var dialog = new OpenFileDialog();
            dialog.Filter = filter;
            dialog.RestoreDirectory = true;
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                result = dialog.FileName;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return result;
    }

    public static string? SaveFile(string filter)
    {
        string? result = null;
        var thread = new Thread(() =>
        {
            using var dialog = new SaveFileDialog();
            dialog.Filter = filter;
            dialog.RestoreDirectory = true;
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                result = dialog.FileName;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        return result;
    }
}
