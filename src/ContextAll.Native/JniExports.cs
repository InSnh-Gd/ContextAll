using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ContextAll.Native;

public static unsafe class JniExports
{
    const int SlotGetArrayLength = 171;
    const int SlotNewByteArray = 176;
    const int SlotGetByteArrayElements = 184;
    const int SlotReleaseByteArrayElements = 192;
    const int SlotSetByteArrayRegion = 208;
    const int JniAbort = 2;

    [UnmanagedCallersOnly(EntryPoint = "contextall_native_smoke", CallConvs = [typeof(CallConvCdecl)])]
    public static int NativeSmokeC() => NativeSessionBridge.RunSmoke();

    [UnmanagedCallersOnly(EntryPoint = "Java_app_contextall_nativebridge_NativeSessionJni_nativeRunSmoke", CallConvs = [typeof(CallConvCdecl)])]
    public static int JniRunSmoke(nint env, nint clazz)
    {
        _ = env;
        _ = clazz;
        return NativeSessionBridge.RunSmoke();
    }

    [UnmanagedCallersOnly(EntryPoint = "Java_app_contextall_nativebridge_NativeSessionJni_nativeIsRunning", CallConvs = [typeof(CallConvCdecl)])]
    public static byte JniIsRunning(nint env, nint clazz)
    {
        _ = env;
        _ = clazz;
        return NativeSessionBridge.Shared.IsRunning ? (byte)1 : (byte)0;
    }

    [UnmanagedCallersOnly(EntryPoint = "Java_app_contextall_nativebridge_NativeSessionJni_nativeStart", CallConvs = [typeof(CallConvCdecl)])]
    public static nint JniStart(
        nint env,
        nint clazz,
        nint noBackupDirUtf8,
        nint filesDirUtf8,
        nint displayNameUtf8,
        nint localPfxBytes,
        nint localSpkiBytes)
    {
        _ = clazz;
        try
        {
            var noBackup = ReadUtf8(env, noBackupDirUtf8) ?? "";
            var files = ReadUtf8(env, filesDirUtf8) ?? "";
            var name = ReadUtf8(env, displayNameUtf8) ?? "Android";
            var localPfx = ReadBytes(env, localPfxBytes);
            var localSpki = ReadBytes(env, localSpkiBytes);
            var json = NativeSessionBridge.Shared.Start(
                noBackup,
                files,
                name,
                localPfx: localPfx,
                localSpki: localSpki);
            return ToUtf8ByteArray(env, json);
        }
        catch (Exception ex)
        {
            return ToUtf8ByteArray(env, "{\"ok\":false,\"error\":\"" + EscapeJson(ex.Message) + "\"}");
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Java_app_contextall_nativebridge_NativeSessionJni_nativeStop", CallConvs = [typeof(CallConvCdecl)])]
    public static void JniStop(nint env, nint clazz)
    {
        _ = env;
        _ = clazz;
        NativeSessionBridge.Shared.Stop();
    }

    [UnmanagedCallersOnly(EntryPoint = "Java_app_contextall_nativebridge_NativeSessionJni_nativeResetIdentity", CallConvs = [typeof(CallConvCdecl)])]
    public static nint JniResetIdentity(
        nint env,
        nint clazz,
        nint noBackupDirUtf8,
        nint filesDirUtf8,
        nint displayNameUtf8,
        nint localPfxBytes,
        nint localSpkiBytes)
    {
        _ = clazz;
        try
        {
            var noBackup = ReadUtf8(env, noBackupDirUtf8) ?? "";
            var files = ReadUtf8(env, filesDirUtf8) ?? "";
            var name = ReadUtf8(env, displayNameUtf8) ?? "Android";
            var localPfx = ReadBytes(env, localPfxBytes);
            var localSpki = ReadBytes(env, localSpkiBytes);
            var json = NativeSessionBridge.Shared.ResetIdentity(
                noBackup,
                files,
                name,
                localPfx: localPfx,
                localSpki: localSpki);
            return ToUtf8ByteArray(env, json);
        }
        catch (Exception ex)
        {
            return ToUtf8ByteArray(env, "{\"ok\":false,\"error\":\"" + EscapeJson(ex.Message) + "\"}");
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Java_app_contextall_nativebridge_NativeSessionJni_nativeGetSnapshotJson", CallConvs = [typeof(CallConvCdecl)])]
    public static nint JniGetSnapshotJson(nint env, nint clazz)
    {
        _ = clazz;
        var json = NativeSessionBridge.Shared.GetSnapshotJson();
        return json is null ? 0 : ToUtf8ByteArray(env, json);
    }

    [UnmanagedCallersOnly(EntryPoint = "Java_app_contextall_nativebridge_NativeSessionJni_nativeSubmitCommandJson", CallConvs = [typeof(CallConvCdecl)])]
    public static nint JniSubmitCommandJson(nint env, nint clazz, int frameType, nint commandJsonUtf8)
    {
        _ = clazz;
        var commandJson = ReadUtf8(env, commandJsonUtf8) ?? "{}";
        if (frameType is < 0 or > byte.MaxValue)
        {
            return ToUtf8ByteArray(env, """{"requestId":"0","ok":false,"error":"invalid"}""");
        }

        var result = NativeSessionBridge.Shared.SubmitCommandJson((byte)frameType, commandJson);
        return ToUtf8ByteArray(env, result);
    }

    [UnmanagedCallersOnly(EntryPoint = "Java_app_contextall_nativebridge_NativeSessionJni_nativeConnectEndpoint", CallConvs = [typeof(CallConvCdecl)])]
    public static nint JniConnectEndpoint(nint env, nint clazz, nint hostUtf8, int port, nint aliasUtf8)
    {
        _ = clazz;
        var host = ReadUtf8(env, hostUtf8) ?? "";
        var alias = ReadUtf8(env, aliasUtf8);
        var result = NativeSessionBridge.Shared.ConnectEndpoint(host, port, alias);
        return ToUtf8ByteArray(env, result);
    }

    [UnmanagedCallersOnly(EntryPoint = "Java_app_contextall_nativebridge_NativeSessionJni_nativePollEventJson", CallConvs = [typeof(CallConvCdecl)])]
    public static nint JniPollEventJson(nint env, nint clazz, int timeoutMs)
    {
        _ = clazz;
        var json = NativeSessionBridge.Shared.PollEventJson(Math.Max(0, timeoutMs));
        return json is null ? 0 : ToUtf8ByteArray(env, json);
    }

    [UnmanagedCallersOnly(EntryPoint = "Java_app_contextall_nativebridge_NativeSessionJni_nativeCompleteInboundCommit", CallConvs = [typeof(CallConvCdecl)])]
    public static void JniCompleteInboundCommit(nint env, nint clazz, long commitId, byte ok, nint committedTargetUtf8)
    {
        _ = clazz;
        var target = ReadUtf8(env, committedTargetUtf8);
        NativeSessionBridge.Shared.CompleteInboundCommit(commitId, ok != 0, target);
    }

    [UnmanagedCallersOnly(EntryPoint = "Java_app_contextall_nativebridge_NativeSessionJni_nativePushClipboardText", CallConvs = [typeof(CallConvCdecl)])]
    public static void JniPushClipboardText(nint env, nint clazz, nint textUtf8)
    {
        _ = clazz;
        var text = ReadUtf8(env, textUtf8);
        if (text is not null)
        {
            NativeSessionBridge.Shared.PushClipboardText(text);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "Java_app_contextall_nativebridge_NativeSessionJni_nativeNotifyNetworkChanged", CallConvs = [typeof(CallConvCdecl)])]
    public static void JniNotifyNetworkChanged(nint env, nint clazz, nint prefixesCsvUtf8)
    {
        _ = clazz;
        var csv = ReadUtf8(env, prefixesCsvUtf8) ?? "";
        NativeSessionBridge.Shared.NotifyNetworkChanged(csv);
    }

    [UnmanagedCallersOnly(EntryPoint = "Java_app_contextall_nativebridge_NativeSessionJni_nativePushDiscoveredPeer", CallConvs = [typeof(CallConvCdecl)])]
    public static void JniPushDiscoveredPeer(nint env, nint clazz, nint deviceIdUtf8, nint addressUtf8, int port, nint displayNameUtf8)
    {
        _ = clazz;
        var deviceId = ReadUtf8(env, deviceIdUtf8) ?? "";
        var address = ReadUtf8(env, addressUtf8) ?? "";
        var displayName = ReadUtf8(env, displayNameUtf8);
        NativeSessionBridge.Shared.PushDiscoveredPeer(deviceId, address, port, displayName);
    }

    [UnmanagedCallersOnly(EntryPoint = "Java_app_contextall_nativebridge_NativeSessionJni_nativeEncodeLocalBeacon", CallConvs = [typeof(CallConvCdecl)])]
    public static nint JniEncodeLocalBeacon(nint env, nint clazz, int port, nint displayNameUtf8)
    {
        _ = clazz;
        if (port is <= 0 or > ushort.MaxValue)
        {
            return 0;
        }

        var name = ReadUtf8(env, displayNameUtf8);
        var encoded = NativeSessionBridge.Shared.EncodeLocalBeacon((ushort)port, name);
        return encoded is null ? 0 : ToByteArray(env, encoded);
    }

    [UnmanagedCallersOnly(EntryPoint = "Java_app_contextall_nativebridge_NativeSessionJni_nativeNotifyBeaconPacket", CallConvs = [typeof(CallConvCdecl)])]
    public static void JniNotifyBeaconPacket(nint env, nint clazz, nint packetBytes, nint sourceAddressUtf8)
    {
        _ = clazz;
        var packet = ReadBytes(env, packetBytes);
        var sourceAddress = ReadUtf8(env, sourceAddressUtf8);
        if (packet is not null && !string.IsNullOrWhiteSpace(sourceAddress))
        {
            NativeSessionBridge.Shared.NotifyBeaconPacket(packet, sourceAddress);
        }
    }

    static nint* VTable(nint env) => *(nint**)env;

    static byte[]? ReadBytes(nint env, nint jbyteArray)
    {
        if (env == 0 || jbyteArray == 0)
        {
            return null;
        }

        var vtable = VTable(env);
        var getLen = (delegate* unmanaged[Cdecl]<nint, nint, int>)vtable[SlotGetArrayLength];
        var len = getLen(env, jbyteArray);
        if (len <= 0)
        {
            return [];
        }

        var getElems = (delegate* unmanaged[Cdecl]<nint, nint, byte*, sbyte*>)vtable[SlotGetByteArrayElements];
        var releaseElems = (delegate* unmanaged[Cdecl]<nint, nint, sbyte*, int, void>)vtable[SlotReleaseByteArrayElements];
        var ptr = getElems(env, jbyteArray, null);
        if (ptr == null)
        {
            return null;
        }

        try
        {
            var result = new byte[len];
            new ReadOnlySpan<byte>(ptr, len).CopyTo(result);
            return result;
        }
        finally
        {
            releaseElems(env, jbyteArray, ptr, JniAbort);
        }
    }

    static string? ReadUtf8(nint env, nint jbyteArray)
    {
        var bytes = ReadBytes(env, jbyteArray);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    static nint ToByteArray(nint env, ReadOnlySpan<byte> bytes)
    {
        if (env == 0)
        {
            return 0;
        }

        var vtable = VTable(env);
        var newArray = (delegate* unmanaged[Cdecl]<nint, int, nint>)vtable[SlotNewByteArray];
        var array = newArray(env, bytes.Length);
        if (array == 0 || bytes.Length == 0)
        {
            return array;
        }

        var setRegion = (delegate* unmanaged[Cdecl]<nint, nint, int, int, sbyte*, void>)vtable[SlotSetByteArrayRegion];
        fixed (byte* ptr = bytes)
        {
            setRegion(env, array, 0, bytes.Length, (sbyte*)ptr);
        }

        return array;
    }

    static nint ToUtf8ByteArray(nint env, string value) =>
        ToByteArray(env, Encoding.UTF8.GetBytes(value));

    static string EscapeJson(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}

