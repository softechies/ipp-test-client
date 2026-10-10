namespace IppTestClient.Ipp;

/// <summary>Delimiter and value tags from RFC 8010 section 3.5.</summary>
public enum IppTag : byte
{
    // Delimiters (group tags)
    OperationAttributes = 0x01,
    JobAttributes = 0x02,
    EndOfAttributes = 0x03,
    PrinterAttributes = 0x04,
    UnsupportedAttributes = 0x05,

    // Out of band values
    Unsupported = 0x10,
    Unknown = 0x12,
    NoValue = 0x13,

    // Integer types
    Integer = 0x21,
    Boolean = 0x22,
    Enum = 0x23,

    // Octet string types
    OctetString = 0x30,
    DateTime = 0x31,
    Resolution = 0x32,
    RangeOfInteger = 0x33,
    BegCollection = 0x34,
    TextWithLanguage = 0x35,
    NameWithLanguage = 0x36,
    EndCollection = 0x37,

    // Character string types
    TextWithoutLanguage = 0x41,
    NameWithoutLanguage = 0x42,
    Keyword = 0x44,
    Uri = 0x45,
    UriScheme = 0x46,
    Charset = 0x47,
    NaturalLanguage = 0x48,
    MimeMediaType = 0x49,
    MemberAttrName = 0x4A,

    Extension = 0x7F
}

public static class IppTagExtensions
{
    public static bool IsDelimiter(this IppTag tag) => (byte)tag <= 0x0F;

    public static bool IsOutOfBand(this IppTag tag) => tag is IppTag.Unsupported or IppTag.Unknown or IppTag.NoValue;

    public static bool IsString(this IppTag tag) => (byte)tag is >= 0x41 and <= 0x49;
}

public enum IppOperation : short
{
    PrintJob = 0x0002,
    PrintUri = 0x0003,
    ValidateJob = 0x0004,
    CreateJob = 0x0005,
    SendDocument = 0x0006,
    CancelJob = 0x0008,
    GetJobAttributes = 0x0009,
    GetJobs = 0x000A,
    GetPrinterAttributes = 0x000B,
    HoldJob = 0x000C,
    ReleaseJob = 0x000D,
    RestartJob = 0x000E,
    PausePrinter = 0x0010,
    ResumePrinter = 0x0011,
    PurgeJobs = 0x0012,
    CancelMyJobs = 0x0039,
    CloseJob = 0x003B,
    IdentifyPrinter = 0x003C
}

public static class IppStatus
{
    public const short SuccessfulOk = 0x0000;

    private static readonly Dictionary<short, string> Names = new()
    {
        [0x0000] = "successful-ok",
        [0x0001] = "successful-ok-ignored-or-substituted-attributes",
        [0x0002] = "successful-ok-conflicting-attributes",
        [0x0400] = "client-error-bad-request",
        [0x0401] = "client-error-forbidden",
        [0x0402] = "client-error-not-authenticated",
        [0x0403] = "client-error-not-authorized",
        [0x0404] = "client-error-not-possible",
        [0x0405] = "client-error-timeout",
        [0x0406] = "client-error-not-found",
        [0x0407] = "client-error-gone",
        [0x0408] = "client-error-request-entity-too-large",
        [0x0409] = "client-error-request-value-too-long",
        [0x040A] = "client-error-document-format-not-supported",
        [0x040B] = "client-error-attributes-or-values-not-supported",
        [0x040C] = "client-error-uri-scheme-not-supported",
        [0x040D] = "client-error-charset-not-supported",
        [0x040E] = "client-error-conflicting-attributes",
        [0x040F] = "client-error-compression-not-supported",
        [0x0410] = "client-error-compression-error",
        [0x0411] = "client-error-document-format-error",
        [0x0412] = "client-error-document-access-error",
        [0x0500] = "server-error-internal-error",
        [0x0501] = "server-error-operation-not-supported",
        [0x0502] = "server-error-service-unavailable",
        [0x0503] = "server-error-version-not-supported",
        [0x0504] = "server-error-device-error",
        [0x0505] = "server-error-temporary-error",
        [0x0506] = "server-error-not-accepting-jobs",
        [0x0507] = "server-error-busy",
        [0x0508] = "server-error-job-canceled",
        [0x0509] = "server-error-multiple-document-jobs-not-supported"
    };

    public static bool IsSuccess(short status) => status is >= 0x0000 and <= 0x00FF;

    public static string Name(short status) =>
        Names.TryGetValue(status, out var name) ? name : $"0x{status:X4}";
}

/// <summary>Human names for common enum attributes (RFC 8011).</summary>
public static class IppEnums
{
    public static string PrinterState(int value) => value switch
    {
        3 => "idle",
        4 => "processing",
        5 => "stopped",
        _ => value.ToString()
    };

    public static string JobState(int value) => value switch
    {
        3 => "pending",
        4 => "pending-held",
        5 => "processing",
        6 => "processing-stopped",
        7 => "canceled",
        8 => "aborted",
        9 => "completed",
        _ => value.ToString()
    };

    public static bool IsTerminalJobState(int value) => value is 7 or 8 or 9;

    private static readonly Dictionary<int, string> OperationNames = new()
    {
        [0x0002] = "Print-Job", [0x0003] = "Print-URI", [0x0004] = "Validate-Job", [0x0005] = "Create-Job",
        [0x0006] = "Send-Document", [0x0007] = "Send-URI", [0x0008] = "Cancel-Job", [0x0009] = "Get-Job-Attributes",
        [0x000A] = "Get-Jobs", [0x000B] = "Get-Printer-Attributes", [0x000C] = "Hold-Job", [0x000D] = "Release-Job",
        [0x000E] = "Restart-Job", [0x0010] = "Pause-Printer", [0x0011] = "Resume-Printer", [0x0012] = "Purge-Jobs",
        [0x0013] = "Set-Printer-Attributes", [0x0014] = "Set-Job-Attributes", [0x0015] = "Get-Printer-Supported-Values",
        [0x0016] = "Create-Printer-Subscriptions", [0x0017] = "Create-Job-Subscriptions", [0x0018] = "Get-Subscription-Attributes",
        [0x0019] = "Get-Subscriptions", [0x001A] = "Renew-Subscription", [0x001B] = "Cancel-Subscription",
        [0x001C] = "Get-Notifications", [0x0022] = "Enable-Printer", [0x0023] = "Disable-Printer",
        [0x0024] = "Pause-Printer-After-Current-Job", [0x0025] = "Hold-New-Jobs", [0x0026] = "Release-Held-New-Jobs",
        [0x0038] = "Cancel-Jobs", [0x0039] = "Cancel-My-Jobs", [0x003A] = "Resubmit-Job", [0x003B] = "Close-Job",
        [0x003C] = "Identify-Printer", [0x003D] = "Validate-Document", [0x003E] = "Add-Document-Images",
        [0x003F] = "Acknowledge-Document", [0x0040] = "Acknowledge-Identify-Printer", [0x0041] = "Acknowledge-Job",
        [0x0042] = "Fetch-Document", [0x0043] = "Fetch-Job", [0x0044] = "Get-Output-Device-Attributes",
        [0x0045] = "Update-Active-Jobs", [0x0046] = "Deregister-Output-Device", [0x0047] = "Update-Document-Status",
        [0x0048] = "Update-Job-Status", [0x0049] = "Update-Output-Device-Attributes",
        // CUPS extensions
        [0x4001] = "CUPS-Get-Default", [0x4002] = "CUPS-Get-Printers", [0x4003] = "CUPS-Add-Modify-Printer",
        [0x4004] = "CUPS-Delete-Printer", [0x4005] = "CUPS-Get-Classes", [0x4006] = "CUPS-Add-Modify-Class",
        [0x4007] = "CUPS-Delete-Class", [0x4008] = "CUPS-Accept-Jobs", [0x4009] = "CUPS-Reject-Jobs",
        [0x400A] = "CUPS-Set-Default", [0x400B] = "CUPS-Get-Devices", [0x400C] = "CUPS-Get-PPDs",
        [0x400D] = "CUPS-Move-Job", [0x400E] = "CUPS-Authenticate-Job", [0x400F] = "CUPS-Get-PPD",
        [0x4027] = "CUPS-Get-Document", [0x4028] = "CUPS-Create-Local-Printer"
    };

    public static string Operation(int value) =>
        OperationNames.TryGetValue(value, out var name) ? name : $"0x{value:X4}";

    /// <summary>Formats an enum attribute by name when we know it.</summary>
    public static string Describe(string attributeName, int value) => attributeName switch
    {
        "printer-state" => PrinterState(value),
        "job-state" => JobState(value),
        "operations-supported" => Operation(value),
        "orientation-requested" or "orientation-requested-default" or "orientation-requested-supported" => value switch
        {
            3 => "portrait", 4 => "landscape", 5 => "reverse-landscape", 6 => "reverse-portrait", 7 => "none", _ => value.ToString()
        },
        "print-quality" or "print-quality-default" or "print-quality-supported" => value switch
        {
            3 => "draft", 4 => "normal", 5 => "high", _ => value.ToString()
        },
        "finishings" or "finishings-default" or "finishings-supported" => value switch
        {
            3 => "none", 4 => "staple", 5 => "punch", 6 => "cover", 7 => "bind", _ => value.ToString()
        },
        _ => value.ToString()
    };
}
