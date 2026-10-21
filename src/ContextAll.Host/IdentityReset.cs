using ContextAll.Core;

namespace ContextAll.Host;

public static class IdentityReset
{
    public static int Run(
        string identityDirectory,
        string settingsPath,
        string transferDirectory,
        bool interactive,
        bool confirmed,
        bool operatorSaidYes)
    {
        if (!interactive && !confirmed)
        {
            return 1;
        }

        if (interactive && !confirmed && !operatorSaidYes)
        {
            return 1;
        }

        var identity = IdentityStore.Start(identityDirectory);
        var settings = SettingsStore.Start(settingsPath, DisplayNames.Initial(null));
        var keptName = settings.Document?.DisplayName;
        if (string.IsNullOrEmpty(keptName) || !DisplayNames.IsLegal(keptName))
        {
            keptName = DisplayNames.Initial(null);
        }

        if (identity.Ok && !settings.Ok)
        {
            SettingsStore.Write(settingsPath, new SettingsDocument
            {
                DisplayName = keptName,
                DownloadDirectory = "",
                Trust = [],
            });
        }
        else
        {
            IdentityStore.Replace(identityDirectory);
            SettingsStore.Write(settingsPath, new SettingsDocument
            {
                DisplayName = keptName,
                DownloadDirectory = settings.Document?.DownloadDirectory ?? "",
                Trust = [],
            });
        }

        if (Directory.Exists(transferDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(transferDirectory))
            {
                File.Delete(file);
            }
        }

        return 0;
    }
}
