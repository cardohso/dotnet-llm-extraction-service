namespace DocService.Api.Tests;

internal static class TestData
{
    public const string IncidentJson =
        """{"date":"2026-03-03","location":"Entroncamento depot","equipment":["points machine P12"],"severity":"medium","summary":"Points failure delayed two services."}""";

    public const string SummaryJson = """{"summary":"Doors failed.","sourceIndices":[1]}""";

    public const string ProblemJsonType = "application/problem+json";

    public static string ProblemType(string suffix) => $"urn:docservice:problem:{suffix}";
}
