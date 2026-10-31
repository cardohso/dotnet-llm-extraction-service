using System.Text.Json.Nodes;
using DocService.Core.Extraction;
using DocService.Core.Extraction.IncidentReports;
using Microsoft.Extensions.Time.Testing;

namespace DocService.Core.Tests.Extraction;

public class IncidentReportSchemaTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
    private readonly IncidentReportSchema _schema = new(Clock);

    private static string Json(
        string date = "\"2026-03-03\"",
        string location = "\"Entroncamento depot\"",
        string equipment = "[\"points machine P12\"]",
        string severity = "\"medium\"",
        string summary = "\"Points failure delayed two services.\"",
        string extra = "") =>
        $$"""{"date":{{date}},"location":{{location}},"equipment":{{equipment}},"severity":{{severity}},"summary":{{summary}}{{extra}}}""";

    [Fact]
    public void Parse_ValidReport_ReturnsData()
    {
        var result = _schema.Parse(Json());

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        var report = Assert.IsType<IncidentReport>(result.Data);
        Assert.Equal(new DateOnly(2026, 3, 3), report.Date);
        Assert.Equal("Entroncamento depot", report.Location);
        Assert.Equal(["points machine P12"], report.Equipment);
        Assert.Equal(Severity.Medium, report.Severity);
        Assert.Equal("Points failure delayed two services.", report.Summary);
    }

    [Fact]
    public void Parse_NullDateAndLocation_IsValid()
    {
        var result = _schema.Parse(Json(date: "null", location: "null", equipment: "[]", severity: "\"unknown\""));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        var report = Assert.IsType<IncidentReport>(result.Data);
        Assert.Null(report.Date);
        Assert.Null(report.Location);
        Assert.Empty(report.Equipment);
        Assert.Equal(Severity.Unknown, report.Severity);
    }

    [Fact]
    public void Parse_FutureDate_Error()
    {
        var result = _schema.Parse(Json(date: "\"2026-11-01\""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.StartsWith("date", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_UnknownField_Error()
    {
        var result = _schema.Parse(Json(extra: ",\"operator\":\"CP\""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.StartsWith("invalid JSON", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_BadSeverity_Error()
    {
        var result = _schema.Parse(Json(severity: "\"catastrophic\""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.StartsWith("invalid JSON", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_MissingRequiredField_Error()
    {
        var result = _schema.Parse("""{"date":null,"location":null,"severity":"low","summary":"x"}""");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Parse_SummaryOver300_Error()
    {
        var result = _schema.Parse(Json(summary: $"\"{new string('a', 301)}\""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.StartsWith("summary", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_DuplicateEquipment_Error()
    {
        var result = _schema.Parse(Json(equipment: "[\"signal S4\",\"Signal S4\"]"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.StartsWith("equipment", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_MalformedJson_Error()
    {
        var result = _schema.Parse("not json at all");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.StartsWith("invalid JSON", StringComparison.Ordinal));
    }

    [Fact]
    public void JsonSchema_MatchesRecordProperties()
    {
        var recordProps = typeof(IncidentReport).GetProperties()
            .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
            .Order()
            .ToArray();
        var schema = _schema.ResponseSchema.Schema;
        var schemaProps = schema["properties"]!.AsObject().Select(p => p.Key).Order().ToArray();
        var required = schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).Order().ToArray();

        Assert.Equal(recordProps, schemaProps);
        Assert.Equal(recordProps, required);
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.Equal("incident-report", _schema.ResponseSchema.Name);
    }
}
