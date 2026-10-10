using IppTestClient.Ipp;
using Xunit;

namespace IppTestClient.Tests;

public class EncodingTests
{
    [Fact]
    public void Encodes_get_printer_attributes_request_byte_for_byte()
    {
        var request = IppMessage.CreateRequest(IppOperation.GetPrinterAttributes, 1, "ipp://p/ipp/print");
        request.VersionMinor = 0;

        var hex = Convert.ToHexString(IppEncoder.Encode(request));

        var expected =
            "0200" + "000B" + "00000001" +            // version 2.0, Get-Printer-Attributes, request-id 1
            "01" +                                    // operation-attributes-tag
            "47" + "0012" + Hex("attributes-charset") + "0005" + Hex("utf-8") +
            "48" + "001B" + Hex("attributes-natural-language") + "0002" + Hex("en") +
            "45" + "000B" + Hex("printer-uri") + "0011" + Hex("ipp://p/ipp/print") +
            "03";                                     // end-of-attributes-tag

        Assert.Equal(expected, hex);
    }

    [Fact]
    public void Multiple_values_use_empty_names()
    {
        var message = new IppMessage { Code = 0, RequestId = 9 };
        message.Group(IppTag.OperationAttributes).Add("requested-attributes", IppTag.Keyword, "a", "bc");

        var hex = Convert.ToHexString(IppEncoder.Encode(message));

        Assert.Contains("44" + "0014" + Hex("requested-attributes") + "0001" + Hex("a") + "44" + "0000" + "0002" + Hex("bc"), hex);
    }

    [Fact]
    public void Round_trips_every_value_type()
    {
        var date = new DateTimeOffset(2026, 10, 10, 14, 30, 5, 300, TimeSpan.FromHours(5));
        var message = new IppMessage { VersionMajor = 1, VersionMinor = 1, Code = 0x0001, RequestId = 42 };
        message.Group(IppTag.PrinterAttributes)
            .Add("i", IppTag.Integer, -5)
            .Add("e", IppTag.Enum, 3)
            .Add("b", IppTag.Boolean, true)
            .Add("d", IppTag.DateTime, date)
            .Add("r", IppTag.Resolution, new IppResolution(600, 1200, 3))
            .Add("g", IppTag.RangeOfInteger, new IppRange(1, 999))
            .Add("t", IppTag.TextWithLanguage, new IppStringWithLanguage("en-us", "Hello"))
            .Add("k", IppTag.Keyword, "one", "two")
            .Add("o", IppTag.OctetString, new byte[] { 1, 2, 3 })
            .Add("n", IppTag.NoValue, new IppOutOfBand(IppTag.NoValue))
            .Add("u", IppTag.Uri, "ipp://x/ipp/print");

        var decoded = IppDecoder.Decode(IppEncoder.Encode(message));
        var p = decoded.Groups.Single();

        Assert.Equal("1.1", decoded.Version);
        Assert.Equal(42, decoded.RequestId);
        Assert.Equal(-5, p["i"]!.AsInt());
        Assert.Equal(3, p["e"]!.AsInt());
        Assert.Equal(true, p["b"]!.AsBool());
        Assert.Equal(date.AddMilliseconds(-date.Millisecond + 300), (DateTimeOffset)p["d"]!.First!);
        Assert.Equal(new IppResolution(600, 1200, 3), p["r"]!.First);
        Assert.Equal(new IppRange(1, 999), p["g"]!.First);
        Assert.Equal("Hello", p["t"]!.AsString());
        Assert.Equal(new[] { "one", "two" }, p["k"]!.AsStrings().ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])p["o"]!.First!);
        Assert.Equal(new IppOutOfBand(IppTag.NoValue), p["n"]!.First);
        Assert.Equal("ipp://x/ipp/print", p["u"]!.AsString());
    }

    [Fact]
    public void Round_trips_nested_collections()
    {
        var size = new IppCollection
        {
            new IppAttribute("x-dimension", IppTag.Integer, 21000),
            new IppAttribute("y-dimension", IppTag.Integer, 29700)
        };
        var mediaCol = new IppCollection
        {
            new IppAttribute("media-size", IppTag.BegCollection, size),
            new IppAttribute("media-type", IppTag.Keyword, "stationery"),
            new IppAttribute("media-source", IppTag.Keyword, "tray-1", "tray-2")
        };

        var message = new IppMessage();
        message.Group(IppTag.PrinterAttributes).Add("media-col-default", IppTag.BegCollection, mediaCol);

        var decoded = IppDecoder.Decode(IppEncoder.Encode(message));
        var col = Assert.IsType<IppCollection>(decoded.Groups[0]["media-col-default"]!.First);

        var decodedSize = Assert.IsType<IppCollection>(col["media-size"]!.First);
        Assert.Equal(21000, decodedSize["x-dimension"]!.AsInt());
        Assert.Equal(29700, decodedSize["y-dimension"]!.AsInt());
        Assert.Equal("stationery", col["media-type"]!.AsString());
        Assert.Equal(new[] { "tray-1", "tray-2" }, col["media-source"]!.AsStrings().ToArray());
    }

    [Fact]
    public void Collection_with_multiple_values_round_trips()
    {
        // media-col-database is a 1setOf collection
        var a = new IppCollection { new IppAttribute("media-key", IppTag.Keyword, "a4") };
        var b = new IppCollection { new IppAttribute("media-key", IppTag.Keyword, "letter") };
        var message = new IppMessage();
        message.Group(IppTag.PrinterAttributes).Add("media-col-database", IppTag.BegCollection, a, b);

        var decoded = IppDecoder.Decode(IppEncoder.Encode(message));
        var values = decoded.Groups[0]["media-col-database"]!.Values;

        Assert.Equal(2, values.Count);
        Assert.Equal("letter", ((IppCollection)values[1])["media-key"]!.AsString());
    }

    [Fact]
    public void Repeated_group_tags_become_separate_groups()
    {
        var message = new IppMessage();
        message.Groups.Add(new IppAttributeGroup(IppTag.OperationAttributes).Add("attributes-charset", IppTag.Charset, "utf-8"));
        message.Groups.Add(new IppAttributeGroup(IppTag.JobAttributes).Add("job-id", IppTag.Integer, 1));
        message.Groups.Add(new IppAttributeGroup(IppTag.JobAttributes).Add("job-id", IppTag.Integer, 2));

        var decoded = IppDecoder.Decode(IppEncoder.Encode(message));

        Assert.Equal(new[] { 1, 2 }, decoded.GroupsOf(IppTag.JobAttributes).Select(g => g["job-id"]!.AsInt()!.Value).ToArray());
    }

    [Fact]
    public void Reports_document_offset_after_attributes()
    {
        var header = IppEncoder.Encode(IppMessage.CreateRequest(IppOperation.PrintJob, 1, "ipp://p/"));
        byte[] body = [.. header, 0x25, 0x50, 0x44, 0x46]; // "%PDF"

        IppDecoder.Decode(body, out var offset);

        Assert.Equal(header.Length, offset);
    }

    [Fact]
    public void Truncated_data_throws_decode_exception()
    {
        var bytes = IppEncoder.Encode(IppMessage.CreateRequest(IppOperation.GetJobs, 1, "ipp://p/"));
        Assert.Throws<IppDecodeException>(() => IppDecoder.Decode(bytes.AsSpan(0, bytes.Length - 5)));
    }

    [Theory]
    [InlineData("ipp://printer/ipp/print", "http://printer:631/ipp/print")]
    [InlineData("ipps://printer/ipp/print", "https://printer:631/ipp/print")]
    [InlineData("ipp://printer:8631/ipp/print", "http://printer:8631/ipp/print")]
    [InlineData("http://server/printers/q", "http://server/printers/q")]
    public void Maps_ipp_uris_to_http(string input, string expected)
    {
        Assert.Equal(expected, IppClient.ToHttpUri(new Uri(input)).ToString());
    }

    [Fact]
    public void Status_names()
    {
        Assert.Equal("client-error-document-format-not-supported", IppStatus.Name(0x040A));
        Assert.True(IppStatus.IsSuccess(0x0001));
        Assert.False(IppStatus.IsSuccess(0x0400));
        Assert.Equal("Get-Printer-Attributes", IppEnums.Operation(0x000B));
    }

    private static string Hex(string ascii) => Convert.ToHexString(System.Text.Encoding.ASCII.GetBytes(ascii));
}
