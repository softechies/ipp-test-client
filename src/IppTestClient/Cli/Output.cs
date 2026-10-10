using System.Globalization;
using System.Text;
using IppTestClient.Ipp;

namespace IppTestClient.Cli;

/// <summary>Turns IPP responses into readable console text.</summary>
public static class Output
{
    public static string PrinterSummary(IppMessage response)
    {
        var p = response.GroupsOf(IppTag.PrinterAttributes).FirstOrDefault()
                ?? throw new InvalidOperationException("Response has no printer attributes");

        string? S(string name) => p[name]?.AsString();
        string L(string name, string fallback = "") =>
            p[name] is { } a ? string.Join(", ", a.AsStrings()) : fallback;

        var sb = new StringBuilder();
        void Row(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"{label,-17} {value}");
        }

        Row("Printer", S("printer-make-and-model") ?? S("printer-name"));
        Row("Name", S("printer-name"));
        Row("Info", S("printer-info"));
        Row("Location", S("printer-location"));

        var state = p["printer-state"]?.AsInt() is { } st ? IppEnums.PrinterState(st) : "unknown";
        var reasons = L("printer-state-reasons", "none");
        Row("State", reasons == "none" ? state : $"{state} ({reasons})");
        Row("State message", S("printer-state-message"));
        Row("Accepting jobs", p["printer-is-accepting-jobs"]?.AsBool() is { } accepting ? (accepting ? "yes" : "no") : null);
        Row("Queued jobs", p["queued-job-count"]?.AsInt()?.ToString(CultureInfo.InvariantCulture));
        Row("IPP versions", L("ipp-versions-supported"));
        Row("URIs", L("printer-uri-supported"));
        Row("Security", L("uri-security-supported"));
        Row("Auth", L("uri-authentication-supported"));
        Row("Formats", L("document-format-supported"));
        Row("Color", p["color-supported"]?.AsBool() is { } color ? (color ? "yes" : "no") : null);
        Row("Color modes", L("print-color-mode-supported"));
        Row("Sides", L("sides-supported"));
        Row("Media default", S("media-default"));
        Row("Media ready", L("media-ready", ""));
        Row("Resolutions", L("printer-resolution-supported", ""));
        Row("Copies", L("copies-supported", ""));
        Row("Firmware", L("printer-firmware-string-version", ""));

        var supplies = Supplies(p);
        if (supplies.Count > 0)
        {
            sb.AppendLine("Supplies");
            foreach (var line in supplies) sb.AppendLine("  " + line);
        }

        if (p["operations-supported"] is { } ops)
        {
            sb.AppendLine("Operations");
            sb.AppendLine("  " + string.Join(", ", ops.AsInts().Select(IppEnums.Operation)));
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Marker levels (toner, ink) with a simple bar. -1/-2/-3 mean unknown per RFC 3805.</summary>
    internal static List<string> Supplies(IppAttributeGroup p)
    {
        var names = p["marker-names"]?.AsStrings().ToList() ?? [];
        var levels = p["marker-levels"]?.AsInts().ToList() ?? [];
        var lines = new List<string>();

        for (var i = 0; i < names.Count; i++)
        {
            var level = i < levels.Count ? levels[i] : -1;
            if (level < 0)
            {
                lines.Add($"{names[i],-30} unknown");
                continue;
            }

            var filled = Math.Clamp(level / 10, 0, 10);
            lines.Add($"{names[i],-30} [{new string('#', filled)}{new string('.', 10 - filled)}] {level}%");
        }
        return lines;
    }

    public static string AllAttributes(IppMessage message)
    {
        var sb = new StringBuilder();
        foreach (var group in message.Groups)
        {
            sb.AppendLine($"[{group.Tag}]");
            foreach (var attribute in group.Attributes.OrderBy(a => a.Name, StringComparer.Ordinal))
            {
                sb.AppendLine($"  {attribute.Name} ({TagName(attribute)}) = {attribute.FormatValues()}");
            }
        }
        return sb.ToString().TrimEnd();
    }

    public static string Job(IppAttributeGroup job)
    {
        var id = job["job-id"]?.AsInt();
        var state = job["job-state"]?.AsInt() is { } s ? IppEnums.JobState(s) : "?";
        var reasons = job["job-state-reasons"] is { } r ? string.Join(", ", r.AsStrings()) : "";
        var name = job["job-name"]?.AsString() ?? "";
        var user = job["job-originating-user-name"]?.AsString() ?? "";
        var pages = job["job-impressions-completed"]?.AsInt();

        var text = $"#{id,-6} {state,-18} {Truncate(name, 30),-30} {user}";
        if (pages is not null) text += $"  {pages} impression(s)";
        if (reasons.Length > 0 && reasons != "none") text += $"  [{reasons}]";
        return text;
    }

    public static string Status(IppMessage response)
    {
        var text = $"{IppStatus.Name(response.StatusCode)} (0x{response.StatusCode:X4})";
        var message = response.Find(IppTag.OperationAttributes, "status-message")?.AsString();
        if (!string.IsNullOrEmpty(message)) text += $": {message}";

        var unsupported = response.GroupsOf(IppTag.UnsupportedAttributes).SelectMany(g => g.Attributes).ToList();
        if (unsupported.Count > 0)
        {
            text += Environment.NewLine + "Unsupported: " + string.Join(", ", unsupported.Select(a => $"{a.Name}={a.FormatValues()}"));
        }
        return text;
    }

    public static string Debug(string direction, IppMessage message, TimeSpan? elapsed)
    {
        var header = direction == "request"
            ? $">>> {IppEnums.Operation(message.Code)} (IPP/{message.Version}, request-id {message.RequestId})"
            : $"<<< {IppStatus.Name(message.Code)} (IPP/{message.Version}, request-id {message.RequestId}, {elapsed?.TotalMilliseconds:F0} ms)";
        return header + Environment.NewLine + AllAttributes(message);
    }

    private static string TagName(IppAttribute attribute)
    {
        var tags = attribute.ValueTags.Distinct().Select(t => t switch
        {
            IppTag.Keyword => "keyword",
            IppTag.Integer => "integer",
            IppTag.Enum => "enum",
            IppTag.Boolean => "boolean",
            IppTag.Uri => "uri",
            IppTag.MimeMediaType => "mimeMediaType",
            IppTag.Charset => "charset",
            IppTag.NaturalLanguage => "naturalLanguage",
            IppTag.OctetString => "octetString",
            IppTag.TextWithLanguage => "textWithLanguage",
            IppTag.NameWithLanguage => "nameWithLanguage",
            IppTag.UriScheme => "uriScheme",
            IppTag.TextWithoutLanguage => "text",
            IppTag.NameWithoutLanguage => "name",
            IppTag.RangeOfInteger => "rangeOfInteger",
            IppTag.Resolution => "resolution",
            IppTag.DateTime => "dateTime",
            IppTag.BegCollection => "collection",
            _ => t.ToString()
        });
        var name = string.Join("|", tags);
        return attribute.Values.Count > 1 ? "1setOf " + name : name;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
