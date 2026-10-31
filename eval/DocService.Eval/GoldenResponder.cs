using System.Text.Json;
using DocService.Core.Llm;

namespace DocService.Eval;

/// <summary>Fake-provider responder that answers each golden text with its expected fields (CI smoke run).</summary>
internal static class GoldenResponder
{
    public static Func<LlmRequest, LlmResult> For(IReadOnlyList<GoldenCase> cases)
    {
        var byText = cases.ToDictionary(c => c.Text);
        return request =>
        {
            if (!byText.TryGetValue(request.UserPrompt, out var golden))
            {
                throw new InvalidOperationException("GoldenResponder received text that is not in the golden set");
            }

            var json = JsonSerializer.Serialize(new
            {
                date = golden.Expected.Date,
                location = golden.Expected.Location,
                equipment = golden.Expected.Equipment,
                severity = golden.Expected.Severity,
                summary = "Golden answer.",
            });
            return new LlmResult(json, new LlmUsage(100, 50), "golden-fake", "stop");
        };
    }
}
