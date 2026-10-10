using IppTestClient.Cli;
using IppTestClient.Ipp;
using Xunit;

namespace IppTestClient.Tests;

public class CommandTests
{
    private static async Task<(int Code, string Output)> Run(FakePrinter printer, params string[] args)
    {
        using var http = new HttpClient();
        var client = new IppClient(http) { UserName = "tester" };
        var output = new StringWriter();
        var code = await new Commands(client, output).RunAsync(Arguments.Parse(args));
        return (code, output.ToString());
    }

    [Fact]
    public async Task Info_shows_printer_summary()
    {
        using var printer = new FakePrinter();

        var (code, output) = await Run(printer, "info", printer.Uri.ToString());

        Assert.Equal(0, code);
        Assert.Contains("Acme LaserJet 4000", output);
        Assert.Contains("idle (toner-low-warning)", output);
        Assert.Contains("application/pdf, image/urf", output);
        Assert.Contains("600dpi", output);
        Assert.Contains("Black Toner", output);
        Assert.Contains("15%", output);
        Assert.Contains("Print-Job, Validate-Job, Get-Printer-Attributes", output);

        var request = printer.Requests.Single();
        Assert.Equal((short)IppOperation.GetPrinterAttributes, request.Code);
        Assert.Equal("tester", request.Find(IppTag.OperationAttributes, "requesting-user-name")!.AsString());
        Assert.Equal(printer.Uri.ToString(), request.Find(IppTag.OperationAttributes, "printer-uri")!.AsString());
        Assert.Equal("application/ipp", printer.LastContentType);
    }

    [Fact]
    public async Task Info_all_dumps_attributes_with_types()
    {
        using var printer = new FakePrinter();

        var (_, output) = await Run(printer, "info", printer.Uri.ToString(), "--all");

        Assert.Contains("copies-supported (rangeOfInteger) = 1-99", output);
        Assert.Contains("printer-state (enum) = idle", output);
        Assert.Contains("media-col-database (collection) = {media-key=iso_a4_210x297mm}", output);
    }

    [Fact]
    public async Task Print_sends_document_and_job_attributes_then_waits()
    {
        using var printer = new FakePrinter();
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
        await File.WriteAllBytesAsync(file, "%PDF-1.4 test"u8.ToArray());

        try
        {
            var (code, output) = await Run(printer, "print", printer.Uri.ToString(), file,
                "--copies", "2", "--sides", "two-sided-long-edge", "--wait");

            Assert.Equal(0, code);
            Assert.Equal("%PDF-1.4 test"u8.ToArray(), printer.LastDocument);
            Assert.Contains("completed", output);

            var print = printer.Requests.First(r => r.Code == (short)IppOperation.PrintJob);
            Assert.Equal("application/pdf", print.Find(IppTag.OperationAttributes, "document-format")!.AsString());
            Assert.Equal(2, print.Find(IppTag.JobAttributes, "copies")!.AsInt());
            Assert.Equal("two-sided-long-edge", print.Find(IppTag.JobAttributes, "sides")!.AsString());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Print_wait_fails_when_job_is_aborted()
    {
        using var printer = new FakePrinter { JobStateToReport = 8 };
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        await File.WriteAllTextAsync(file, "hello");

        try
        {
            var (code, output) = await Run(printer, "print", printer.Uri.ToString(), file, "--wait");
            Assert.Equal(1, code);
            Assert.Contains("aborted", output);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Jobs_lists_each_job_group()
    {
        using var printer = new FakePrinter();

        var (code, output) = await Run(printer, "jobs", printer.Uri.ToString());

        Assert.Equal(0, code);
        Assert.Contains("#11", output);
        Assert.Contains("processing", output);
        Assert.Contains("#12", output);
        Assert.Contains("photo.jpg", output);
    }

    [Fact]
    public async Task Validate_reports_printer_error_status()
    {
        using var printer = new FakePrinter();

        var (code, output) = await Run(printer, "validate", printer.Uri.ToString(), "--format", "application/x-unknown");

        Assert.Equal(1, code);
        Assert.Contains("client-error-document-format-not-supported", output);
        Assert.Contains("Format not supported", output);
    }

    [Fact]
    public async Task Unsupported_operation_returns_error()
    {
        using var printer = new FakePrinter();

        var (code, output) = await Run(printer, "identify", printer.Uri.ToString());

        Assert.Equal(1, code);
        Assert.Contains("server-error-operation-not-supported", output);
    }

    [Fact]
    public async Task Help_and_bad_usage()
    {
        using var printer = new FakePrinter();
        Assert.Contains("Usage", (await Run(printer, "--help")).Output);
        Assert.Equal(2, (await Run(printer, "job", printer.Uri.ToString())).Code);
        Assert.Equal(2, (await Run(printer, "frobnicate", printer.Uri.ToString())).Code);
    }

    [Fact]
    public void Document_format_guessing()
    {
        Assert.Equal("application/pdf", DocumentFormats.FromFileName("a.PDF"));
        Assert.Equal("image/pwg-raster", DocumentFormats.FromFileName("page.pwg"));
        Assert.Equal("application/octet-stream", DocumentFormats.FromFileName("data.bin"));
    }
}
