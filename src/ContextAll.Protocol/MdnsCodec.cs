using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace ContextAll.Protocol;

public sealed record MdnsService(string DeviceId, string? DisplayName, int Port, IPAddress Address);

public sealed record MdnsRead(IReadOnlyList<MdnsService> Services, bool ServiceQuery);

public static class MdnsCodec
{
    public const string ServiceName = "_contextall._udp.local";

    const ushort TypeA = 1;
    const ushort TypePtr = 12;
    const ushort TypeTxt = 16;
    const ushort TypeAaaa = 28;
    const ushort TypeSrv = 33;
    const ushort ClassIn = 1;
    const ushort ResponseFlags = 0x8400;
    const int MaxRecords = 256;
    const int MaxNameBytes = 255;

    public static byte[] EncodeQuery(ushort id)
    {
        var bytes = new List<byte>(64);
        WriteHeader(bytes, id, 0, questions: 1, answers: 0, authority: 0, additional: 0);
        WriteName(bytes, ServiceName);
        WriteU16(bytes, TypePtr);
        WriteU16(bytes, ClassIn);
        return bytes.ToArray();
    }

    public static byte[] EncodeResponse(string deviceId, string? displayName, int port, IPAddress? address, ushort id = 0)
    {
        var instance = InstanceName(deviceId);
        var answers = new List<byte>();
        WriteName(answers, ServiceName);
        WriteU16(answers, TypePtr);
        WriteU16(answers, ClassIn);
        WriteU32(answers, 120);
        var target = new List<byte>();
        WriteName(target, instance);
        WriteU16(answers, (ushort)target.Count);
        answers.AddRange(target);

        var additional = new List<byte>();
        WriteName(additional, instance);
        WriteU16(additional, TypeSrv);
        WriteU16(additional, ClassIn);
        WriteU32(additional, 120);
        var srv = new List<byte>();
        WriteU16(srv, 0);
        WriteU16(srv, 0);
        WriteU16(srv, (ushort)port);
        WriteName(srv, instance);
        WriteU16(additional, (ushort)srv.Count);
        additional.AddRange(srv);

        WriteName(additional, instance);
        WriteU16(additional, TypeTxt);
        WriteU16(additional, ClassIn);
        WriteU32(additional, 120);
        var txt = new List<byte>();
        WriteTxt(txt, "id=" + deviceId);
        if (!string.IsNullOrEmpty(displayName))
        {
            WriteTxt(txt, "name=" + displayName);
        }

        WriteU16(additional, (ushort)txt.Count);
        additional.AddRange(txt);

        if (address is not null)
        {
            var raw = address.GetAddressBytes();
            WriteName(additional, instance);
            WriteU16(additional, raw.Length == 4 ? TypeA : TypeAaaa);
            WriteU16(additional, ClassIn);
            WriteU32(additional, 120);
            WriteU16(additional, (ushort)raw.Length);
            additional.AddRange(raw);
        }

        var bytes = new List<byte>(answers.Count + additional.Count + 16);
        WriteHeader(bytes, id, ResponseFlags, questions: 0, answers: 1, authority: 0, additional: (ushort)(2 + (address is null ? 0 : 1)));
        bytes.AddRange(answers);
        bytes.AddRange(additional);
        return bytes.ToArray();
    }

    public static MdnsRead Read(ReadOnlySpan<byte> packet, IPAddress source)
    {
        if (packet.Length < 12)
        {
            return new MdnsRead([], false);
        }

        var questionCount = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(4));
        var answerCount = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(6));
        var authorityCount = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(8));
        var additionalCount = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(10));
        var total = (int)questionCount + answerCount + authorityCount + additionalCount;
        if (total > MaxRecords)
        {
            return new MdnsRead([], false);
        }

        var offset = 12;
        var serviceQuery = false;
        for (var i = 0; i < questionCount; i++)
        {
            if (!TryReadName(packet, ref offset, out var name) || offset + 4 > packet.Length)
            {
                return new MdnsRead([], false);
            }

            var type = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset));
            if (string.Equals(name, ServiceName, StringComparison.OrdinalIgnoreCase) && type is TypePtr or 255)
            {
                serviceQuery = true;
            }

            offset += 4;
        }

        var pointers = new List<string>();
        var srv = new Dictionary<string, (int Port, string Target)>(StringComparer.OrdinalIgnoreCase);
        var txt = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var addresses = new Dictionary<string, IPAddress>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < answerCount + authorityCount + additionalCount; i++)
        {
            if (!TryReadName(packet, ref offset, out var name) || offset + 10 > packet.Length)
            {
                return new MdnsRead([], serviceQuery);
            }

            var type = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset));
            var length = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset + 8));
            offset += 10;
            if (offset + length > packet.Length)
            {
                return new MdnsRead([], serviceQuery);
            }

            var dataStart = offset;
            var dataEnd = offset + length;
            var data = packet.Slice(dataStart, length);
            offset = dataEnd;
            switch (type)
            {
                case TypePtr:
                {
                    var inner = dataStart;
                    if (TryReadName(packet, ref inner, out var target, dataEnd))
                    {
                        pointers.Add(target);
                    }

                    break;
                }

                case TypeSrv:
                {
                    if (data.Length < 6)
                    {
                        break;
                    }

                    var port = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4));
                    var inner = dataStart + 6;
                    if (TryReadName(packet, ref inner, out var target, dataEnd))
                    {
                        srv[name] = (port, target);
                    }

                    break;
                }

                case TypeTxt:
                {
                    txt[name] = ReadTxt(data);
                    break;
                }

                case TypeA when data.Length == 4:
                case TypeAaaa when data.Length == 16:
                {
                    addresses[name] = new IPAddress(data.ToArray());
                    break;
                }
            }
        }

        var services = new List<MdnsService>();
        foreach (var instance in pointers.Concat(srv.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!srv.TryGetValue(instance, out var record) || !txt.TryGetValue(instance, out var fields))
            {
                continue;
            }

            var id = Field(fields, "id");
            if (id is null || !IsDeviceId(id))
            {
                continue;
            }

            var name = Field(fields, "name");
            var address = addresses.TryGetValue(record.Target, out var found) ? found : source;
            services.Add(new MdnsService(id, name, record.Port, address));
        }

        return new MdnsRead(services, serviceQuery);
    }

    static string InstanceName(string deviceId) => deviceId + "." + ServiceName;

    static bool IsDeviceId(string value) =>
        value.Length == 29 && value.StartsWith("ca1", StringComparison.Ordinal) && value.Skip(3).All(ch => ch is >= 'a' and <= 'z' or >= '2' and <= '7');

    static string? Field(string[] fields, string key)
    {
        foreach (var field in fields)
        {
            if (field.StartsWith(key + "=", StringComparison.Ordinal))
            {
                return field[(key.Length + 1)..];
            }
        }

        return null;
    }

    static string[] ReadTxt(ReadOnlySpan<byte> data)
    {
        var values = new List<string>();
        var offset = 0;
        while (offset < data.Length)
        {
            var length = data[offset++];
            if (offset + length > data.Length)
            {
                return [];
            }

            values.Add(Encoding.UTF8.GetString(data.Slice(offset, length)));
            offset += length;
        }

        return values.ToArray();
    }

    static bool TryReadName(ReadOnlySpan<byte> data, ref int offset, out string name, int limit = -1)
    {
        var labels = new List<string>();
        var position = offset;
        var end = offset;
        var jumped = false;
        var hops = 0;
        var total = 0;
        while (true)
        {
            if (position >= data.Length || (limit >= 0 && position >= limit))
            {
                name = "";
                return false;
            }

            var length = data[position];
            if ((length & 0xC0) == 0xC0)
            {
                if (position + 1 >= data.Length || ++hops > 32)
                {
                    name = "";
                    return false;
                }

                var pointer = ((length & 0x3F) << 8) | data[position + 1];
                if (pointer >= data.Length)
                {
                    name = "";
                    return false;
                }

                if (!jumped)
                {
                    end = position + 2;
                    jumped = true;
                }

                position = pointer;
                limit = -1;
                continue;
            }

            if ((length & 0xC0) != 0)
            {
                name = "";
                return false;
            }

            position++;
            if (length == 0)
            {
                break;
            }

            if (length > 63 || position + length > data.Length || (limit >= 0 && position + length > limit))
            {
                name = "";
                return false;
            }

            total += length + 1;
            if (total > MaxNameBytes)
            {
                name = "";
                return false;
            }

            labels.Add(Encoding.ASCII.GetString(data.Slice(position, length)));
            position += length;
        }

        if (!jumped)
        {
            end = position;
        }

        offset = end;
        name = string.Join('.', labels);
        return true;
    }

    static void WriteHeader(List<byte> bytes, ushort id, ushort flags, ushort questions, ushort answers, ushort authority, ushort additional)
    {
        WriteU16(bytes, id);
        WriteU16(bytes, flags);
        WriteU16(bytes, questions);
        WriteU16(bytes, answers);
        WriteU16(bytes, authority);
        WriteU16(bytes, additional);
    }

    static void WriteName(List<byte> bytes, string name)
    {
        foreach (var label in name.Split('.'))
        {
            if (label.Length == 0)
            {
                continue;
            }

            if (label.Length > 63)
            {
                throw new ArgumentException("label", nameof(name));
            }

            var encoded = Encoding.ASCII.GetBytes(label);
            bytes.Add((byte)encoded.Length);
            bytes.AddRange(encoded);
        }

        bytes.Add(0);
    }

    static void WriteTxt(List<byte> bytes, string value)
    {
        var encoded = Encoding.UTF8.GetBytes(value);
        if (encoded.Length > 255)
        {
            throw new ArgumentException("txt", nameof(value));
        }

        bytes.Add((byte)encoded.Length);
        bytes.AddRange(encoded);
    }

    static void WriteU16(List<byte> bytes, ushort value)
    {
        Span<byte> raw = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(raw, value);
        bytes.AddRange(raw.ToArray());
    }

    static void WriteU32(List<byte> bytes, uint value)
    {
        Span<byte> raw = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(raw, value);
        bytes.AddRange(raw.ToArray());
    }
}
