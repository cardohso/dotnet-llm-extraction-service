using DocService.Core.Extraction;

namespace DocService.Core.Tests.Extraction;

public class JsonContentTests
{
    private const string Bare = """{"a":1}""";

    [Theory]
    [InlineData("{\"a\":1}")]
    [InlineData("  \n{\"a\":1}\n  ")]
    [InlineData("```\n{\"a\":1}\n```")]
    [InlineData("```json\n{\"a\":1}\n```")]
    [InlineData("```json\n{\"a\":1}\n```\n")]
    [InlineData("```json {\"a\":1}```")]
    [InlineData("Here is the JSON:\n```json\n{\"a\":1}\n```")]
    [InlineData("Result: {\"a\":1}")]
    public void Normalize_ReturnsBareObject(string input) =>
        Assert.Equal(Bare, JsonContent.Normalize(input));
}
