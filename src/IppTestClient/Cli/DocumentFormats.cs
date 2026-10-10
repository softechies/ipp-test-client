namespace IppTestClient.Cli;

public static class DocumentFormats
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".pwg"] = "image/pwg-raster",
        [".urf"] = "image/urf",
        [".txt"] = "text/plain",
        [".ps"] = "application/postscript",
        [".pcl"] = "application/vnd.hp-PCL",
        [".prn"] = "application/vnd.hp-PCL",
    };

    /// <summary>Guess from the file extension; octet-stream lets the printer auto detect.</summary>
    public static string FromFileName(string path) =>
        ByExtension.TryGetValue(Path.GetExtension(path), out var mime) ? mime : "application/octet-stream";
}
