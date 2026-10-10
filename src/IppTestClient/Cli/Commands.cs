using IppTestClient.Ipp;

namespace IppTestClient.Cli;

public sealed class Commands(IppClient client, TextWriter output)
{
    public const string Usage = """
        IPP test client: query printers and send test jobs over IPP / IPP Everywhere.

        Usage: ipp-test <command> <printer-uri> [arguments] [options]

        Commands
          info      <uri> [--all]                 printer status, capabilities and supplies (--all dumps every attribute)
          print     <uri> <file> [job options]    send a file with Print-Job
          validate  <uri> [job options]           ask the printer if a job would be accepted (Validate-Job)
          job       <uri> <job-id>                job status (Get-Job-Attributes)
          jobs      <uri> [--completed]           list active (or completed) jobs (Get-Jobs)
          cancel    <uri> <job-id>                cancel a job (Cancel-Job)
          identify  <uri>                         make the printer beep or flash (Identify-Printer)

        Job options
          --format <mime>        document format, default guessed from the file extension
          --copies <n>           --sides one-sided | two-sided-long-edge | two-sided-short-edge
          --media <keyword>      e.g. iso_a4_210x297mm, na_letter_8.5x11in
          --color <mode>         color | monochrome | auto
          --job-name <name>      --wait (poll until the job finishes)

        Global options
          --user <name>          requesting-user-name, default is the current user
          --ipp-version <v>      1.1 or 2.0 (default 2.0)
          --insecure             accept self signed certificates on ipps:// (common on printers)
          --timeout <seconds>    default 30
          --debug                print every request and response attribute

        URIs
          ipp://192.168.1.50/ipp/print           most network printers (IPP Everywhere / AirPrint)
          ipps://192.168.1.50/ipp/print          same over TLS
          ipp://localhost:631/printers/MyQueue   CUPS queue
          http://server:631/printers/MyQueue     Windows / CUPS print server
        """;

    public async Task<int> RunAsync(Arguments args, CancellationToken ct = default)
    {
        if (args.Has("--help") || args.Has("-h") || args.Positional.Count == 0)
        {
            await output.WriteLineAsync(Usage);
            return 0;
        }

        var command = args.Positional[0].ToLowerInvariant();
        if (args.Positional.Count < 2)
        {
            await output.WriteLineAsync($"'{command}' needs a printer URI. Run with --help for usage.");
            return 2;
        }

        var uri = new Uri(args.Positional[1]);
        if (args.Has("--debug"))
        {
            client.Trace = (direction, message, elapsed) => output.WriteLine(Output.Debug(direction, message, elapsed) + Environment.NewLine);
        }

        return command switch
        {
            "info" => await InfoAsync(uri, args, ct),
            "print" => await PrintAsync(uri, args, ct),
            "validate" => await ValidateAsync(uri, args, ct),
            "job" => await JobAsync(uri, args, ct),
            "jobs" => await JobsAsync(uri, args, ct),
            "cancel" => await CancelAsync(uri, args, ct),
            "identify" => await IdentifyAsync(uri, ct),
            _ => await UnknownAsync(command)
        };
    }

    private async Task<int> InfoAsync(Uri uri, Arguments args, CancellationToken ct)
    {
        var response = await client.GetPrinterAttributesAsync(uri, ct: ct);
        if (!await CheckAsync(response)) return 1;

        await output.WriteLineAsync(args.Has("--all") ? Output.AllAttributes(response) : Output.PrinterSummary(response));
        return 0;
    }

    private async Task<int> PrintAsync(Uri uri, Arguments args, CancellationToken ct)
    {
        if (args.Positional.Count < 3)
        {
            await output.WriteLineAsync("Usage: ipp-test print <uri> <file> [options]");
            return 2;
        }

        var path = args.Positional[2];
        if (!File.Exists(path))
        {
            await output.WriteLineAsync($"File not found: {path}");
            return 2;
        }

        var options = JobOptions(args, path);
        await output.WriteLineAsync($"Sending {Path.GetFileName(path)} ({new FileInfo(path).Length:N0} bytes, {options.DocumentFormat})...");

        await using var file = File.OpenRead(path);
        var response = await client.PrintJobAsync(uri, file, options, ct);
        if (!await CheckAsync(response)) return 1;

        var job = response.GroupsOf(IppTag.JobAttributes).FirstOrDefault();
        var jobId = job?["job-id"]?.AsInt();
        await output.WriteLineAsync($"Accepted: {Output.Status(response)}");
        if (job is not null) await output.WriteLineAsync(Output.Job(job));

        if (args.Has("--wait") && jobId is not null)
        {
            return await WaitForJobAsync(uri, jobId.Value, ct);
        }
        return 0;
    }

    private async Task<int> WaitForJobAsync(Uri uri, int jobId, CancellationToken ct)
    {
        string? lastLine = null;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            var response = await client.GetJobAttributesAsync(uri, jobId, ct);
            if (!await CheckAsync(response)) return 1;

            var job = response.GroupsOf(IppTag.JobAttributes).First();
            var line = Output.Job(job);
            if (line != lastLine)
            {
                await output.WriteLineAsync(line);
                lastLine = line;
            }

            var state = job["job-state"]?.AsInt() ?? 0;
            if (IppEnums.IsTerminalJobState(state))
            {
                return state == 9 ? 0 : 1; // completed is success, canceled/aborted is not
            }
        }
        return 1;
    }

    private async Task<int> ValidateAsync(Uri uri, Arguments args, CancellationToken ct)
    {
        var response = await client.ValidateJobAsync(uri, JobOptions(args, null), ct);
        await output.WriteLineAsync(Output.Status(response));
        return response.IsSuccess ? 0 : 1;
    }

    private async Task<int> JobAsync(Uri uri, Arguments args, CancellationToken ct)
    {
        if (!TryJobId(args, out var jobId)) return await UsageAsync("job <uri> <job-id>");

        var response = await client.GetJobAttributesAsync(uri, jobId, ct);
        if (!await CheckAsync(response)) return 1;

        var job = response.GroupsOf(IppTag.JobAttributes).First();
        await output.WriteLineAsync(Output.Job(job));
        if (args.Has("--all")) await output.WriteLineAsync(Output.AllAttributes(response));
        return 0;
    }

    private async Task<int> JobsAsync(Uri uri, Arguments args, CancellationToken ct)
    {
        var response = await client.GetJobsAsync(uri, args.Has("--completed"), ct: ct);
        if (!await CheckAsync(response)) return 1;

        var jobs = response.GroupsOf(IppTag.JobAttributes).ToList();
        if (jobs.Count == 0)
        {
            await output.WriteLineAsync(args.Has("--completed") ? "No completed jobs." : "No active jobs.");
            return 0;
        }

        foreach (var job in jobs)
        {
            await output.WriteLineAsync(Output.Job(job));
        }
        return 0;
    }

    private async Task<int> CancelAsync(Uri uri, Arguments args, CancellationToken ct)
    {
        if (!TryJobId(args, out var jobId)) return await UsageAsync("cancel <uri> <job-id>");

        var response = await client.CancelJobAsync(uri, jobId, ct);
        await output.WriteLineAsync(Output.Status(response));
        return response.IsSuccess ? 0 : 1;
    }

    private async Task<int> IdentifyAsync(Uri uri, CancellationToken ct)
    {
        var response = await client.IdentifyPrinterAsync(uri, ct);
        await output.WriteLineAsync(Output.Status(response));
        return response.IsSuccess ? 0 : 1;
    }

    private async Task<int> UnknownAsync(string command)
    {
        await output.WriteLineAsync($"Unknown command '{command}'. Run with --help for usage.");
        return 2;
    }

    private async Task<int> UsageAsync(string usage)
    {
        await output.WriteLineAsync("Usage: ipp-test " + usage);
        return 2;
    }

    private async Task<bool> CheckAsync(IppMessage response)
    {
        if (response.IsSuccess) return true;
        await output.WriteLineAsync("Printer returned " + Output.Status(response));
        return false;
    }

    private static bool TryJobId(Arguments args, out int jobId)
    {
        jobId = 0;
        return args.Positional.Count >= 3 && int.TryParse(args.Positional[2], out jobId);
    }

    internal static PrintJobOptions JobOptions(Arguments args, string? path) => new()
    {
        JobName = args.Get("--job-name") ?? (path is null ? "ipp-test" : Path.GetFileName(path)),
        DocumentFormat = args.Get("--format") ?? (path is null ? "application/pdf" : DocumentFormats.FromFileName(path)),
        Copies = args.GetInt("--copies"),
        Sides = args.Get("--sides"),
        Media = args.Get("--media"),
        PrintColorMode = args.Get("--color")
    };
}
