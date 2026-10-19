using System.Diagnostics;
using System.Runtime.InteropServices;
namespace PCSetup.Launcher;
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                throw new PlatformNotSupportedException("Windows 11 is required.");
            var executable = Path.Combine(AppContext.BaseDirectory,
                LauncherTarget.FolderFor(RuntimeInformation.OSArchitecture), "PSeeSetup.exe");
            if (!File.Exists(executable))
                throw new FileNotFoundException("Extract the entire PSeeSetup 1.2.7 folder before launching it.", executable);
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            };
            foreach (var argument in args) start.ArgumentList.Add(argument);
            using var child = Process.Start(start) ?? throw new InvalidOperationException("PSeeSetup could not start.");
            child.WaitForExit();
            return child.ExitCode;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "PSeeSetup 1.2.7", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
