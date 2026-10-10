using System.Buffers.Binary;
using System.Text;

namespace IppTestClient.Ipp;

/// <summary>Binary encoding of IPP messages per RFC 8010 section 3.</summary>
public static class IppEncoder
{
    public static byte[] Encode(IppMessage message)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriterBE(ms);

        w.Byte(message.VersionMajor);
        w.Byte(message.VersionMinor);
        w.Int16(message.Code);
        w.Int32(message.RequestId);

        foreach (var group in message.Groups)
        {
            w.Byte((byte)group.Tag);
            foreach (var attribute in group.Attributes)
            {
                WriteAttribute(w, attribute, attribute.Name);
            }
        }

        w.Byte((byte)IppTag.EndOfAttributes);
        return ms.ToArray();
    }

    private static void WriteAttribute(BinaryWriterBE w, IppAttribute attribute, string name)
    {
        for (var i = 0; i < attribute.Values.Count; i++)
        {
            // Only the first value carries the name; extra values use name-length 0.
            WriteValue(w, attribute.ValueTags[i], i == 0 ? name : "", attribute.Values[i]);
        }

        if (attribute.Values.Count == 0)
        {
            WriteValue(w, IppTag.NoValue, name, new IppOutOfBand(IppTag.NoValue));
        }
    }

    private static void WriteValue(BinaryWriterBE w, IppTag tag, string name, object value)
    {
        if (value is IppCollection collection)
        {
            WriteCollection(w, name, collection);
            return;
        }

        w.Byte((byte)tag);
        w.String16(name);

        switch (value)
        {
            case IppOutOfBand:
                w.Int16(0);
                break;

            case int n:
                w.Int16(4);
                w.Int32(n);
                break;

            case bool b:
                w.Int16(1);
                w.Byte(b ? (byte)1 : (byte)0);
                break;

            case DateTimeOffset d:
                w.Int16(11);
                WriteDateTime(w, d);
                break;

            case IppResolution r:
                w.Int16(9);
                w.Int32(r.CrossFeed);
                w.Int32(r.Feed);
                w.Byte(r.Units);
                break;

            case IppRange r:
                w.Int16(8);
                w.Int32(r.Lower);
                w.Int32(r.Upper);
                break;

            case IppStringWithLanguage s:
                var lang = Encoding.UTF8.GetBytes(s.Language);
                var text = Encoding.UTF8.GetBytes(s.Value);
                w.Int16((short)(4 + lang.Length + text.Length));
                w.Bytes16(lang);
                w.Bytes16(text);
                break;

            case string s:
                w.String16(s);
                break;

            case byte[] bytes:
                w.Bytes16(bytes);
                break;

            default:
                throw new NotSupportedException($"Cannot encode {value.GetType().Name} for tag {tag}");
        }
    }

    private static void WriteCollection(BinaryWriterBE w, string name, IppCollection collection)
    {
        w.Byte((byte)IppTag.BegCollection);
        w.String16(name);
        w.Int16(0);

        foreach (var member in collection)
        {
            // memberAttrName: name-length 0, the member name is the value.
            w.Byte((byte)IppTag.MemberAttrName);
            w.Int16(0);
            w.String16(member.Name);
            WriteAttribute(w, member, "");
        }

        w.Byte((byte)IppTag.EndCollection);
        w.Int16(0);
        w.Int16(0);
    }

    private static void WriteDateTime(BinaryWriterBE w, DateTimeOffset d)
    {
        // RFC 2579 DateAndTime, 11 octet form.
        w.Int16((short)d.Year);
        w.Byte((byte)d.Month);
        w.Byte((byte)d.Day);
        w.Byte((byte)d.Hour);
        w.Byte((byte)d.Minute);
        w.Byte((byte)d.Second);
        w.Byte((byte)(d.Millisecond / 100));
        var offset = d.Offset;
        w.Byte((byte)(offset < TimeSpan.Zero ? '-' : '+'));
        w.Byte((byte)Math.Abs(offset.Hours));
        w.Byte((byte)Math.Abs(offset.Minutes));
    }

    private sealed class BinaryWriterBE(Stream stream)
    {
        private readonly byte[] _buffer = new byte[4];

        public void Byte(byte value) => stream.WriteByte(value);

        public void Int16(short value)
        {
            BinaryPrimitives.WriteInt16BigEndian(_buffer, value);
            stream.Write(_buffer, 0, 2);
        }

        public void Int32(int value)
        {
            BinaryPrimitives.WriteInt32BigEndian(_buffer, value);
            stream.Write(_buffer, 0, 4);
        }

        public void Bytes16(byte[] bytes)
        {
            if (bytes.Length > short.MaxValue) throw new ArgumentException("IPP value longer than 32767 bytes");
            Int16((short)bytes.Length);
            stream.Write(bytes);
        }

        public void String16(string value) => Bytes16(Encoding.UTF8.GetBytes(value));
    }
}
