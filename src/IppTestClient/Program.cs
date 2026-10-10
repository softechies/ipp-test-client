using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using IppTestClient.Cli;
using IppTestClient.Ipp;

Arguments parsed;
try
{
    parsed = Arguments.Parse(args);
    _ = parsed.GetInt("--timeout"); // validate numbers up front
    _ = parsed.GetInt("--copies");
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

var handler = new SocketsHttpHandler
{
    ConnectTimeout = TimeSpan.FromSeconds(10)
};

if (parsed.Has("--insecure"))
{
    // Printers almost always ship with self signed certificates.
    handler.SslOptions = new SslClientAuthenticationOptions
    {
        RemoteCertificateValidationCallback = (_, _, _, _) => true
    };
}

using var http = new HttpClient(handler)
{
    Timeout = TimeSpan.FromSeconds(parsed.GetInt("--timeout") ?? 30)
};

var client = new IppClient(http);
if (parsed.Get("--user") is { } user) client.UserName = user;
if (parsed.Get("--ipp-version") is { } version)
{
    (byte, byte)? parsedVersion = version switch
    {
        "1.1" => (1, 1),
        "2.0" => (2, 0),
        "2.1" => (2, 1),
        "2.2" => (2, 2),
        _ => null
    };
    if (parsedVersion is null)
    {
        Console.Error.WriteLine("--ipp-version must be 1.1, 2.0, 2.1 or 2.2");
        return 2;
    }
    client.Version = parsedVersion.Value;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    return await new Commands(client, Console.Out).RunAsync(parsed, cts.Token);
}
catch (IppHttpException ex)
{
    Console.Error.WriteLine(ex.Message);
}
catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
{
    Console.Error.WriteLine($"TLS error: {ex.InnerException.Message}. If the printer uses a self signed certificate, add --insecure.");
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Could not reach the printer: {ex.Message}");
}
catch (TaskCanceledException) when (!cts.IsCancellationRequested)
{
    Console.Error.WriteLine("The printer did not answer in time. Try --timeout 60.");
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
}
catch (IppDecodeException ex)
{
    Console.Error.WriteLine($"Could not parse the printer's response: {ex.Message}. Is this really an IPP endpoint?");
}
catch (UriFormatException ex)
{
    Console.Error.WriteLine($"Invalid printer URI: {ex.Message}");
    return 2;
}
return 1;
