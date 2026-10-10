using System.Buffers.Binary;
using System.Text;

namespace IppTestClient.Ipp;

public sealed class IppDecodeException(string message) : Exception(message);

/// <summary>Parses IPP messages per RFC 8010 section 3, including collections (RFC 8010 3.1.6).</summary>
public static class IppDecoder
{
    /// <param name="data">The full HTTP body.</param>
    /// <param name="documentOffset">Where the attributes end and any document data begins.</param>
    public static IppMessage Decode(ReadOnlySpan<byte> data, out int documentOffset)
    {
        var r = new Reader(data);
        var message = new IppMessage
        {
            VersionMajor = r.Byte(),
            VersionMinor = r.Byte(),
            Code = r.Int16(),
            RequestId = r.Int32()
        };

        IppAttributeGroup? group = null;
        IppAttribute? current = null;

        while (true)
        {
            var tag = (IppTag)r.Byte();

            if (tag == IppTag.EndOfAttributes)
            {
                break;
            }

            if (tag.IsDelimiter())
            {
                // A group tag may repeat, e.g. one job-attributes group per job in Get-Jobs.
                group = new IppAttributeGroup(tag);
                message.Groups.Add(group);
                current = null;
                continue;
            }

            if (group is null)
            {
                throw new IppDecodeException("Attribute found before any group tag");
            }

            var name = r.String();

            if (tag == IppTag.BegCollection)
            {
                r.Bytes(r.Int16()); // value is normally empty
                var collection = ReadCollection(ref r);
                current = AddValue(group, current, name, tag, collection);
                continue;
            }

            var value = ReadValue(ref r, tag);
            current = AddValue(group, current, name, tag, value);
        }

        documentOffset = r.Position;
        return message;
    }

    public static IppMessage Decode(ReadOnlySpan<byte> data) => Decode(data, out _);

    private static IppAttribute AddValue(IppAttributeGroup group, IppAttribute? current, string name, IppTag tag, object value)
    {
        if (name.Length == 0)
        {
            // Additional value for the previous attribute.
            if (current is null) throw new IppDecodeException("Additional value without an attribute");
            current.Add(tag, value);
            return current;
        }

        var attribute = new IppAttribute(name, tag, value);
        group.Attributes.Add(attribute);
        return attribute;
    }

    private static IppCollection ReadCollection(ref Reader r)
    {
        var collection = new IppCollection();
        IppAttribute? member = null;
        string? pendingName = null;

        while (true)
        {
            var tag = (IppTag)r.Byte();
            r.String(); // name is always empty inside a collection

            switch (tag)
            {
                case IppTag.EndCollection:
                    r.Bytes(r.Int16());
                    return collection;

                case IppTag.MemberAttrName:
                    pendingName = Encoding.UTF8.GetString(r.Bytes(r.Int16()));
                    member = null;
                    break;

                case IppTag.BegCollection:
                    r.Bytes(r.Int16());
                    member = AddMemberValue(collection, member, ref pendingName, tag, ReadCollection(ref r));
                    break;

                default:
                    if (tag.IsDelimiter())
                        throw new IppDecodeException("Group tag inside a collection");
                    member = AddMemberValue(collection, member, ref pendingName, tag, ReadValue(ref r, tag));
                    break;
            }
        }
    }

    private static IppAttribute AddMemberValue(IppCollection collection, IppAttribute? member, ref string? pendingName, IppTag tag, object value)
    {
        if (pendingName is not null)
        {
            var attribute = new IppAttribute(pendingName, tag, value);
            collection.Add(attribute);
            pendingName = null;
            return attribute;
        }

        if (member is null) throw new IppDecodeException("Collection value without a member name");
        member.Add(tag, value);
        return member;
    }

    private static object ReadValue(ref Reader r, IppTag tag)
    {
        var length = r.Int16();
        if (length < 0) throw new IppDecodeException($"Negative value length for tag {tag}");
        var bytes = r.Bytes(length);

        if (tag.IsOutOfBand())
        {
            return new IppOutOfBand(tag);
        }

        switch (tag)
        {
            case IppTag.Integer:
            case IppTag.Enum:
                Expect(tag, length, 4);
                return BinaryPrimitives.ReadInt32BigEndian(bytes);

            case IppTag.Boolean:
                Expect(tag, length, 1);
                return bytes[0] != 0;

            case IppTag.DateTime:
                Expect(tag, length, 11);
                return ReadDateTime(bytes);

            case IppTag.Resolution:
                Expect(tag, length, 9);
                return new IppResolution(
                    BinaryPrimitives.ReadInt32BigEndian(bytes),
                    BinaryPrimitives.ReadInt32BigEndian(bytes[4..]),
                    bytes[8]);

            case IppTag.RangeOfInteger:
                Expect(tag, length, 8);
                return new IppRange(
                    BinaryPrimitives.ReadInt32BigEndian(bytes),
                    BinaryPrimitives.ReadInt32BigEndian(bytes[4..]));

            case IppTag.TextWithLanguage:
            case IppTag.NameWithLanguage:
                var inner = new Reader(bytes);
                var language = inner.String();
                var text = inner.String();
                return new IppStringWithLanguage(language, text);

            default:
                return tag.IsString() ? Encoding.UTF8.GetString(bytes) : bytes.ToArray();
        }
    }

    private static DateTimeOffset ReadDateTime(ReadOnlySpan<byte> b)
    {
        var year = BinaryPrimitives.ReadUInt16BigEndian(b);
        var offset = new TimeSpan(b[9], b[10], 0);
        if (b[8] == '-') offset = -offset;
        try
        {
            return new DateTimeOffset(year, b[2], b[3], b[4], b[5], Math.Min((int)b[6], 59), b[7] * 100, offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Some printers send zeroed dates before their clock is set.
            return DateTimeOffset.MinValue;
        }
    }

    private static void Expect(IppTag tag, int actual, int expected)
    {
        if (actual != expected)
            throw new IppDecodeException($"{tag} value must be {expected} bytes, got {actual}");
    }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        public int Position { get; private set; }

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || Position + count > _data.Length)
                throw new IppDecodeException($"Unexpected end of data at offset {Position}");
            var slice = _data.Slice(Position, count);
            Position += count;
            return slice;
        }

        public byte Byte() => Take(1)[0];
        public short Int16() => BinaryPrimitives.ReadInt16BigEndian(Take(2));
        public int Int32() => BinaryPrimitives.ReadInt32BigEndian(Take(4));
        public ReadOnlySpan<byte> Bytes(int count) => Take(count);
        public string String() => Encoding.UTF8.GetString(Take(Int16()));
    }
}
