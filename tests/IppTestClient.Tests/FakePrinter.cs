using System.Net;
using System.Net.Sockets;
using IppTestClient.Ipp;

namespace IppTestClient.Tests;

/// <summary>A tiny in process IPP printer for integration tests.</summary>
internal sealed class FakePrinter : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Task _loop;

    public FakePrinter()
    {
        Port = FreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _loop = Task.Run(LoopAsync);
    }

    public int Port { get; }
    public Uri Uri => new($"ipp://127.0.0.1:{Port}/ipp/print");
    public List<IppMessage> Requests { get; } = [];
    public byte[]? LastDocument { get; private set; }
    public string? LastContentType { get; private set; }
    public int JobStateToReport { get; set; } = 9;

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch { return; }

            using var body = new MemoryStream();
            await context.Request.InputStream.CopyToAsync(body);
            var bytes = body.ToArray();
            LastContentType = context.Request.ContentType;

            var request = IppDecoder.Decode(bytes, out var offset);
            lock (Requests) Requests.Add(request);

            var response = Handle(request, bytes[offset..]);
            var encoded = IppEncoder.Encode(response);

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/ipp";
            await context.Response.OutputStream.WriteAsync(encoded);
            context.Response.Close();
        }
    }

    private IppMessage Handle(IppMessage request, byte[] document)
    {
        var response = new IppMessage { Code = IppStatus.SuccessfulOk, RequestId = request.RequestId };
        response.Group(IppTag.OperationAttributes)
            .Add("attributes-charset", IppTag.Charset, "utf-8")
            .Add("attributes-natural-language", IppTag.NaturalLanguage, "en");

        switch ((IppOperation)request.Code)
        {
            case IppOperation.GetPrinterAttributes:
                response.Group(IppTag.PrinterAttributes)
                    .Add("printer-name", IppTag.NameWithoutLanguage, "Office-Laser")
                    .Add("printer-make-and-model", IppTag.TextWithoutLanguage, "Acme LaserJet 4000")
                    .Add("printer-location", IppTag.TextWithoutLanguage, "2nd floor")
                    .Add("printer-state", IppTag.Enum, 3)
                    .Add("printer-state-reasons", IppTag.Keyword, "toner-low-warning")
                    .Add("printer-is-accepting-jobs", IppTag.Boolean, true)
                    .Add("ipp-versions-supported", IppTag.Keyword, "1.1", "2.0")
                    .Add("document-format-supported", IppTag.MimeMediaType, "application/pdf", "image/urf")
                    .Add("sides-supported", IppTag.Keyword, "one-sided", "two-sided-long-edge")
                    .Add("color-supported", IppTag.Boolean, false)
                    .Add("printer-resolution-supported", IppTag.Resolution, new IppResolution(600, 600, 3))
                    .Add("copies-supported", IppTag.RangeOfInteger, new IppRange(1, 99))
                    .Add("marker-names", IppTag.NameWithoutLanguage, "Black Toner")
                    .Add("marker-levels", IppTag.Integer, 15)
                    .Add("operations-supported", IppTag.Enum, 0x0002, 0x0004, 0x000B)
                    .Add("media-col-database", IppTag.BegCollection,
                        new IppCollection { new IppAttribute("media-key", IppTag.Keyword, "iso_a4_210x297mm") });
                break;

            case IppOperation.PrintJob:
                LastDocument = document;
                response.Group(IppTag.JobAttributes)
                    .Add("job-id", IppTag.Integer, 7)
                    .Add("job-state", IppTag.Enum, 3)
                    .Add("job-name", IppTag.NameWithoutLanguage, "test.pdf");
                break;

            case IppOperation.GetJobAttributes:
                response.Group(IppTag.JobAttributes)
                    .Add("job-id", IppTag.Integer, 7)
                    .Add("job-state", IppTag.Enum, JobStateToReport)
                    .Add("job-name", IppTag.NameWithoutLanguage, "test.pdf");
                break;

            case IppOperation.GetJobs:
                response.Groups.Add(new IppAttributeGroup(IppTag.JobAttributes)
                    .Add("job-id", IppTag.Integer, 11).Add("job-state", IppTag.Enum, 5).Add("job-name", IppTag.NameWithoutLanguage, "report.pdf"));
                response.Groups.Add(new IppAttributeGroup(IppTag.JobAttributes)
                    .Add("job-id", IppTag.Integer, 12).Add("job-state", IppTag.Enum, 3).Add("job-name", IppTag.NameWithoutLanguage, "photo.jpg"));
                break;

            case IppOperation.ValidateJob:
                if (request.Find(IppTag.OperationAttributes, "document-format")?.AsString() == "application/x-unknown")
                {
                    response.Code = 0x040A;
                    response.Group(IppTag.OperationAttributes).Add("status-message", IppTag.TextWithoutLanguage, "Format not supported");
                }
                break;

            default:
                response.Code = 0x0501;
                break;
        }

        return response;
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { /* stopping */ }
    }
}
