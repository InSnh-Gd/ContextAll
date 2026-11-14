using System.Collections.Immutable;
using System.Formats.Asn1;
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
    public static byte[]? LeafSpki(X509Certificate2 certificate)
    {
        if (!IsP256(certificate))
        {
            return null;
        }

        using var key = certificate.GetECDsaPublicKey();
        if (key is null)
        {
            return null;
        }

        return key.ExportSubjectPublicKeyInfo();
    }

    public static byte[]? LeafSpki(ReadOnlySpan<byte> certificateDer)
    {
        if (certificateDer.IsEmpty)
        {
            return null;
        }

        try
        {
            var certReader = new AsnReader(certificateDer.ToArray(), AsnEncodingRules.DER);
            var certSeq = certReader.ReadSequence();
            if (certReader.HasData)
            {
                return null;
            }

            var tbsSeq = certSeq.ReadSequence();
            var versionTag = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
            if (tbsSeq.HasData && tbsSeq.PeekTag().HasSameClassAndValue(versionTag))
            {
                _ = tbsSeq.ReadEncodedValue();
            }

            _ = tbsSeq.ReadIntegerBytes();
            _ = tbsSeq.ReadSequence();
            _ = tbsSeq.ReadSequence();
            _ = tbsSeq.ReadSequence();
            _ = tbsSeq.ReadSequence();

            var spkiBytes = tbsSeq.PeekEncodedValue().ToArray();
            var spkiSeq = tbsSeq.ReadSequence();
            var algSeq = spkiSeq.ReadSequence();
            var algOid = algSeq.ReadObjectIdentifier();
            var curveOid = algSeq.ReadObjectIdentifier();
            if (algSeq.HasData)
            {
                return null;
            }

            var keyBits = spkiSeq.ReadBitString(out var unusedBitCount);
            if (unusedBitCount != 0 || spkiSeq.HasData)
            {
                return null;
            }

            if (algOid != "1.2.840.10045.2.1" || curveOid != "1.2.840.10045.3.1.7")
            {
                return null;
            }

            if (keyBits.Length != 65 || keyBits[0] != 0x04)
            {
                return null;
            }

            _ = certSeq.ReadSequence();
            _ = certSeq.ReadBitString(out _);
            if (certSeq.HasData)
            {
                return null;
            }

            return spkiBytes;
        }
        catch (AsnContentException)
        {
            return null;
        }
    }

    public static Fin<byte[]> SpkiOfLeaf(X509Certificate2 certificate)
    {
        var bytes = LeafSpki(certificate);
        return bytes is null ? Fin.Fail<byte[]>("not-p256") : Fin.Succ(bytes);
    }

    public static Fin<byte[]> SpkiOfLeaf(ReadOnlySpan<byte> certificateDer)
    {
        var bytes = LeafSpki(certificateDer);
        return bytes is null ? Fin.Fail<byte[]>("not-p256") : Fin.Succ(bytes);
    }

    public static CertificateFate Evaluate(X509Certificate2 certificate, TrustEntry? entry) =>
        EvaluateSpki(LeafSpki(certificate), entry);

    public static CertificateFate Evaluate(ReadOnlySpan<byte> certificateDer, TrustEntry? entry) =>
        EvaluateSpki(LeafSpki(certificateDer), entry);

    static CertificateFate EvaluateSpki(byte[]? bytes, TrustEntry? entry)
    {
        if (bytes is null)
        {
            return CertificateFate.RejectBeforeId;
        }
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
