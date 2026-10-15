using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PCSetup.Services;

public sealed class ProcessRunner
{
    private readonly RunLogger _log;

    public ProcessRunner(RunLogger log) => _log = log;

    public async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default,
        string? workingDirectory = null, bool logOutput = true)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        };
        // A parent PowerShell 7 session can export Core-only module directories.
        // Windows PowerShell must discover its own .NET Framework modules instead.
        if (Path.GetFileName(fileName).Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
            psi.Environment.Remove("PSModulePath");
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        _log.Info($"Starting: {System.IO.Path.GetFileName(fileName)} {string.Join(" ", arguments.Select(QuoteForLog))}");
        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            throw new InvalidOperationException($"Windows did not start {fileName}.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is not null)
            timeoutCts.CancelAfter(timeout.Value);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException($"{fileName} did not finish within {timeout}.");
        }

        var output = await stdoutTask.ConfigureAwait(false);
        var error = await stderrTask.ConfigureAwait(false);
        if (logOutput && !string.IsNullOrWhiteSpace(output)) _log.Info(output.Trim());
        if (logOutput && !string.IsNullOrWhiteSpace(error)) _log.Warn(error.Trim());
        _log.Info($"Finished: {System.IO.Path.GetFileName(fileName)} (exit {process.ExitCode})");
        return new ProcessResult(process.ExitCode, output, error);
    }

    public ProcessResult Run(string fileName, IEnumerable<string> arguments, TimeSpan? timeout = null,
        bool logOutput = true) => RunAsync(fileName, arguments, timeout, default, null, logOutput).GetAwaiter().GetResult();

    public int RunAsInteractiveUser(string fileName, IEnumerable<string> arguments, TimeSpan timeout)
    {
        var sessionId = NativeMethods.WTSGetActiveConsoleSessionId();
        var explorer = Process.GetProcessesByName("explorer")
            .FirstOrDefault(p => p.SessionId == sessionId)
            ?? throw new InvalidOperationException("The signed-in user's Explorer process was not found.");

        var explorerHandle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, explorer.Id);
        if (explorerHandle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the signed-in user's Explorer process.");
        IntPtr sourceToken;
        try
        {
            if (!NativeMethods.OpenProcessToken(explorerHandle,
                    NativeMethods.TOKEN_DUPLICATE | NativeMethods.TOKEN_QUERY | NativeMethods.TOKEN_ASSIGN_PRIMARY,
                    out sourceToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the signed-in user's token.");
        }
        finally { NativeMethods.CloseHandle(explorerHandle); }

        using var source = new SafeTokenHandle(sourceToken);
        if (!NativeMethods.DuplicateTokenEx(source.DangerousGetHandle(), NativeMethods.MAXIMUM_ALLOWED, IntPtr.Zero,
                2, 1, out var duplicatedToken))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not duplicate the signed-in user's token.");

        using var token = new SafeTokenHandle(duplicatedToken);
        var commandLine = new System.Text.StringBuilder(Quote(fileName) + " " + string.Join(" ", arguments.Select(Quote)));
        var startup = new NativeMethods.STARTUPINFO { cb = Marshal.SizeOf<NativeMethods.STARTUPINFO>(), lpDesktop = @"winsta0\default" };
        if (!NativeMethods.CreateProcessWithTokenW(token.DangerousGetHandle(), 1, fileName, commandLine,
                NativeMethods.CREATE_UNICODE_ENVIRONMENT | NativeMethods.CREATE_NO_WINDOW, IntPtr.Zero, null,
                ref startup, out var processInfo))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not start {System.IO.Path.GetFileName(fileName)} as the signed-in user.");

        try
        {
            var wait = NativeMethods.WaitForSingleObject(processInfo.hProcess, checked((uint)timeout.TotalMilliseconds));
            if (wait == NativeMethods.WAIT_TIMEOUT)
            {
                NativeMethods.TerminateProcess(processInfo.hProcess, 1460);
                throw new TimeoutException($"{fileName} did not finish within {timeout}.");
            }
            if (!NativeMethods.GetExitCodeProcess(processInfo.hProcess, out var exitCode))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            _log.Info($"User-context process finished: {System.IO.Path.GetFileName(fileName)} (exit {exitCode})");
            return unchecked((int)exitCode);
        }
        finally
        {
            NativeMethods.CloseHandle(processInfo.hThread);
            NativeMethods.CloseHandle(processInfo.hProcess);
        }
    }

    private static string QuoteForLog(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private sealed class SafeTokenHandle : SafeHandle
    {
        public SafeTokenHandle(IntPtr handle) : base(IntPtr.Zero, true) => SetHandle(handle);
        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);
        protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
    }

    private static class NativeMethods
    {
        internal const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
        internal const uint TOKEN_DUPLICATE = 0x0002;
        internal const uint TOKEN_QUERY = 0x0008;
        internal const uint MAXIMUM_ALLOWED = 0x02000000;
        internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        internal const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        internal const uint CREATE_NO_WINDOW = 0x08000000;
        internal const uint WAIT_TIMEOUT = 0x00000102;

        [DllImport("kernel32.dll")]
        internal static extern uint WTSGetActiveConsoleSessionId();

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DuplicateTokenEx(IntPtr existingToken, uint desiredAccess, IntPtr tokenAttributes,
            int impersonationLevel, int tokenType, out IntPtr newToken);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcessWithTokenW(IntPtr token, uint logonFlags, string applicationName,
            System.Text.StringBuilder commandLine, uint creationFlags, IntPtr environment, string? currentDirectory,
            ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateProcess(IntPtr process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct STARTUPINFO
        {
            public int cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }
    }
}
