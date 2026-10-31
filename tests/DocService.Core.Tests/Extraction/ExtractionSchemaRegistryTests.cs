using DocService.Core.Extraction;
using DocService.Core.Extraction.IncidentReports;

namespace DocService.Core.Tests.Extraction;

public class ExtractionSchemaRegistryTests
{
    [Fact]
    public void Registry_TryGet_IsCaseInsensitive()
    {
        var registry = new ExtractionSchemaRegistry([new IncidentReportSchema(TimeProvider.System)]);

        Assert.True(registry.TryGet("Incident-Report", out var schema));
        Assert.Equal("incident-report", schema.Name);
        Assert.False(registry.TryGet("invoice", out _));
        Assert.Equal(["incident-report"], registry.Names);
    }
}
