using PCSetup.Services;
using PCSetup.UI;

namespace PCSetup;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, @"Local\PCSetup.SingleInstance", out var ownsMutex);
        if (!ownsMutex)
        {
            MessageBox.Show("PSeeSetup is already running in this Windows session.", "PSeeSetup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            if (!SystemContext.IsSupportedWindows(out var platformReason))
            {
                MessageBox.Show(platformReason, "PSeeSetup cannot run", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!SystemContext.IsAdministrator())
            {
                MessageBox.Show("PSeeSetup must run as an administrator.", "PSeeSetup cannot run", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var interactiveUser = SystemContext.InteractiveIdentity();
            if (!SystemContext.SameIdentity(SystemContext.CurrentIdentity(), interactiveUser))
            {
                MessageBox.Show($"The elevated account ({SystemContext.CurrentIdentity()}) is not the signed-in account ({interactiveUser}). Sign in with the administrator account that will receive Spotify and the other user applications.",
                    "User account mismatch", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var bitLockerResume = args.Any(x =>
                x.Equals(BitLockerPreparationService.ResumeArgument, StringComparison.OrdinalIgnoreCase));
            if (bitLockerResume)
            {
                var currentExe = Path.GetFullPath(Environment.ProcessPath ?? "");
                if (!currentExe.Equals(Path.GetFullPath(BitLockerPreparationService.StagedExecutable),
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Only PSeeSetup's protected staged copy may continue BitLocker preparation after sign-in.");
            }

            BitLockerGateResult gate;
            using (var nullLog = new RunLogger())
            {
                var runner = new ProcessRunner(nullLog);
                gate = new BitLockerGate(nullLog, runner).CheckAllInternalFixedDrives();
            }
            if (!gate.IsSafe)
            {
                if (!bitLockerResume && !BitLockerPreparationService.PreparationPending)
                {
                    using var choice = new BitLockerChoiceDialog(gate);
                    if (choice.ShowDialog() != DialogResult.OK) return;
                }

                using var preparationLog = new RunLogger(BitLockerPreparationService.LogPath);
                var preparation = new BitLockerPreparationService(preparationLog);
                if (!bitLockerResume) preparation.StageAndRegister(interactiveUser);

                Application.Run(new BitLockerPreparationForm(preparation, gate));
                return;
            }

            if (!bitLockerResume && BitLockerPreparationService.PreparationPending)
            {
                using var preparationLog = new RunLogger(BitLockerPreparationService.LogPath);
                var preparation = new BitLockerPreparationService(preparationLog);
                Application.Run(new BitLockerPreparationForm(preparation, gate));
                return;
            }
            if (bitLockerResume)
            {
                using var preparationLog = new RunLogger(BitLockerPreparationService.LogPath);
                new BitLockerPreparationService(preparationLog).RemoveResumeTask();
            }

            SetupState? resumeState = null;
            var resumeIndex = Array.FindIndex(args, x => x.Equals("--resume", StringComparison.OrdinalIgnoreCase));
            if (resumeIndex >= 0)
            {
                if (resumeIndex + 1 >= args.Length) throw new ArgumentException("The resume state path is missing.");
                var path = Path.GetFullPath(args[resumeIndex + 1]);
                var allowedRoot = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PCSetup", "Runs")) + Path.DirectorySeparatorChar;
                if (!path.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The resume state is outside PCSetup's protected run directory.");
                resumeState = StateStore.Load(path);
                var currentExe = Path.GetFullPath(Environment.ProcessPath ?? "");
                if (!currentExe.Equals(Path.GetFullPath(resumeState.StagedExecutable), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Only the staged PCSetup executable may resume this saved run.");
            }

            Application.Run(new MainForm(resumeState, gate));
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "PSeeSetup stopped safely", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
