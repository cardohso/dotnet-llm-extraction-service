using DocService.Core.Llm;

namespace DocService.Llm.Fake;

/// <summary>Schema-valid fixed replies so the service can run end to end without an API key.</summary>
public static class CannedResponder
{
    private const string IncidentReport =
        """{"date":"2025-01-15","location":"Example Station","equipment":["example signal"],"severity":"low","summary":"Canned response from the fake provider."}""";

    private const string Summary =
        """{"summary":"Canned response from the fake provider.","sourceIndices":[0]}""";

    public static LlmResult Respond(LlmRequest request)
    {
        var content = request.ResponseSchema?.Name switch
        {
            "incident-report" => IncidentReport,
            "summary-with-sources" => Summary,
            _ => "{}",
        };
        return new LlmResult(content, new LlmUsage(100, 50), "fake", "stop");
    }
}
