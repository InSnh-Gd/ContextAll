using System.Text.Json;
using System.Text.Json.Serialization;
using ContextAll.Core;
using ContextAll.Protocol;
using ContextAll.Session.Abstractions;

namespace ContextAll.Session;

public sealed class ClipboardGroup
{
    readonly string localId;
    readonly string path;
    readonly ClipboardGroupState state;

    public ClipboardGroup(string localId, string directory)
    {
        this.localId = localId;
        path = Path.Combine(directory, "clipboard-group.json");
        try
        {
            state = File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllBytes(path), ClipboardGroupJson.Default.ClipboardGroupState) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            state = new();
        }
    }

    public string? Owner => state.Owner;
    public string? Group => state.Group;
    public long Since => state.Since;
    public bool IsOwner => Owner == localId;
    public bool Enabled => Owner is not null;

    public string? Configure(string? owner)
    {
        if (owner == Owner) return null;
        state.Owner = owner;
        state.Group = owner == localId ? Guid.NewGuid().ToString("N") : null;
        state.Since = Now();
        state.Members.Clear();
        state.Invited.Clear();
        Save();
        return null;
    }

    public ClipboardGroupPermit? Admit(string peer, bool allowed)
    {
        if (!IsOwner || Group is not { } group || !allowed)
        {
            return null;
        }

        if (!state.Members.TryGetValue(peer, out var since))
        {
            since = Now();
            state.Members[peer] = since;
            Save();
        }

        return new ClipboardGroupPermit(group, since);
    }

    public long? MemberSince(string peer) => state.Members.TryGetValue(peer, out var since) ? since : null;

    public bool IsInvited(string peer) => state.Invited.Contains(peer);

    public void Invite(string peer, bool allowed)
    {
        if (allowed)
        {
            if (state.Invited.Add(peer)) Save();
        }
        else
        {
            Revoke(peer);
        }
    }

    public void Revoke(string peer)
    {
        var removed = state.Members.Remove(peer);
        if (state.Invited.Remove(peer) || removed) Save();
    }

    public void Leave(string peer)
    {
        if (state.Members.Remove(peer)) Save();
    }

    public bool Accept(string peer, ClipboardGroupPermit permit)
    {
        if (IsOwner || peer != Owner || permit.Request ||
            (permit.Group is not null && (!Guid.TryParseExact(permit.Group, "N", out _) || permit.Since < 0)))
        {
            return false;
        }

        if (Group != permit.Group || (permit.Group is not null && Since != permit.Since))
        {
            state.Group = permit.Group;
            state.Since = permit.Since;
            Save();
        }

        return permit.Group is not null;
    }

    public bool Allows(string peer, ClipboardGroupItem item, bool active)
    {
        if (!active || Group is null || item.Group != Group || item.Sequence <= 0 || item.Counter == uint.MaxValue ||
            item.Milliseconds < 0 || item.Milliseconds > Now() + 60_000 ||
            (item.Mime is null ? item.Bytes is < 0 or > FrameLimits.MaxClipboardBytes : !ClipboardAttachment.IsValid(item.Mime, item.Name, item.Bytes)) ||
            item.Sha256 is not { Length: 64 } ||
            !item.Sha256.All(Uri.IsHexDigit) || item.Origin is not { Length: 29 } ||
            !item.Origin.StartsWith("ca1", StringComparison.Ordinal))
        {
            return false;
        }

        if (IsOwner)
        {
            return item.Origin == peer && MemberSince(peer) is long since && item.Milliseconds >= since;
        }

        if (peer != Owner || item.Origin == localId || item.Milliseconds < Since || item.RelayedAt < Since)
        {
            return false;
        }

        try
        {
            return Base32.DigestFromDeviceId(item.Origin).Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    void Save()
    {
        if (Path.GetDirectoryName(path) is { } directory)
        {
            Directory.CreateDirectory(directory);
            IdentityStore.SetDirectoryMode(directory);
        }

        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(state, ClipboardGroupJson.Default.ClipboardGroupState));
        IdentityStore.SetFileMode(temporary);
        File.Move(temporary, path, overwrite: true);
    }

    static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

internal sealed class ClipboardGroupState
{
    public string? Owner { get; set; }
    public string? Group { get; set; }
    public long Since { get; set; }
    public Dictionary<string, long> Members { get; set; } = [];
    public HashSet<string> Invited { get; set; } = [];
}

[JsonSerializable(typeof(ClipboardGroupState))]
internal partial class ClipboardGroupJson : JsonSerializerContext;
