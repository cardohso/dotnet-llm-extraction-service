using System.Diagnostics.CodeAnalysis;

namespace DocService.Core.Extraction;

public sealed class ExtractionSchemaRegistry(IEnumerable<IExtractionSchema> schemas)
{
    private readonly Dictionary<string, IExtractionSchema> _schemas =
        schemas.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Names => _schemas.Keys;

    public bool TryGet(string name, [NotNullWhen(true)] out IExtractionSchema? schema) =>
        _schemas.TryGetValue(name, out schema);
}
