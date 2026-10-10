using System.Globalization;
using System.Text;

namespace IppTestClient.Ipp;

public readonly record struct IppResolution(int CrossFeed, int Feed, byte Units)
{
    public override string ToString()
    {
        var unit = Units == 3 ? "dpi" : Units == 4 ? "dpcm" : $"u{Units}";
        return CrossFeed == Feed ? $"{CrossFeed}{unit}" : $"{CrossFeed}x{Feed}{unit}";
    }
}

public readonly record struct IppRange(int Lower, int Upper)
{
    public override string ToString() => $"{Lower}-{Upper}";
}

public readonly record struct IppStringWithLanguage(string Language, string Value)
{
    public override string ToString() => $"{Value} ({Language})";
}

/// <summary>Marker for out of band values (unsupported, unknown, no-value).</summary>
public readonly record struct IppOutOfBand(IppTag Tag)
{
    public override string ToString() => Tag switch
    {
        IppTag.Unsupported => "unsupported",
        IppTag.Unknown => "unknown",
        _ => "no-value"
    };
}

/// <summary>A collection value: an ordered set of member attributes.</summary>
public sealed class IppCollection : List<IppAttribute>
{
    public IppAttribute? this[string name] => Find(a => a.Name == name);

    public override string ToString() =>
        "{" + string.Join(" ", this.Select(a => $"{a.Name}={a.FormatValues()}")) + "}";
}

/// <summary>
/// One attribute with one or more values. All values share the attribute's
/// first tag except where IPP allows mixing (for example keyword or name).
/// </summary>
public sealed class IppAttribute
{
    public IppAttribute(string name, IppTag tag, params object[] values)
    {
        Name = name;
        Tag = tag;
        Values = [.. values];
        ValueTags = values.Select(_ => tag).ToList();
    }

    public string Name { get; }
    public IppTag Tag { get; }
    public List<object> Values { get; }

    /// <summary>Tag of each value (they can differ, e.g. keyword | name).</summary>
    public List<IppTag> ValueTags { get; }

    public void Add(IppTag tag, object value)
    {
        Values.Add(value);
        ValueTags.Add(tag);
    }

    public object? First => Values.Count > 0 ? Values[0] : null;

    public string? AsString() => First switch
    {
        string s => s,
        IppStringWithLanguage swl => swl.Value,
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        null => null,
        var other => other.ToString()
    };

    public int? AsInt() => First is int i ? i : null;

    public bool? AsBool() => First is bool b ? b : null;

    public IEnumerable<string> AsStrings() => Values.Select(v => v switch
    {
        string s => s,
        IppStringWithLanguage swl => swl.Value,
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
    });

    public IEnumerable<int> AsInts() => Values.OfType<int>();

    public string FormatValues()
    {
        var parts = Values.Select((v, i) => v switch
        {
            int n when ValueTags[i] == IppTag.Enum => IppEnums.Describe(Name, n),
            bool b => b ? "true" : "false",
            DateTimeOffset d => d.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            byte[] bytes => IsPrintable(bytes) ? Encoding.UTF8.GetString(bytes) : "0x" + Convert.ToHexString(bytes),
            _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
        });
        return string.Join(", ", parts);
    }

    private static bool IsPrintable(byte[] bytes) => bytes.All(b => b is >= 0x20 and < 0x7F);

    public override string ToString() => $"{Name} ({Tag}) = {FormatValues()}";
}

public sealed class IppAttributeGroup(IppTag tag)
{
    public IppTag Tag { get; } = tag;
    public List<IppAttribute> Attributes { get; } = [];

    public IppAttribute? this[string name] => Attributes.Find(a => a.Name == name);

    public IppAttributeGroup Add(string name, IppTag tag, params object[] values)
    {
        Attributes.Add(new IppAttribute(name, tag, values));
        return this;
    }
}

/// <summary>An IPP request or response (RFC 8010 section 3.1).</summary>
public sealed class IppMessage
{
    public byte VersionMajor { get; set; } = 2;
    public byte VersionMinor { get; set; } = 0;

    /// <summary>Operation id on a request, status code on a response.</summary>
    public short Code { get; set; }

    public int RequestId { get; set; } = 1;

    public List<IppAttributeGroup> Groups { get; } = [];

    public short StatusCode => Code;

    public bool IsSuccess => IppStatus.IsSuccess(Code);

    public string Version => $"{VersionMajor}.{VersionMinor}";

    public IppAttributeGroup Group(IppTag tag)
    {
        var group = Groups.Find(g => g.Tag == tag);
        if (group is null)
        {
            group = new IppAttributeGroup(tag);
            Groups.Add(group);
        }
        return group;
    }

    public IEnumerable<IppAttributeGroup> GroupsOf(IppTag tag) => Groups.Where(g => g.Tag == tag);

    /// <summary>Finds an attribute in the first group of the given type.</summary>
    public IppAttribute? Find(IppTag groupTag, string name) =>
        Groups.FirstOrDefault(g => g.Tag == groupTag)?[name];

    /// <summary>
    /// Starts a request with the three operation attributes every request needs,
    /// in the order the spec requires.
    /// </summary>
    public static IppMessage CreateRequest(IppOperation operation, int requestId, string? printerUri = null)
    {
        var message = new IppMessage { Code = (short)operation, RequestId = requestId };
        var op = message.Group(IppTag.OperationAttributes);
        op.Add("attributes-charset", IppTag.Charset, "utf-8");
        op.Add("attributes-natural-language", IppTag.NaturalLanguage, "en");
        if (printerUri is not null)
        {
            op.Add("printer-uri", IppTag.Uri, printerUri);
        }
        return message;
    }
}
