using Microsoft.Extensions.Options;

namespace DocService.Llm;

public sealed class LlmOptionsValidator : IValidateOptions<LlmOptions>
{
    public ValidateOptionsResult Validate(string? name, LlmOptions options)
    {
        if (options.Provider == LlmProvider.Fake)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Llm:ApiKey is not set. Use dotnet user-secrets or the Llm__ApiKey environment variable.");
        }

        if (!options.BaseUrl.IsAbsoluteUri || !options.BaseUrl.AbsoluteUri.EndsWith('/'))
        {
            failures.Add("Llm:BaseUrl must be an absolute URL ending in '/'.");
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            failures.Add("Llm:Model must not be empty.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
