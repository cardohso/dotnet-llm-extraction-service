using System.Text.Json;

namespace DocService.Eval;

internal sealed record ExpectedFields(string? Date, string? Location, IReadOnlyList<string> Equipment, string Severity);

internal sealed record GoldenCase(string Id, string Lang, string? Trap, string Text, ExpectedFields Expected);

internal static class GoldenLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static IReadOnlyList<GoldenCase> Load(string path)
    {
        var cases = File.ReadLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select((line, index) => JsonSerializer.Deserialize<GoldenCase>(line, Options)
                ?? throw new InvalidDataException($"{path}: line {index + 1} is not a golden case"))
            .ToList();

        var duplicate = cases.GroupBy(c => c.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"{path}: duplicate case id '{duplicate.Key}'");
        }

        return cases;
    }
}
