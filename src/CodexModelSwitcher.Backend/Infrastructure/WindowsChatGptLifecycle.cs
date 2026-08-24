using System.Diagnostics;
using CodexModelSwitcher.Abstractions;
using CodexModelSwitcher.Domain;

namespace CodexModelSwitcher.Infrastructure;

public sealed class WindowsChatGptLifecycle : IChatGptLifecycle
{
    private static readonly string[] ProcessNames = ["ChatGPT", "ChatGPT-Desktop"];

    public async Task<RestartResult> RestartAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new RestartResult(false, false, false, "Automatic ChatGPT restart is only supported on Windows.");
        }

        var processes = FindProcesses();
        var wasRunning = processes.Count > 0;
        var executable = TryGetExecutablePath(processes);
        var appId = await TryFindStartAppIdAsync(cancellationToken);

        foreach (var process in processes)
        {
            try
            {
                _ = process.CloseMainWindow();
            }
            catch (InvalidOperationException)
            {
                // The process exited between discovery and shutdown.
            }
        }

        await WaitForExitAsync(processes, TimeSpan.FromSeconds(4), cancellationToken);
        foreach (var process in processes.Where(IsAlive))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Report through the stopped flag below.
            }
        }

        await WaitForExitAsync(processes, TimeSpan.FromSeconds(4), cancellationToken);
        var stopped = processes.All(process => !IsAlive(process));
        foreach (var process in processes)
        {
            process.Dispose();
        }

        if (!stopped)
        {
            return new RestartResult(wasRunning, false, false, "One or more ChatGPT processes could not be stopped.");
        }

        var launchSucceeded = TryLaunch(appId, executable);
        if (!launchSucceeded)
        {
            return new RestartResult(wasRunning, true, false, "ChatGPT was stopped, but no usable launch target was found.");
        }

        var started = await WaitForStartAsync(TimeSpan.FromSeconds(12), cancellationToken);
        return new RestartResult(wasRunning, true, started, started ? null : "The ChatGPT launch command returned, but its process was not detected.");
    }

    private static List<Process> FindProcesses()
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        return ProcessNames
            .SelectMany(Process.GetProcessesByName)
            .Where(process =>
            {
                try
                {
                    return process.SessionId == sessionId;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            })
            .GroupBy(process => process.Id)
            .Select(group => group.First())
            .ToList();
    }

    private static string? TryGetExecutablePath(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    return path;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Packaged applications may not expose MainModule to callers.
            }
        }

        return null;
    }

    private static async Task<string?> TryFindStartAppIdAsync(CancellationToken cancellationToken)
    {
        const string script = "$app = Get-StartApps | Where-Object { $_.Name -eq 'ChatGPT' } | Select-Object -First 1 -ExpandProperty AppID; if ($app) { [Console]::Out.Write($app) }";
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-NonInteractive");
            process.StartInfo.ArgumentList.Add("-Command");
            process.StartInfo.ArgumentList.Add(script);
            if (!process.Start())
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = (await outputTask).Trim();
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }

    private static bool TryLaunch(string? appId, string? executable)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(appId))
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"shell:AppsFolder\\{appId}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                return true;
            }

            if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = true
                });
                return true;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }

        return false;
    }

    private static async Task WaitForExitAsync(
        IReadOnlyCollection<Process> processes,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (processes.Any(IsAlive) && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(100, cancellationToken);
        }
    }

    private static async Task<bool> WaitForStartAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var processes = FindProcesses();
            if (processes.Count > 0)
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }

                return true;
            }

            await Task.Delay(200, cancellationToken);
        }

        return false;
    }

    private static bool IsAlive(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
