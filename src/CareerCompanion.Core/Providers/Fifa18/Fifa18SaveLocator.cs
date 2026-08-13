using Microsoft.Win32;

namespace CareerCompanion.Core.Providers.Fifa18;

public sealed class Fifa18SaveLocator
{
    public string? FindSettingsDirectory()
    {
        var candidates = new List<string>();
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents)) candidates.Add(Path.Combine(documents,"FIFA 18","settings"));
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
            if (key?.GetValue("Personal") is string personal)
                candidates.Add(Path.Combine(Environment.ExpandEnvironmentVariables(personal),"FIFA 18","settings"));
        }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Documents","FIFA 18","settings"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"OneDrive","Documents","FIFA 18","settings"));
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).FirstOrDefault(Directory.Exists);
    }

    public string? FindLatestCareer(string? settingsDirectory = null)
    {
        var directory = settingsDirectory ?? FindSettingsDirectory();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return null;
        return new DirectoryInfo(directory).EnumerateFiles("Career*")
            .Where(f => !f.Name.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name)
            .FirstOrDefault()?.FullName;
    }
}
