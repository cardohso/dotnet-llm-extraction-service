using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DocService.Core.Llm;

namespace DocService.Core.Extraction.IncidentReports;

public sealed class IncidentReportSchema(TimeProvider timeProvider) : IExtractionSchema
{
    public const string SchemaName = "incident-report";

    private const int MaxLocationLength = 200;
    private const int MaxEquipmentItems = 20;
    private const int MaxEquipmentLength = 100;
    private const int MaxSummaryLength = 300;

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private static readonly JsonObject JsonSchema = LoadJsonSchema();

    public string Name => SchemaName;

    public string Instructions =>
        "Extract an incident report. Use only facts stated in the text. " +
        "Use null for date or location when they are not stated, an empty array when no equipment is named, " +
        "and \"unknown\" for severity when it cannot be determined. " +
        "Never infer a date from relative words such as \"yesterday\" or \"last week\"; use null instead. " +
        "Write the summary in the language of the text, in one sentence of at most 300 characters.";

    public LlmJsonSchema ResponseSchema { get; } = new(SchemaName, (JsonObject)JsonSchema.DeepClone());

    public SchemaParseResult Parse(string content)
    {
        IncidentReport? report;
        try
        {
            report = JsonSerializer.Deserialize<IncidentReport>(JsonContent.Normalize(content), SerializerOptions);
        }
        catch (JsonException ex)
        {
            return SchemaParseResult.Failure($"invalid JSON: {ex.Message}");
        }

        if (report is null)
        {
            return SchemaParseResult.Failure("invalid JSON: expected an object, got null");
        }

        var errors = Validate(report);
        return errors.Count == 0 ? SchemaParseResult.Success(report) : new SchemaParseResult(null, errors);
    }

    private List<string> Validate(IncidentReport report)
    {
        var errors = new List<string>();

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (report.Date > today)
        {
            errors.Add($"date: must not be after {today:yyyy-MM-dd}");
        }

        if (report.Location is { } location && (location.Length is 0 or > MaxLocationLength))
        {
            errors.Add($"location: must be 1-{MaxLocationLength} characters or null");
        }

        if (report.Equipment.Count > MaxEquipmentItems)
        {
            errors.Add($"equipment: at most {MaxEquipmentItems} items");
        }

        if (report.Equipment.Any(e => e.Length is 0 or > MaxEquipmentLength))
        {
            errors.Add($"equipment: each item must be 1-{MaxEquipmentLength} characters");
        }

        if (report.Equipment.Distinct(StringComparer.OrdinalIgnoreCase).Count() != report.Equipment.Count)
        {
            errors.Add("equipment: items must be unique");
        }

        if (report.Summary.Length is 0 or > MaxSummaryLength)
        {
            errors.Add($"summary: must be 1-{MaxSummaryLength} characters");
        }

        return errors;
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static JsonObject LoadJsonSchema()
    {
        using var stream = typeof(IncidentReportSchema).Assembly
            .GetManifestResourceStream("incident-report.schema.json")
            ?? throw new InvalidOperationException("Embedded resource incident-report.schema.json not found");
        return JsonNode.Parse(stream)!.AsObject();
    }
}
