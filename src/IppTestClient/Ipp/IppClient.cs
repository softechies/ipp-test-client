using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace IppTestClient.Ipp;

public sealed class IppHttpException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

public sealed class PrintJobOptions
{
    public string JobName { get; set; } = "ipp-test";
    public string DocumentFormat { get; set; } = "application/octet-stream";
    public int? Copies { get; set; }
    public string? Sides { get; set; }
    public string? Media { get; set; }
    public string? PrintColorMode { get; set; }
    public int? PrintQuality { get; set; }
    public int? OrientationRequested { get; set; }
}

/// <summary>
/// Sends IPP requests over HTTP(S). ipp:// maps to http:// and ipps:// to https://,
/// both on port 631 unless the URI says otherwise.
/// </summary>
public sealed class IppClient(HttpClient http)
{
    private int _requestId;

    public string UserName { get; set; } = Environment.UserName;

    /// <summary>Request version. Use 1.1 for very old printers.</summary>
    public (byte Major, byte Minor) Version { get; set; } = (2, 0);

    /// <summary>Called with each request and response, for --debug output.</summary>
    public Action<string, IppMessage, TimeSpan?>? Trace { get; set; }

    public static Uri ToHttpUri(Uri printerUri)
    {
        var scheme = printerUri.Scheme.ToLowerInvariant();
        if (scheme is "http" or "https")
        {
            return printerUri;
        }

        var builder = new UriBuilder(printerUri)
        {
            Scheme = scheme switch
            {
                "ipp" => "http",
                "ipps" => "https",
                _ => throw new ArgumentException($"Unsupported scheme '{printerUri.Scheme}'. Use ipp://, ipps://, http:// or https://")
            }
        };
        if (printerUri.Port <= 0)
        {
            builder.Port = 631; // IPP default port, for both ipp and ipps
        }
        return builder.Uri;
    }

    /// <summary>The printer-uri attribute should use the ipp/ipps scheme form.</summary>
    public static string ToIppUri(Uri uri)
    {
        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme is "ipp" or "ipps")
        {
            return uri.OriginalString;
        }

        var builder = new UriBuilder(uri) { Scheme = scheme == "https" ? "ipps" : "ipp", Port = uri.Port };
        return builder.Uri.ToString();
    }

    public IppMessage NewRequest(IppOperation operation, Uri printerUri)
    {
        var request = IppMessage.CreateRequest(operation, Interlocked.Increment(ref _requestId), ToIppUri(printerUri));
        request.VersionMajor = Version.Major;
        request.VersionMinor = Version.Minor;
        request.Group(IppTag.OperationAttributes).Add("requesting-user-name", IppTag.NameWithoutLanguage, UserName);
        return request;
    }

    public async Task<IppMessage> SendAsync(Uri printerUri, IppMessage request, Stream? document = null, CancellationToken ct = default)
    {
        var header = IppEncoder.Encode(request);
        Trace?.Invoke("request", request, null);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ToHttpUri(printerUri))
        {
            Content = new IppContent(header, document)
        };

        var stopwatch = Stopwatch.StartNew();
        using var response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            var hint = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => " The printer wants authentication.",
                HttpStatusCode.UpgradeRequired => " The printer requires TLS, try ipps://.",
                HttpStatusCode.NotFound => " Check the resource path, e.g. /ipp/print or /printers/<name>.",
                _ => ""
            };
            throw new IppHttpException(response.StatusCode, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}.{hint}");
        }

        var body = await response.Content.ReadAsByteArrayAsync(ct);
        var message = IppDecoder.Decode(body);
        Trace?.Invoke("response", message, stopwatch.Elapsed);
        return message;
    }

    public Task<IppMessage> GetPrinterAttributesAsync(Uri printerUri, IEnumerable<string>? requested = null, CancellationToken ct = default)
    {
        var request = NewRequest(IppOperation.GetPrinterAttributes, printerUri);
        var names = (requested ?? ["all", "media-col-database"]).Cast<object>().ToArray();
        request.Group(IppTag.OperationAttributes).Add("requested-attributes", IppTag.Keyword, names);
        return SendAsync(printerUri, request, null, ct);
    }

    public Task<IppMessage> PrintJobAsync(Uri printerUri, Stream document, PrintJobOptions options, CancellationToken ct = default)
    {
        var request = NewRequest(IppOperation.PrintJob, printerUri);
        AddJobAttributes(request, options);
        return SendAsync(printerUri, request, document, ct);
    }

    public Task<IppMessage> ValidateJobAsync(Uri printerUri, PrintJobOptions options, CancellationToken ct = default)
    {
        var request = NewRequest(IppOperation.ValidateJob, printerUri);
        AddJobAttributes(request, options);
        return SendAsync(printerUri, request, null, ct);
    }

    public Task<IppMessage> GetJobAttributesAsync(Uri printerUri, int jobId, CancellationToken ct = default)
    {
        var request = NewRequest(IppOperation.GetJobAttributes, printerUri);
        var op = request.Group(IppTag.OperationAttributes);
        op.Add("job-id", IppTag.Integer, jobId);
        op.Add("requested-attributes", IppTag.Keyword, "all");
        return SendAsync(printerUri, request, null, ct);
    }

    public Task<IppMessage> GetJobsAsync(Uri printerUri, bool completed, int limit = 20, CancellationToken ct = default)
    {
        var request = NewRequest(IppOperation.GetJobs, printerUri);
        var op = request.Group(IppTag.OperationAttributes);
        op.Add("which-jobs", IppTag.Keyword, completed ? "completed" : "not-completed");
        op.Add("limit", IppTag.Integer, limit);
        op.Add("requested-attributes", IppTag.Keyword,
            "job-id", "job-name", "job-state", "job-state-reasons", "job-originating-user-name", "time-at-creation", "job-impressions-completed");
        return SendAsync(printerUri, request, null, ct);
    }

    public Task<IppMessage> CancelJobAsync(Uri printerUri, int jobId, CancellationToken ct = default)
    {
        var request = NewRequest(IppOperation.CancelJob, printerUri);
        request.Group(IppTag.OperationAttributes).Add("job-id", IppTag.Integer, jobId);
        return SendAsync(printerUri, request, null, ct);
    }

    public Task<IppMessage> IdentifyPrinterAsync(Uri printerUri, CancellationToken ct = default)
    {
        var request = NewRequest(IppOperation.IdentifyPrinter, printerUri);
        request.Group(IppTag.OperationAttributes).Add("identify-actions", IppTag.Keyword, "flash", "sound");
        return SendAsync(printerUri, request, null, ct);
    }

    private static void AddJobAttributes(IppMessage request, PrintJobOptions options)
    {
        var op = request.Group(IppTag.OperationAttributes);
        op.Add("job-name", IppTag.NameWithoutLanguage, options.JobName);
        op.Add("document-format", IppTag.MimeMediaType, options.DocumentFormat);

        var job = new IppAttributeGroup(IppTag.JobAttributes);
        if (options.Copies is { } copies) job.Add("copies", IppTag.Integer, copies);
        if (options.Sides is { } sides) job.Add("sides", IppTag.Keyword, sides);
        if (options.Media is { } media) job.Add("media", IppTag.Keyword, media);
        if (options.PrintColorMode is { } color) job.Add("print-color-mode", IppTag.Keyword, color);
        if (options.PrintQuality is { } quality) job.Add("print-quality", IppTag.Enum, quality);
        if (options.OrientationRequested is { } orientation) job.Add("orientation-requested", IppTag.Enum, orientation);

        if (job.Attributes.Count > 0)
        {
            request.Groups.Add(job);
        }
    }

    /// <summary>
    /// IPP attributes followed by the document. Sends Content-Length when the document size is known,
    /// because some printer firmware does not accept chunked uploads.
    /// </summary>
    private sealed class IppContent : HttpContent
    {
        private readonly byte[] _header;
        private readonly Stream? _document;

        public IppContent(byte[] header, Stream? document)
        {
            _header = header;
            _document = document;
            Headers.ContentType = new MediaTypeHeaderValue("application/ipp");
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(_header);
            if (_document is not null)
            {
                await _document.CopyToAsync(stream);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            if (_document is null)
            {
                length = _header.Length;
                return true;
            }
            if (_document.CanSeek)
            {
                length = _header.Length + (_document.Length - _document.Position);
                return true;
            }
            length = 0;
            return false;
        }
    }
}
