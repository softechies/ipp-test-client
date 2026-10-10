# IPP Test Client

A command line tool for talking to printers over **IPP** (Internet Printing Protocol):
check status and supplies, list capabilities, send a test print, and watch the job finish.
Works with network printers (IPP Everywhere, AirPrint), CUPS queues and Windows print servers.

The IPP encoder and decoder are written from scratch against RFC 8010 / RFC 8011
in plain .NET 8, with no dependencies. Collections (`media-col` and friends) are fully supported.

[![build](https://github.com/softechies/ipp-test-client/actions/workflows/build.yml/badge.svg)](https://github.com/softechies/ipp-test-client/actions/workflows/build.yml)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![License: MIT](https://img.shields.io/badge/license-MIT-green)

## Why

When a print integration fails, the first question is always "what does the printer actually say?".
Vendor tools hide the protocol, and `ipptool` needs test files. This gives you readable answers
in one command, and a `--debug` switch that shows every attribute on the wire.

## Examples

```text
$ ipp-test info ipp://192.168.1.50/ipp/print

Printer           HP LaserJet Pro M404dn
Name              HP_LaserJet_M404
Location          2nd floor, finance
State             idle (toner-low-warning)
Accepting jobs    yes
IPP versions      1.1, 2.0
Formats           application/pdf, image/urf, image/pwg-raster, application/octet-stream
Color             no
Sides             one-sided, two-sided-long-edge, two-sided-short-edge
Media default     iso_a4_210x297mm
Resolutions       600dpi, 1200dpi
Copies            1-999
Supplies
  Black Cartridge                [#.........] 15%
Operations
  Print-Job, Validate-Job, Create-Job, Send-Document, Cancel-Job, Get-Job-Attributes, Get-Jobs, Get-Printer-Attributes, Identify-Printer
```

```text
$ ipp-test print ipp://192.168.1.50/ipp/print invoice.pdf --sides two-sided-long-edge --copies 2 --wait

Sending invoice.pdf (48,213 bytes, application/pdf)...
Accepted: successful-ok (0x0000)
#212    pending            invoice.pdf
#212    processing         invoice.pdf                    usama
#212    completed          invoice.pdf                    usama  4 impression(s)
```

```text
$ ipp-test validate ipp://192.168.1.50/ipp/print --format application/vnd.ms-excel
client-error-document-format-not-supported (0x040A): Unsupported document-format
```

## Commands

| Command | IPP operation | What it does |
|---|---|---|
| `info <uri> [--all]` | Get-Printer-Attributes | Status, capabilities, supplies. `--all` dumps every attribute with its type |
| `print <uri> <file>` | Print-Job | Sends a file, `--wait` polls until it completes |
| `validate <uri>` | Validate-Job | Asks whether a job with these options would be accepted, without printing |
| `job <uri> <id>` | Get-Job-Attributes | One job's state |
| `jobs <uri> [--completed]` | Get-Jobs | Active or finished jobs |
| `cancel <uri> <id>` | Cancel-Job | Cancels a job |
| `identify <uri>` | Identify-Printer | Makes the printer beep or flash so you can find it |

**Job options:** `--format`, `--copies`, `--sides`, `--media`, `--color`, `--job-name`, `--wait`
**Global options:** `--user`, `--ipp-version 1.1|2.0`, `--insecure`, `--timeout`, `--debug`

## Printer URIs

| Target | URI |
|---|---|
| Most network printers | `ipp://<ip>/ipp/print` |
| Same, over TLS | `ipps://<ip>/ipp/print` (add `--insecure` for self signed certificates) |
| CUPS queue | `ipp://<host>:631/printers/<queue>` |
| Windows print server | `http://<server>/printers/<share>/.printer` |

`ipp://` is sent as HTTP and `ipps://` as HTTPS, both on port 631 unless the URI has another port.

## Install and run

```bash
git clone https://github.com/softechies/ipp-test-client.git
cd ipp-test-client
dotnet run --project src/IppTestClient -- info ipp://192.168.1.50/ipp/print
```

Or publish a single file executable:

```bash
dotnet publish src/IppTestClient -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o out
out\ipp-test.exe --help
```

CI builds ready to run binaries for Windows, Linux and macOS on every push (see the Actions tab).

## Using the library in your own code

The `Ipp` folder is a small standalone IPP library you can copy into another project:

```csharp
using var http = new HttpClient();
var ipp = new IppClient(http) { UserName = "billing-service" };

var printer = new Uri("ipp://192.168.1.50/ipp/print");
var attrs = await ipp.GetPrinterAttributesAsync(printer, ["printer-state", "marker-levels"]);
var state = attrs.Find(IppTag.PrinterAttributes, "printer-state")?.AsInt();

await using var pdf = File.OpenRead("label.pdf");
var result = await ipp.PrintJobAsync(printer, pdf, new PrintJobOptions
{
    DocumentFormat = "application/pdf",
    Media = "na_index-4x6_4x6in"
});
var jobId = result.Find(IppTag.JobAttributes, "job-id")?.AsInt();
```

## Tests

```bash
dotnet test
```

The suite checks byte exact request encoding, round trips of every value type including nested
collections, and runs every command against an in process fake printer. The tool has also been
checked against a real CUPS server (IPP/1.1 and 2.x, plain and TLS).

## Project layout

```
src/IppTestClient/
  Ipp/   protocol: tags, message model, encoder, decoder, HTTP client
  Cli/   argument parsing, commands, output formatting
tests/IppTestClient.Tests/
```

## License

MIT

---

Built and maintained by [Softechies Solutions](https://softechies.net). We work on printing and
Windows system software: IPP servers and clients, print drivers, Print Support Apps and Windows services.
