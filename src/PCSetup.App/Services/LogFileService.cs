using System.Diagnostics;

namespace PCSetup.Services;

public static class LogFileService
{
    public static ProcessStartInfo BuildOpenInfo(string path)
    {
        var absolute = Path.GetFullPath(path);
        if (!File.Exists(absolute)) throw new FileNotFoundException("The run log is not available yet.", absolute);
        return new ProcessStartInfo(absolute) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(absolute)! };
    }

    public static void Open(string path)
    {
        var start = BuildOpenInfo(path);
        try { Process.Start(start)?.Dispose(); }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1155)
        {
            var fallback = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "notepad.exe")) { UseShellExecute = false };
            fallback.ArgumentList.Add(start.FileName);
            Process.Start(fallback)?.Dispose();
        }
    }
}
