# dotnet-llm-extraction-service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An ASP.NET Core (.NET 10) minimal API that wraps an LLM for validated structured extraction and grounded summaries, with tests, evaluation, Docker and CI.

**Architecture:** `Core` holds the domain (LLM abstraction, schemas, extraction and summary services, budget). `Llm` holds the provider clients (OpenAI-compatible over `HttpClient`, deterministic fake). `Api` composes them, maps results to HTTP and owns middleware (rate limiting, ProblemDetails, health, OpenAPI). `Eval` reuses the same DI registrations against a golden set.

**Tech Stack:** .NET 10 SDK, ASP.NET Core minimal APIs, `Microsoft.AspNetCore.OpenApi`, `Swashbuckle.AspNetCore.SwaggerUI` (UI only), `Microsoft.Extensions.Http.Resilience`, xUnit v3, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Extensions.Diagnostics.Testing`, Docker, GitHub Actions. No FluentAssertions (licence change) — plain `Assert`.

**Spec:** `docs/specs/2026-10-30-design.md`

## Global Constraints

- Target `net10.0`; `Nullable=enable`; `TreatWarningsAsErrors=true`; `AnalysisLevel=latest`; `ImplicitUsings=enable`.
- Central package management (`Directory.Packages.props`); latest stable versions at Task 1, pinned exactly.
- `dotnet format --verify-no-changes` must pass before every commit.
- No test touches the network.
- Secrets: only user-secrets (`UserSecretsId` shared by Api and Eval) or the `Llm__ApiKey` env var. `.env` git-ignored; `.env.example` committed. Never print a key.
- Defaults: Groq `https://api.groq.com/openai/v1/`, model `openai/gpt-oss-20b`, `ReasoningEffort=low`, temperature 0.
- Limits: max input 20,000 chars; token budget 16,000 per request; `max_tokens` extract 800 / summarise 600; max 200 sentences; `maxSentences` 1–10 default 3.
- Transport: total 30 s, attempt 10 s, 2 retries (jitter, honours `Retry-After`), breaker ratio 0.5 / sampling 30 s / min throughput 5 / break 30 s.
- Rate limit: per-IP token bucket (limit 5, 1 token per 3 s ≈ 20/min); global concurrency 4, queue 10. Not applied to `/health/*` or `/openapi/*`.
- ProblemDetails `type` = `urn:docservice:problem:<suffix>`; every problem has `traceId`.
- Logs never contain input text; only length and the first 12 hex chars of its SHA-256.
- Ask João before: installing software, creating the GitHub repo, pushing, making anything public.

## Review Focus

1. Model wraps JSON in ```` ```json ```` fences or adds whitespace (common on the Gemini path) → output is still parsed. Test: Task 4 `Extract_StripsCodeFences`.
2. Output cut off (`finish_reason = "length"`) → `422` with reason `output-truncated`, no repair (a repair with the same cap would truncate again). Test: Task 4 `Extract_TruncatedOutput_NoRepair`.
3. Missing or wrong API key (provider 401/403) → `502` with a clear detail, no retry. Tests: Task 6 `Unauthorized_MapsToProviderException_NoRetry` (mapping), Task 8 `ResilienceTests.Unauthorized_NotRetried` (call count).
4. Client disconnects mid-call → `OperationCanceledException` propagates, no repair attempt, not logged as a provider failure. Test: Task 4 `Extract_Cancelled_DoesNotRepair`.
5. Summarise input with one sentence, or `maxSentences` larger than the sentence count → works; the model may cite only existing sentences. Test: Task 5 `Summarise_SingleSentence_Works`.

---

### Task 0: Environment check ⏸ (Sonnet, 0.3 h)

- [ ] **Step 1:** Run `dotnet --list-sdks`, `docker --version`, `gh --version`, `git --version`, `gitleaks version`. Report which are missing.
- [ ] **Step 2:** ⏸ Ask João before installing anything missing. Proposal: `brew install --cask dotnet-sdk` (10.x), `brew install gh gitleaks`; Docker Desktop (free personal) or `brew install colima docker`.
- [ ] **Step 3:** Verify: `dotnet --list-sdks` shows `10.0.x`.

### Task 1: Solution skeleton and CI (Sonnet, 1.2 h)

**Files:**
- Create: `dotnet-llm-extraction-service.sln`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore` (`dotnet new gitignore` + `.env`), `.env.example`, `global.json` (sdk 10.0.x, `rollForward: latestFeature`)
- Create projects: `src/DocService.Core` (classlib), `src/DocService.Llm` (classlib), `src/DocService.Api` (web, empty), `eval/DocService.Eval` (console), `tests/DocService.Core.Tests`, `tests/DocService.Api.Tests`, `tests/DocService.Eval.Tests` (xUnit v3)
- Create: `.github/workflows/ci.yml`
- Move in: `docs/specs/2026-10-30-design.md`, this plan

**Interfaces:**
- Produces: project references `Api → Core, Llm`; `Llm → Core`; `Eval → Core, Llm`; `Core.Tests → Core, Llm` (fake and provider client tests live there); `Eval.Tests → Eval`; `Api.Tests → Api` with `InternalsVisibleTo`. `public partial class Program;` in Api for `WebApplicationFactory`.

- [ ] **Step 1:** `git init`, create the solution and projects, add references and packages.
- [ ] **Step 2:** Write the trivial test `tests/DocService.Api.Tests/SmokeTests.cs`:

```csharp
[Fact]
public async Task App_Starts_And_LivenessReturns200() // temporary: Program maps /health/live => Results.Ok()
{
    await using var factory = new WebApplicationFactory<Program>();
    var response = await factory.CreateClient().GetAsync("/health/live", TestContext.Current.CancellationToken);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

- [ ] **Step 3:** Run `dotnet test` → FAIL (endpoint missing). Map it, rerun → PASS.
- [ ] **Step 4:** `ci.yml` on `push` and `pull_request`: `actions/checkout@v4`, `actions/setup-dotnet@v4` (`global-json-file: global.json`), `dotnet restore`, `dotnet format --verify-no-changes --no-restore`, `dotnet build -c Release --no-restore`, `dotnet test -c Release --no-build`. (Eval smoke step added in Task 9.)
- [ ] **Step 5:** Verify locally: `dotnet format --verify-no-changes && dotnet build -c Release && dotnet test -c Release` all exit 0.
- [ ] **Step 6:** Commit `chore: solution skeleton, CI, design spec`. ⏸ Ask João before `gh repo create` (repo stays private until Task 12).

### Task 2: LLM abstraction and fake client (Sonnet, 1 h)

**Files:**
- Create: `src/DocService.Core/Llm/ILlmClient.cs`, `LlmModels.cs`, `LlmExceptions.cs`
- Create: `src/DocService.Llm/Fake/FakeLlmClient.cs`
- Test: `tests/DocService.Core.Tests/Llm/FakeLlmClientTests.cs`

**Interfaces:**
- Produces (namespace `DocService.Core.Llm`):
  - `interface ILlmClient { Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken cancellationToken); }`
  - `record LlmJsonSchema(string Name, JsonObject Schema)`
  - `record LlmRequest(string SystemPrompt, string UserPrompt, LlmJsonSchema? ResponseSchema, int MaxOutputTokens, double Temperature = 0)`
  - `record LlmUsage(int InputTokens, int OutputTokens)` with `static LlmUsage Zero`
  - `record LlmResult(string Content, LlmUsage Usage, string Model, string FinishReason)`
  - `abstract class LlmException : Exception`; `LlmRateLimitedException(TimeSpan? RetryAfter)`, `LlmUnavailableException`, `LlmTimeoutException`, `LlmProviderException(int? StatusCode)`
- Produces (namespace `DocService.Llm.Fake`): `sealed class FakeLlmClient : ILlmClient`
  - `FakeLlmClient Enqueue(string content, LlmUsage? usage = null, string finishReason = "stop")`
  - `FakeLlmClient EnqueueException(Exception exception)`
  - `FakeLlmClient EnqueueHang()` — awaits `Task.Delay(Infinite, ct)`
  - `Func<LlmRequest, LlmResult>? Responder` — used when the queue is empty; if null and queue empty → `InvalidOperationException("FakeLlmClient queue is empty")`
  - `IReadOnlyList<LlmRequest> Requests`
  - Default usage when not given: `new LlmUsage(100, 50)`; model `"fake"`.
  - Thread-safe (`ConcurrentQueue`, lock on `Requests`).

- [ ] **Step 1: Failing tests:** `Enqueue_ReturnsInOrder_AndRecordsRequests` (two enqueued → contents in order, `Requests.Count == 2`); `EnqueueException_Throws`; `EmptyQueue_NoResponder_Throws`; `EmptyQueue_UsesResponder`; `EnqueueHang_HonoursCancellation` (cancel after 50 ms → `OperationCanceledException`).
- [ ] **Step 2:** `dotnet test tests/DocService.Core.Tests` → FAIL (types missing).
- [ ] **Step 3:** Implement the types above.
- [ ] **Step 4:** Rerun → PASS. Commit `feat(core): LLM client abstraction and deterministic fake`.

### Task 3: Incident-report schema and registry (Sonnet, 1.2 h)

**Files:**
- Create: `src/DocService.Core/Extraction/IExtractionSchema.cs`, `SchemaParseResult.cs`, `ExtractionSchemaRegistry.cs`, `JsonContent.cs`
- Create: `src/DocService.Core/Extraction/IncidentReports/IncidentReport.cs`, `IncidentReportSchema.cs`, `incident-report.schema.json` (EmbeddedResource)
- Test: `tests/DocService.Core.Tests/Extraction/IncidentReportSchemaTests.cs`, `JsonContentTests.cs`

**Interfaces:**
- Consumes: `LlmJsonSchema`.
- Produces:
  - `interface IExtractionSchema { string Name { get; } string Instructions { get; } LlmJsonSchema ResponseSchema { get; } SchemaParseResult Parse(string content); }`
  - `record SchemaParseResult(object? Data, IReadOnlyList<string> Errors) { bool IsValid => Data is not null && Errors.Count == 0; }`
  - `sealed class ExtractionSchemaRegistry(IEnumerable<IExtractionSchema> schemas) { bool TryGet(string name, [NotNullWhen(true)] out IExtractionSchema? schema); IReadOnlyCollection<string> Names { get; } }` — case-insensitive names.
  - `static class JsonContent { static string Normalize(string content); }` — trims; strips a leading ```` ``` ```` / ```` ```json ```` line and trailing ```` ``` ````.
  - `record IncidentReport(DateOnly? Date, string? Location, IReadOnlyList<string> Equipment, Severity Severity, string Summary)`
  - `enum Severity { Low, Medium, High, Critical, Unknown }` serialised lower-case.
  - `sealed class IncidentReportSchema(TimeProvider timeProvider) : IExtractionSchema` — `Name = "incident-report"`.
- Parse: `JsonSerializer` with `JsonSerializerDefaults.Web`, `UnmappedMemberHandling = Disallow`, `JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)`. A `JsonException` becomes one error `"invalid JSON: <message>"`. Field rules exactly as spec §6; error strings name the field, e.g. `"date: must not be after 2026-10-30"`.
- JSON schema: `additionalProperties: false`, all five fields `required` (nullable via `"type": ["string","null"]`) — strict mode needs every property required.

- [ ] **Step 1: Failing tests:**
  - `Parse_ValidReport_ReturnsData` (asserts each field value)
  - `Parse_NullDateAndLocation_IsValid`
  - `Parse_FutureDate_Error` (FakeTimeProvider at 2026-10-30; date 2026-11-01 → error contains `"date"`)
  - `Parse_UnknownField_Error`; `Parse_BadSeverity_Error`; `Parse_SummaryOver300_Error`; `Parse_DuplicateEquipment_Error`; `Parse_MalformedJson_Error`
  - `JsonSchema_MatchesRecordProperties` (schema `properties` keys == camelCase record property names, and == `required`)
  - `JsonContentTests`: fenced, ```` ```json ````-fenced, whitespace-padded, and plain inputs all normalise to the bare object.
  - `Registry_TryGet_IsCaseInsensitive`
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS. Commit `feat(core): incident-report schema with strict validation`.

### Task 4: Token budget and extraction service (Sonnet, 1.5 h)

**Files:**
- Create: `src/DocService.Core/Processing/ProcessingOptions.cs`, `PricingOptions.cs`, `TokenBudget.cs`, `ProcessingResult.cs`, `CostEstimator.cs`, `Log.cs`
- Create: `src/DocService.Core/Extraction/ExtractionService.cs`, `ExtractionPrompts.cs`
- Create: `src/DocService.Core/ServiceCollectionExtensions.cs`
- Test: `tests/DocService.Core.Tests/Extraction/ExtractionServiceTests.cs`, `Processing/TokenBudgetTests.cs`

**Interfaces:**
- Consumes: `ILlmClient`, `LlmResult`, `IExtractionSchema`, `ExtractionSchemaRegistry`, `JsonContent`.
- Produces:
  - `ProcessingOptions` (section `Processing`): `MaxInputChars = 20000`, `TokenBudgetPerRequest = 16000`, `ExtractMaxOutputTokens = 800`, `SummariseMaxOutputTokens = 600`, `MaxSentences = 200`.
  - `PricingOptions` (section `Llm:Pricing`): `decimal InputPerMillionUsd = 0`, `decimal OutputPerMillionUsd = 0`.
  - `sealed class TokenBudget(int limit) { static int Estimate(string text) => (text.Length + 3) / 4; int Used { get; } void Record(LlmUsage usage); bool CanAfford(int estimatedTokens); LlmUsage Total { get; } }`
  - `record RequestUsage(int InputTokens, int OutputTokens, decimal EstimatedCostUsd)`
  - `enum ProcessingError { UnknownSchema, InputTooLarge, InvalidOutput, BudgetExhausted, OutputTruncated }`
  - `record ProcessingResult<T>(T? Value, int Attempts, RequestUsage Usage, ProcessingError? Error, IReadOnlyList<string> Errors) { bool IsSuccess => Error is null; }` with static `Success(...)` / `Failure(...)` factories.
  - `sealed class CostEstimator(IOptions<PricingOptions>) { RequestUsage ToRequestUsage(LlmUsage usage); }`
  - `sealed class ExtractionService(ILlmClient, ExtractionSchemaRegistry, IOptions<ProcessingOptions>, CostEstimator, ILogger<ExtractionService>)` — creates `new TokenBudget(options.TokenBudgetPerRequest)` per call.
    `Task<ProcessingResult<object>> ExtractAsync(string schemaName, string text, CancellationToken cancellationToken)`
  - `static IServiceCollection AddDocServiceCore(this IServiceCollection services, IConfiguration configuration)` — binds both options with `ValidateDataAnnotations().ValidateOnStart()`, registers `TimeProvider.System`, schemas, registry, services as singletons.
  - `static partial class Log` (source-generated): `InputReceived(ILogger, string task, int length, string hashPrefix)`, `LlmCallCompleted(ILogger, string task, int attempt, string model, int inputTokens, int outputTokens, long latencyMs, string outcome)`.
- Algorithm:
  1. Unknown schema → `UnknownSchema`, 0 attempts. Text length > `MaxInputChars` → `InputTooLarge`, 0 attempts.
  2. Attempt 1: `LlmRequest(system = ExtractionPrompts.System(schema), user = text, schema.ResponseSchema, ExtractMaxOutputTokens)`. Record usage.
  3. `FinishReason == "length"` → `OutputTruncated`, no repair.
  4. Parse `JsonContent.Normalize(content)`. Valid → success.
  5. Invalid: estimate repair = `Estimate(system + user + previous output + errors) + ExtractMaxOutputTokens`. `!CanAfford` → `BudgetExhausted` with the parse errors. Otherwise attempt 2 with `ExtractionPrompts.Repair(text, previousOutput, errors)`.
  6. Attempt 2 invalid or truncated → `InvalidOutput` / `OutputTruncated` with errors, `Attempts = 2`.
  - `OperationCanceledException` is never caught. `LlmException` propagates to the API.
  - System prompt says: extract only stated facts; null / `"unknown"` when absent; never infer dates from relative words.

- [ ] **Step 1: Failing tests** (FakeLlmClient, `NullLogger` except where noted):
  - `Extract_ValidFirstAttempt_ReturnsData` (`Attempts == 1`, `fake.Requests[0].ResponseSchema!.Name == "incident-report"`, `MaxOutputTokens == 800`)
  - `Extract_InvalidThenValid_RepairsOnce` (`Attempts == 2`; `fake.Requests[1].UserPrompt` contains the first error string)
  - `Extract_InvalidTwice_ReturnsInvalidOutput` (`Error == InvalidOutput`, `Errors` not empty, `fake.Requests.Count == 2`)
  - `Extract_BudgetExhausted_SkipsRepair` (`TokenBudgetPerRequest = 1000`, first usage `(900, 50)` → `BudgetExhausted`, 1 request)
  - `Extract_UnknownSchema_NoLlmCall`; `Extract_TooLarge_NoLlmCall` (20,001 chars)
  - `Extract_StripsCodeFences`
  - `Extract_TruncatedOutput_NoRepair` (`finishReason: "length"` → `OutputTruncated`, 1 request)
  - `Extract_Cancelled_DoesNotRepair` (`EnqueueHang`, cancel → throws `OperationCanceledException`, 1 request)
  - `Extract_UsageSummedAcrossAttempts` (`(100,50)+(120,60)` → `Usage.InputTokens == 220`, `OutputTokens == 110`)
  - `Extract_EstimatesCost` (prices 1.00 / 2.00 per million, usage `(1_000_000, 500_000)` → `EstimatedCostUsd == 2.00m`)
  - `Extract_NeverLogsInputText` (`FakeLogger`; no record's message contains the input sentinel `"SENTINEL-TEXT-42"`)
  - `TokenBudgetTests`: `Estimate("abcd") == 1`, `Estimate("abcde") == 2`; `CanAfford` true at exactly the remaining amount, false one over.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS. Commit `feat(core): extraction service with repair retry and token budget`.

### Task 5: Sentence splitter and summary service (Sonnet, 1.2 h)

**Files:**
- Create: `src/DocService.Core/Summaries/SentenceSplitter.cs`, `SummaryService.cs`, `SummaryPrompts.cs`, `SummaryModels.cs`
- Test: `tests/DocService.Core.Tests/Summaries/SentenceSplitterTests.cs`, `SummaryServiceTests.cs`

**Interfaces:**
- Consumes: Task 4 types.
- Produces:
  - `static class SentenceSplitter { static IReadOnlyList<string> Split(string text); }` — split on `[.!?]` followed by whitespace, unless the token before is in `{ "e.g", "i.e", "Mr", "Mrs", "Dr", "St", "No", "nº", "Sr", "Sra" }`; also split on blank lines; trim; drop empties.
  - `record SourceSentence(int Index, string Text)` (0-based)
  - `record SummaryResult(string Summary, IReadOnlyList<SourceSentence> Sources)`
  - `sealed class SummaryService(ILlmClient, IOptions<ProcessingOptions>, CostEstimator, ILogger<SummaryService>)`
    `Task<ProcessingResult<SummaryResult>> SummariseAsync(string text, int maxSentences, CancellationToken cancellationToken)`
- Model schema `summary-with-sources`: `{ summary: string, sourceIndices: integer[] }`, both required. The user prompt lists sentences as `[i] text`.
- Validation of model output: summary non-empty, ≤ 2,000 chars; 1..`maxSentences` indices; distinct; each `0 <= i < sentences.Count`. Same repair, budget and truncation flow as Task 4 (extract a private shared helper only if both services duplicate more than ~20 lines — reviewer's call).
- Too large: `> MaxInputChars` or `> MaxSentences` sentences → `InputTooLarge`.
- Sources returned in ascending index order with the original sentence text.

- [ ] **Step 1: Failing tests:**
  - Splitter: `"A. B? C!"` → 3; `"See Dr. Silva. Done."` → 2; `"Sem pontuação"` → 1; `"Line one\n\nLine two"` → 2; `""` → 0.
  - `Summarise_ReturnsVerbatimSources` (fake returns indices `[2,0]` → sources `[0,2]` with exact input sentences)
  - `Summarise_OutOfRangeIndex_Repairs` (first `[9]`, then `[0]` → `Attempts == 2`)
  - `Summarise_TooManyIndices_Repairs` (`maxSentences = 1`, first `[0,1]`)
  - `Summarise_SingleSentence_Works` (one-sentence text, `maxSentences = 3`, fake `[0]` → success)
  - `Summarise_MaxOutputTokensIs600`
  - `Summarise_Over200Sentences_InputTooLarge`
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS. Commit `feat(core): grounded summary service`.

### Task 6: OpenAI-compatible client, options and DI (Sonnet, 1.5 h)

**Files:**
- Create: `src/DocService.Llm/LlmOptions.cs`, `LlmOptionsValidator.cs`, `ServiceCollectionExtensions.cs`
- Create: `src/DocService.Llm/OpenAiCompatible/OpenAiCompatibleLlmClient.cs`, `ChatCompletionDtos.cs`
- Create: `src/DocService.Llm/Fake/CannedResponder.cs`
- Test: `tests/DocService.Core.Tests/Llm/OpenAiCompatibleLlmClientTests.cs` (+ `StubHttpMessageHandler`)

**Interfaces:**
- Consumes: Task 2 types; `IExtractionSchema` names for canned responses.
- Produces:
  - `enum LlmProvider { OpenAiCompatible, Fake }`
  - `LlmOptions` (section `Llm`): `Provider = OpenAiCompatible`, `Uri BaseUrl = https://api.groq.com/openai/v1/`, `string Model = "openai/gpt-oss-20b"`, `string? ApiKey`, `string? ReasoningEffort = "low"`.
  - `LlmOptionsValidator : IValidateOptions<LlmOptions>` — `OpenAiCompatible` requires non-empty `ApiKey` (message: `"Llm:ApiKey is not set. Use dotnet user-secrets or the Llm__ApiKey environment variable."`) and absolute `BaseUrl` ending in `/`.
  - `static IHttpClientBuilder? AddLlmClient(this IServiceCollection services, IConfiguration configuration)` — Fake → singleton `FakeLlmClient { Responder = CannedResponder.Respond }`, returns null; OpenAiCompatible → `AddHttpClient<ILlmClient, OpenAiCompatibleLlmClient>` with base address and `Authorization: Bearer`, returns the builder (Task 8 adds resilience to it).
  - `static class CannedResponder { static LlmResult Respond(LlmRequest request); }` — schema-valid fixed JSON per `ResponseSchema.Name`; lets the app run without a key (Docker smoke, fresh clone).
  - `OpenAiCompatibleLlmClient(HttpClient, IOptions<LlmOptions>, TimeProvider)` posts `chat/completions` with `model`, `messages` (system, user), `temperature`, `max_tokens`, `reasoning_effort` (omitted when null), and `response_format = { type: "json_schema", json_schema: { name, strict: true, schema } }` when a schema is set.
- Error mapping (after the resilience handler gives up):
  - 429 → `LlmRateLimitedException(Retry-After)`
  - 401/403 → `LlmProviderException(status)` with message `"Provider rejected the API key"`
  - other non-success → `LlmProviderException(status)`
  - `Polly.CircuitBreaker.BrokenCircuitException` → `LlmUnavailableException`
  - `Polly.Timeout.TimeoutRejectedException`, or `TaskCanceledException` when the caller's token is **not** cancelled → `LlmTimeoutException`
  - caller-token cancellation → rethrow as-is
  - empty `choices` → `LlmProviderException(null)`

- [ ] **Step 1: Failing tests** (stub handler captures the request):
  - `SendsSchemaAndLimits` (body JSON: `model == "openai/gpt-oss-20b"`, `max_tokens == 800`, `response_format.json_schema.strict == true`, `reasoning_effort == "low"`, header `Authorization == "Bearer test-key"`, path ends `/chat/completions`)
  - `ParsesContentUsageAndFinishReason` (canned Groq-shaped response → `Usage == (412, 96)`, `FinishReason == "stop"`)
  - `TooManyRequests_MapsToRateLimited_WithRetryAfter` (`Retry-After: 7` → `RetryAfter == 7s`)
  - `Unauthorized_MapsToProviderException_NoRetry` (registered via `AddLlmClient` + resilience in Task 8; here assert mapping and `StatusCode == 401`; Task 8 adds the no-retry count assertion)
  - `CallerCancellation_Rethrows_OperationCanceled`
  - `Validator_MissingKey_Fails_WithHelpfulMessage`; `Validator_FakeProvider_NoKeyNeeded`
  - `CannedResponder_IncidentReport_IsValidForSchema` (parse with `IncidentReportSchema` → `IsValid`)
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS. Commit `feat(llm): OpenAI-compatible client and provider selection`.

### Task 7: Endpoints, request validation, ProblemDetails, OpenAPI (Sonnet, 2 h)

**Files:**
- Modify: `src/DocService.Api/Program.cs`
- Create: `src/DocService.Api/Endpoints/ExtractEndpoints.cs`, `SummariseEndpoints.cs`, `Contracts.cs`
- Create: `src/DocService.Api/Errors/Problems.cs`, `LlmExceptionHandler.cs`
- Create: `src/DocService.Api/appsettings.json`, `appsettings.Development.json`
- Test: `tests/DocService.Api.Tests/DocServiceFactory.cs`, `ExtractEndpointTests.cs`, `SummariseEndpointTests.cs`, `ProblemDetailsTests.cs`

**Interfaces:**
- Consumes: `ExtractionService`, `SummaryService`, `ProcessingResult<T>`, `LlmException` types, `AddDocServiceCore`, `AddLlmClient`.
- Produces:
  - `record ExtractRequest([property: Required] string Schema, [property: Required, MinLength(1)] string Text)`
  - `record ExtractResponse(string Schema, object Data, int Attempts, RequestUsage Usage)`
  - `record SummariseRequest([property: Required, MinLength(1)] string Text, [property: Range(1, 10)] int MaxSentences = 3)`
  - `record SummariseResponse(string Summary, IReadOnlyList<SourceSentence> Sources, int Attempts, RequestUsage Usage)`
  - `static IResult Problems.From(ProcessingError error, IReadOnlyList<string> errors)` — `UnknownSchema`→400 `validation`; `InputTooLarge`→413 `input-too-large`; `InvalidOutput`/`OutputTruncated`/`BudgetExhausted`→422 `llm-output-invalid` with `extensions["reason"]` = `invalid-output` / `output-truncated` / `budget-exhausted` and `extensions["errors"]`.
  - `LlmExceptionHandler : IExceptionHandler` — `LlmProviderException`→502 `provider-error`; `LlmUnavailableException`→503 `provider-unavailable`; `LlmRateLimitedException`→503 `provider-unavailable` with `Retry-After`; `LlmTimeoutException`→504 `provider-timeout`. Detail never includes the API key or input text.
  - `AddProblemDetails(o => o.CustomizeProblemDetails = add traceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier)`; `UseExceptionHandler()`; `UseStatusCodePages()`.
  - Request validation: `builder.Services.AddValidation()` (.NET 10 minimal-API validation, returns `ValidationProblem`); verify on first run, fall back to an endpoint filter calling `Validator.TryValidateObject` if the generator doesn't pick up the records. Set `type` to `urn:docservice:problem:validation`.
  - `AddOpenApi()`, `MapOpenApi()` (`/openapi/v1.json`); Swagger UI at `/swagger` in Development only.
  - `DocServiceFactory : WebApplicationFactory<Program>` — sets `Llm:Provider=Fake`, replaces `ILlmClient` with a per-test `FakeLlmClient Fake` property; `WithConfig(key, value)` helper.

- [ ] **Step 1: Failing tests:**
  - `Extract_Valid_200` (asserts `data.severity == "medium"`, `attempts == 1`)
  - `Extract_UnknownSchema_400_ProblemDetails` (`type == "urn:docservice:problem:validation"`, has `traceId`)
  - `Extract_EmptyText_400`; `Extract_TooLarge_413`
  - `Extract_InvalidTwice_422_WithErrors` (`reason == "invalid-output"`, `errors` non-empty)
  - `Summarise_Valid_200_WithSources`; `Summarise_MaxSentences11_400`
  - `ProviderError_502`; `CircuitOpen_503`; `ProviderTimeout_504` (fake `EnqueueException`)
  - `ProblemDetails_ContentType_IsProblemJson`
  - `OpenApi_DocumentServed` (`/openapi/v1.json` 200, contains `"/extract"` and `"/summarise"`)
  - `Extract_PortugueseText_RoundTripsUtf8` (equipment `"aparelho de mudança de via"` returned unchanged)
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS. Commit `feat(api): extract and summarise endpoints with ProblemDetails`.

### Task 8: Resilience, rate limiting, logging, health (Sonnet, 2 h)

**Files:**
- Modify: `src/DocService.Llm/ServiceCollectionExtensions.cs`, `src/DocService.Api/Program.cs`, `appsettings.json`
- Create: `src/DocService.Api/RateLimiting/RateLimitingSetup.cs`, `src/DocService.Api/Health/ProviderCircuitHealthCheck.cs`
- Test: `tests/DocService.Api.Tests/RateLimitingTests.cs`, `HealthTests.cs`, `tests/DocService.Core.Tests/Llm/ResilienceTests.cs`

**Interfaces:**
- Consumes: `IHttpClientBuilder` from `AddLlmClient`.
- Produces:
  - Resilience: `builder.AddStandardResilienceHandler().Configure(configuration.GetSection("Llm:Resilience"))`, with appsettings values from Global Constraints; `ShouldHandle` excludes 401/403/400. `CircuitBreaker.StateProvider` = singleton `CircuitBreakerStateProvider` registered in DI.
  - `ProviderCircuitHealthCheck(CircuitBreakerStateProvider? state)` — `Open`/`Isolated` → Unhealthy; null (Fake provider) → Healthy.
  - Health: `/health/live` (predicate `_ => false`), `/health/ready` (tag `ready`). Replace the Task 1 temporary endpoint.
  - Rate limiting: `RateLimitingOptions` (section `RateLimiting`): `TokenLimit = 5`, `ReplenishmentSeconds = 3`, `TokensPerPeriod = 1`, `ConcurrencyLimit = 4`, `QueueLimit = 10`. `GlobalLimiter = PartitionedRateLimiter.CreateChained(perIpTokenBucket, globalConcurrency)`; paths under `/health` and `/openapi` get `RateLimitPartition.GetNoLimiter`. `OnRejected` → 429 problem `rate-limited` with `Retry-After` when metadata has it.
  - Logging: `builder.Logging.ClearProviders().AddJsonConsole(o => o.IncludeScopes = true)`; request scope with `traceId`. `Log.LlmCallCompleted` called by both services (Task 4/5).
- [ ] **Step 1: Failing tests:**
  - `ResilienceTests.ServerError_RetriedTwice_ThenProviderException` (stub returns 500 always → 3 calls total)
  - `ResilienceTests.Unauthorized_NotRetried` (stub 401 → 1 call)
  - `ResilienceTests.RetryAfter_Honoured` (first 429 with `Retry-After: 1`, then 200 → success, delay ≥ 1 s using `FakeTimeProvider` if supported by the pipeline, otherwise real time with tolerance)
  - `RateLimit_SixthRapidRequest_429` (`TokenLimit=5`; sixth call → 429 problem `rate-limited`)
  - `RateLimit_DoesNotApplyToHealth` (20 rapid `/health/live` → all 200)
  - `Ready_Healthy_WithFakeProvider`; `Ready_Unhealthy_WhenCircuitOpen` (stub state provider reporting `Open` → 503)
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS; full `dotnet test` green. Commit `feat: resilience, rate limiting, structured logging, health checks`.

### Task 9: Golden set and evaluation harness ⏸ (Opus for the golden set, Sonnet for the harness, 3 h)

**Files:**
- Create: `eval/golden/incident-reports.jsonl`, `eval/THRESHOLDS.md`, `eval/thresholds.json`
- Create: `eval/DocService.Eval/Program.cs`, `GoldenCase.cs`, `FieldScorer.cs`, `EvalReport.cs`, `GoldenResponder.cs`
- Modify: `.github/workflows/ci.yml` (add smoke step)
- Test: `tests/DocService.Eval.Tests/FieldScorerTests.cs`, `EvalReportTests.cs`

**Interfaces:**
- Consumes: `AddDocServiceCore`, `AddLlmClient`, `ExtractionService`, `IncidentReport`.
- Produces:
  - Golden line: `{"id":"ir-01","lang":"en"|"pt","text":"...","expected":{"date":"2026-03-03"|null,"location":"..."|null,"equipment":["..."],"severity":"medium"}}` — `record GoldenCase(string Id, string Lang, string Text, ExpectedFields Expected)`.
  - `static class FieldScorer`: `bool Date(DateOnly? expected, DateOnly? actual)`; `bool Severity(Severity e, Severity a)`; `bool Location(string? e, string? a)` (trim, collapse whitespace, case-insensitive; null == null); `double Equipment(IReadOnlyList<string> e, IReadOnlyList<string> a)` (set F1 after the same normalisation; both empty → 1.0).
  - `EvalReport` — per-case rows plus: per-field accuracy, overall = mean(date, location, equipment F1, severity) over all cases (invalid output scores 0 on every field), first-attempt valid rate, valid-after-repair rate, p50/p95 latency (nearest-rank), total tokens. `bool MeetsThresholds(Thresholds t, out IReadOnlyList<string> failures)`; `string ToMarkdown()`.
  - `thresholds.json`: `{ "validAfterRepair": 0.95, "overallFieldAccuracy": 0.85, "severity": 0.80, "p95LatencyMs": 5000 }`.
  - CLI: `--provider groq|fake` (default groq), `--golden <path>`, `--out <dir>` (default `eval/results`), `--delay-ms <n>` (default 2000, for free-tier limits). Runs cases sequentially through `ExtractionService`. Writes `<yyyy-MM-dd-HHmm>-<provider>.json` and `.md`. Exit 1 if any threshold is missed, 2 on harness error.
  - `GoldenResponder` (fake mode): returns each case's expected fields as JSON, so the smoke run scores 100 %.
- [ ] **Step 1 (Opus):** Write 18 synthetic rail incident texts covering: all five severities; 3 with no date; 2 with relative dates only ("yesterday") → `date: null`; 2 with no location; 4 with several pieces of equipment; 1 with no equipment; 3 in Portuguese (European); 2 long (~1,500 chars) with distractor details; 1 mentioning an earlier unrelated incident date. Location names real places (Portuguese/UK stations) but events fictional. ⏸ **Review the golden set with João** before any further step.
- [ ] **Step 2:** Commit `THRESHOLDS.md` + `thresholds.json` + golden set **before** any real run (`eval: golden set and pre-registered thresholds`).
- [ ] **Step 3: Failing tests:** `Location_NormalisesCaseAndWhitespace`; `Location_NullEqualsNull`; `Equipment_F1_PartialMatch` (expected `[a,b]`, actual `[a,c]` → 0.5); `Equipment_BothEmpty_IsOne`; `Date_NullVsValue_False`; `Percentile_NearestRank` (`[100..1000]` step 100 → p95 = 1000, p50 = 500); `Report_InvalidCase_ScoresZero`; `MeetsThresholds_ListsFailures`.
- [ ] **Step 4:** Run → FAIL. **Step 5:** Implement. **Step 6:** Run → PASS.
- [ ] **Step 7:** Smoke: `dotnet run --project eval/DocService.Eval -- --provider fake --out "$(mktemp -d)"` → exit 0, report shows 100 %. Add the same step to `ci.yml`. Commit `feat(eval): evaluation harness with CI smoke run`.

### Task 10: Docker (Sonnet, 1 h)

**Files:**
- Create: `Dockerfile`, `.dockerignore`

**Interfaces:**
- Produces: image listening on 8080, `Llm__Provider` / `Llm__ApiKey` from env.
- Build stage `mcr.microsoft.com/dotnet/sdk:10.0`: restore with only `*.csproj` + props copied first (layer cache), then publish `src/DocService.Api` `-c Release -o /app`.
- Runtime stage `mcr.microsoft.com/dotnet/aspnet:10.0`: `apt-get install -y --no-install-recommends curl` then clean lists; `USER $APP_UID`; `ENV ASPNETCORE_HTTP_PORTS=8080`; `HEALTHCHECK --interval=30s --timeout=3s --retries=3 CMD curl -fsS http://localhost:8080/health/live || exit 1`.
- `.dockerignore`: `bin/`, `obj/`, `.git/`, `.env`, `eval/results/`, `tests/`.

- [ ] **Step 1:** `docker build -t docservice .` → success.
- [ ] **Step 2:** `docker run -d --name ds -p 8080:8080 -e Llm__Provider=Fake docservice`; wait; `docker inspect --format '{{.State.Health.Status}}' ds` → `healthy`; `docker exec ds id -u` → not `0`.
- [ ] **Step 3:** `curl -s localhost:8080/extract -H 'content-type: application/json' -d '{"schema":"incident-report","text":"x"}'` → 200 canned JSON. `docker rm -f ds`.
- [ ] **Step 4:** Commit `build: multi-stage non-root Docker image`.

### Task 11: Real evaluation run (Sonnet, 1 h)

**Files:**
- Create: `.github/workflows/eval.yml`, `eval/results/<run>.json`, `eval/results/<run>.md`

- [ ] **Step 1:** João creates a free Groq key and runs, in his own terminal: `dotnet user-secrets set "Llm:ApiKey" "<key>" --project src/DocService.Api`. Claude never sees the key.
- [ ] **Step 2:** `dotnet run --project eval/DocService.Eval -- --provider groq`. Record the exit code and the Markdown table as-is, pass or fail. If it fails a threshold, report it; do not change thresholds or the golden set to pass.
- [ ] **Step 3:** `eval.yml`: `workflow_dispatch` only; `env: Llm__ApiKey: ${{ secrets.GROQ_API_KEY }}`; run the harness; `actions/upload-artifact@v4` with `eval/results/`.
- [ ] **Step 4:** Commit `eval: first real Groq run`.

### Task 12: README, licence, fresh clone, secret scan, publish ⏸ (Sonnet, 2 h)

**Files:**
- Create: `README.md`, `LICENSE` (MIT, 2026, João Pedro Cardoso)

- [ ] **Step 1:** README sections: 30-second summary; CI badge; Mermaid architecture diagram (client → rate limiter → endpoints → services → `ILlmClient` → resilience → Groq; fake for tests); curl examples for all endpoints with real responses captured from a local run; design decisions (from spec §2 and §7); evaluation results copied from `eval/results/*.md` with run date and model; limitations (spec §12); run locally (user-secrets and Fake-provider paths); Docker; tests.
- [ ] **Step 2:** Fresh-clone test: `git clone <local path> "$(mktemp -d)/fresh"` and follow the README commands literally, Fake provider path and test path. Fix the README for any step that doesn't work.
- [ ] **Step 3:** Secret scan: `gitleaks git --redact -v .` → `no leaks found`. Also `git log -p | grep -iE 'gsk_|api[_-]?key\s*[:=]\s*\S{8,}'` → empty.
- [ ] **Step 4:** ⏸ Show João the README and repo tree.
- [ ] **Step 5:** ⏸ Ask before `gh repo create dotnet-llm-extraction-service --public --source . --push`, then before adding the `GROQ_API_KEY` repo secret (João does it himself in GitHub settings).
- [ ] **Step 6:** Confirm the CI run on GitHub is green (`gh run watch`), badge renders.

---

## Hours

| Task | h | | Task | h |
|---|---|---|---|---|
| 0 Env | 0.3 | | 7 API | 2 |
| 1 Skeleton/CI | 1.2 | | 8 Resilience etc. | 2 |
| 2 Abstraction | 1 | | 9 Eval | 3 |
| 3 Schema | 1.2 | | 10 Docker | 1 |
| 4 Extraction | 1.5 | | 11 Real run | 1 |
| 5 Summary | 1.2 | | 12 README/publish | 2 |
| 6 Client | 1.5 | | **Total** | **~19** |
