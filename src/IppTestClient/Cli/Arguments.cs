namespace IppTestClient.Cli;

/// <summary>Minimal parser: positional arguments plus --name value / --flag options.</summary>
public sealed class Arguments
{
    private static readonly HashSet<string> Flags = ["--all", "--insecure", "--debug", "--wait", "--completed", "--help", "-h"];

    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Positional { get; } = [];

    public static Arguments Parse(IReadOnlyList<string> args)
    {
        var result = new Arguments();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (Flags.Contains(arg))
            {
                result._flags.Add(arg);
            }
            else if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var eq = arg.IndexOf('=');
                if (eq > 0)
                {
                    result._options[arg[..eq]] = arg[(eq + 1)..];
                }
                else if (i + 1 < args.Count)
                {
                    result._options[arg] = args[++i];
                }
                else
                {
                    throw new ArgumentException($"{arg} needs a value");
                }
            }
            else
            {
                result.Positional.Add(arg);
            }
        }
        return result;
    }

    public bool Has(string flag) => _flags.Contains(flag);

    public string? Get(string option) => _options.TryGetValue(option, out var v) ? v : null;

    public int? GetInt(string option) =>
        Get(option) is { } v ? (int.TryParse(v, out var n) ? n : throw new ArgumentException($"{option} must be a number")) : null;
}
