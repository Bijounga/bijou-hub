using System.Diagnostics;
using BijouHub.Models;

namespace BijouHub.Services;

public static class ModeLauncherService
{
    public static async Task<List<string>> LaunchAsync(WorkMode mode)
    {
        var failures = new List<string>();

        foreach (var item in mode.LaunchItems)
        {
            if (item.DelayMs > 0)
                await Task.Delay(item.DelayMs);

            try
            {
                LaunchItem(item);
            }
            catch (Exception ex)
            {
                // Keep going so one bad item doesn't block the rest of the mode,
                // but report every failure back to the caller instead of hiding it.
                failures.Add($"{item.DisplayName}: {ex.Message}");
            }
        }

        return failures;
    }

    private static void LaunchItem(LaunchItem item)
    {
        switch (item.Type)
        {
            case LaunchItemType.Application:
                Process.Start(new ProcessStartInfo
                {
                    FileName = item.Path,
                    Arguments = item.Arguments,
                    UseShellExecute = true
                });
                break;

            case LaunchItemType.Folder:
            case LaunchItemType.File:
                if (!string.IsNullOrEmpty(item.OpenWithAppPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = item.OpenWithAppPath,
                        Arguments = $"\"{item.Path}\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = item.Path,
                        UseShellExecute = true
                    });
                }
                break;

            case LaunchItemType.Url:
                Process.Start(new ProcessStartInfo
                {
                    FileName = item.Path,
                    UseShellExecute = true
                });
                break;

            case LaunchItemType.BrowserUrl:
                BrowserProfileService.LaunchUrl(item.Browser, item.BrowserProfileDir, item.Path);
                break;
        }
    }

    public static List<Process> GetRunningBlockedProcesses(WorkMode mode)
    {
        var result = new List<Process>();
        foreach (var block in mode.BlockItems)
        {
            result.AddRange(Process.GetProcessesByName(block.ProcessName));
        }
        return result;
    }
}
