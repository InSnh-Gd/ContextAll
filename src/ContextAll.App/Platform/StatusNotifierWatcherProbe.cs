using Tmds.DBus.Protocol;

namespace ContextAll.App.Platform;

/// <summary>Probes whether a desktop StatusNotifierWatcher D-Bus host is currently active.</summary>
public interface IStatusNotifierProbe
{
    Task<bool> IsWatcherAvailableAsync(CancellationToken ct = default);
}

/// <summary>Determines whether closing the main window should hide to the system tray instead of exiting.</summary>
public static class TrayClosePolicy
{
    public static bool ShouldHideToTray(bool trayEnabledInPreferences, bool watcherAvailable) =>
        trayEnabledInPreferences && watcherAvailable;
}

/// <summary>Windows NotifyIcon / Avalonia TrayIcon is always present, unlike Linux StatusNotifierWatcher.</summary>
public sealed class AlwaysAvailableStatusNotifierProbe : IStatusNotifierProbe
{
    public Task<bool> IsWatcherAvailableAsync(CancellationToken ct = default) =>
        Task.FromResult(true);
}

/// <summary>Queries the session D-Bus daemon for StatusNotifierWatcher ownership.</summary>
public sealed class DbusStatusNotifierProbe : IStatusNotifierProbe
{
    private const string DbusService = "org.freedesktop.DBus";
    private const string DbusPath = "/org/freedesktop/DBus";
    private const string DbusInterface = "org.freedesktop.DBus";

    public async Task<bool> IsWatcherAvailableAsync(CancellationToken ct = default)
    {
        var address = DBusAddress.Session;
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        try
        {
            using var connection = new DBusConnection(address);
            await connection.ConnectAsync().AsTask().WaitAsync(ct).ConfigureAwait(false);

            if (await NameHasOwnerAsync(connection, "org.kde.StatusNotifierWatcher", ct).ConfigureAwait(false))
            {
                return true;
            }

            return await NameHasOwnerAsync(connection, "org.freedesktop.StatusNotifierWatcher", ct).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> NameHasOwnerAsync(DBusConnection connection, string name, CancellationToken ct)
    {
        var message = BuildNameHasOwnerMessage(connection, name);
        return await connection.CallMethodAsync(
            message,
            static (Message reply, object? _) => reply.GetBodyReader().ReadBool(),
            null).WaitAsync(ct).ConfigureAwait(false);
    }

    private static MessageBuffer BuildNameHasOwnerMessage(DBusConnection connection, string name)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(DbusService, DbusPath, DbusInterface, "NameHasOwner", "s", MessageFlags.None);
        writer.WriteString(name);
        return writer.CreateMessage();
    }
}
