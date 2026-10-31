namespace DocService.Core.Extraction.IncidentReports;

public enum Severity
{
    Low,
    Medium,
    High,
    Critical,
    Unknown,
}

public sealed record IncidentReport(
    DateOnly? Date,
    string? Location,
    IReadOnlyList<string> Equipment,
    Severity Severity,
    string Summary);
