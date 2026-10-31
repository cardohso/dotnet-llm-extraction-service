using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using DocService.Core.Llm;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace DocService.Llm.OpenAiCompatible;

/// <summary>Chat-completions client for any OpenAI-compatible endpoint (Groq, Gemini, ...).</summary>
public sealed class OpenAiCompatibleLlmClient(
    HttpClient httpClient,
    IOptions<LlmOptions> options,
    TimeProvider timeProvider) : ILlmClient
{
    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(BuildBody(settings, request).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var response = await SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw MapFailure(response);
        }

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var choice = payload?["choices"]?.AsArray().FirstOrDefault();
        var content = choice?["message"]?["content"]?.GetValue<string>();
        if (choice is null || content is null)
        {
            throw new LlmProviderException("Provider returned no completion", (int)response.StatusCode);
        }

        var usage = new LlmUsage(
            payload?["usage"]?["prompt_tokens"]?.GetValue<int>() ?? 0,
            payload?["usage"]?["completion_tokens"]?.GetValue<int>() ?? 0);
        return new LlmResult(
            content,
            usage,
            payload?["model"]?.GetValue<string>() ?? settings.Model,
            choice["finish_reason"]?.GetValue<string>() ?? "stop");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (BrokenCircuitException ex)
        {
            throw new LlmUnavailableException("Provider circuit breaker is open", ex);
        }
        catch (TimeoutRejectedException ex)
        {
            throw new LlmTimeoutException("Provider call timed out", ex);
        }
        catch (OperationCanceledException ex)
        {
            throw new LlmTimeoutException("Provider call timed out", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmUnavailableException("Could not reach the provider", ex);
        }
    }

    private LlmException MapFailure(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        return response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests =>
                new LlmRateLimitedException("Provider rate limit exceeded", RetryAfter(response)),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new LlmProviderException("Provider rejected the API key", status),
            _ => new LlmProviderException($"Provider returned HTTP {status}", status),
        };
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }

        return header?.Date is { } date ? date - timeProvider.GetUtcNow() : null;
    }

    private static JsonObject BuildBody(LlmOptions settings, LlmRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = request.UserPrompt }),
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxOutputTokens,
        };

        if (!string.IsNullOrEmpty(settings.ReasoningEffort))
        {
            body["reasoning_effort"] = settings.ReasoningEffort;
        }

        if (request.ResponseSchema is { } schema)
        {
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = schema.Name,
                    ["strict"] = true,
                    ["schema"] = schema.Schema.DeepClone(),
                },
            };
        }

        return body;
    }
}
