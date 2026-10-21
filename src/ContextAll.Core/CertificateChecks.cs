using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LanguageExt;

namespace ContextAll.Core;

public sealed record TrustEntry(
    string DeviceId,
    string DisplayName,
    string SpkiBase64,
    bool ClipboardSync,
    bool AskBeforeSave);

public enum CertificateFate
{
    Continue,
    RejectBeforeId,
    CloseKeepTrust,
}

public static class CertificateChecks
{
    public static Fin<byte[]> SpkiOfLeaf(X509Certificate2 certificate)
    {
        if (!IsP256(certificate))
        {
            return Fin.Fail<byte[]>("not-p256");
        }

        using var key = certificate.GetECDsaPublicKey();
        if (key is null)
        {
            return Fin.Fail<byte[]>("not-p256");
        }

        return Fin.Succ(key.ExportSubjectPublicKeyInfo());
    }

    public static CertificateFate Evaluate(X509Certificate2 certificate, TrustEntry? entry)
    {
        var spki = SpkiOfLeaf(certificate);
        if (spki.IsFail)
        {
            return CertificateFate.RejectBeforeId;
        }

        var bytes = spki.Match(Succ: value => value, Fail: _ => Array.Empty<byte>());
        if (entry is null)
        {
            return CertificateFate.Continue;
        }

        byte[] stored;
        try
        {
            stored = Convert.FromBase64String(entry.SpkiBase64);
        }
        catch (FormatException)
        {
            return CertificateFate.CloseKeepTrust;
        }

        if (!stored.AsSpan().SequenceEqual(bytes))
        {
            return CertificateFate.CloseKeepTrust;
        }

        return CertificateFate.Continue;
    }

    public static bool IsP256(X509Certificate2 certificate)
    {
        using var key = certificate.GetECDsaPublicKey();
        if (key is null || key.KeySize != 256)
        {
            return false;
        }

        return key.ExportParameters(false).Curve.Oid?.Value == "1.2.840.10045.3.1.7";
    }
}

public sealed record ChannelState(bool Open, ImmutableArray<TrustEntry> Trust);

public static class ChannelRules
{
    public static ChannelState OnQuicFrame(ChannelState state, ReadOnlySpan<byte> bytes)
    {
        var read = Protocol.FrameCodec.Read(bytes);
        if (read.NeedMore)
        {
            return state;
        }

        if (read.IsFailure || read.Frame is null || read.Frame.Type >= 64)
        {
            return state with { Open = false };
        }

        return state;
    }
}
