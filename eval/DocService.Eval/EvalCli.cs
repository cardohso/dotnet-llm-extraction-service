using System.Globalization;
using System.Text.Json;
using DocService.Core;
using DocService.Core.Extraction;
using DocService.Core.Llm;
using DocService.Llm;
using DocService.Llm.Fake;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocService.Eval;

internal static class EvalCli
{
    private const string Usage =
        "Usage: dotnet run --project eval/DocService.Eval -- [--provider groq|fake] [--golden <file>] " +
        "[--thresholds <file>] [--out <dir>] [--delay-ms <n>]";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            var options = CliOptions.Parse(args);
            return await ExecuteAsync(options, cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or OptionsValidationException or IOException or InvalidDataException or JsonException)
        {
            await Console.Error.WriteLineAsync(ex is OptionsValidationException v ? string.Join(Environment.NewLine, v.Failures) : ex.Message);
            return 2;
        }
    }

    private static async Task<int> ExecuteAsync(CliOptions cli, CancellationToken cancellationToken)
    {
        var cases = GoldenLoader.Load(cli.GoldenPath);
        var limits = Thresholds.Load(cli.ThresholdsPath);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Development });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        if (cli.Fake)
        {
            builder.Configuration["Llm:Provider"] = nameof(LlmProvider.Fake);
        }

        builder.Services.AddDocServiceCore(builder.Configuration);
        builder.Services.AddLlmClient(builder.Configuration);
        if (cli.Fake)
        {
            builder.Services.AddSingleton<ILlmClient>(new FakeLlmClient { Responder = GoldenResponder.For(cases) });
        }

        using var host = builder.Build();
        var llm = host.Services.GetRequiredService<IOptions<LlmOptions>>().Value; // throws with a helpful message if the key is missing
        var service = host.Services.GetRequiredService<ExtractionService>();
        var providerLabel = cli.Fake ? "fake" : "groq";
        var model = cli.Fake ? "golden-fake" : llm.Model;

        Console.WriteLine($"Running {cases.Count} cases against {providerLabel} ({model})");
        var results = new List<CaseResult>();
        foreach (var golden in cases)
        {
            if (results.Count > 0 && cli.Delay > TimeSpan.Zero)
            {
                await Task.Delay(cli.Delay, cancellationToken);
            }

            var result = await EvalRunner.RunCaseAsync(service, golden, TimeSpan.FromSeconds(15), cancellationToken);
            results.Add(result);
            Console.WriteLine($"  {result.Id}: valid={result.Valid} attempts={result.Attempts} {result.LatencyMs} ms severity={(result.SeverityOk ? "ok" : "MISS")}");
        }

        var report = new EvalReport(DateTimeOffset.UtcNow, providerLabel, model, results, EvalSummary.From(results));
        var checks = ThresholdCheck.Evaluate(report.Summary, limits);
        WriteOutputs(cli, report, limits, checks);

        foreach (var check in checks)
        {
            Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")}  {check.Name}: {check.Actual} (needs {check.Limit})");
        }

        return ThresholdCheck.AllPassed(checks) ? 0 : 1;
    }

    private static void WriteOutputs(CliOptions cli, EvalReport report, Thresholds limits, IReadOnlyList<ThresholdResult> checks)
    {
        Directory.CreateDirectory(cli.OutDir);
        var stem = Path.Combine(cli.OutDir, $"{report.RunAt:yyyy-MM-dd-HHmm}-{report.Provider}");
        var json = JsonSerializer.Serialize(
            new { report.RunAt, report.Provider, report.Model, report.Summary, Thresholds = limits, Checks = checks, report.Cases },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        File.WriteAllText($"{stem}.json", json);
        File.WriteAllText($"{stem}.md", report.ToMarkdown(limits));
        Console.WriteLine($"Wrote {stem}.json and .md");
    }

    private sealed record CliOptions(bool Fake, string GoldenPath, string ThresholdsPath, string OutDir, TimeSpan Delay)
    {
        public static CliOptions Parse(string[] args)
        {
            var root = RepoRoot.Find();
            var provider = "groq";
            var golden = Path.Combine(root, "eval", "golden", "incident-reports.jsonl");
            var thresholds = Path.Combine(root, "eval", "thresholds.json");
            var outDir = Path.Combine(root, "eval", "results");
            int? delayMs = null;

            for (var i = 0; i < args.Length; i += 2)
            {
                var value = i + 1 < args.Length ? args[i + 1] : throw new ArgumentException($"Missing value for {args[i]}. {Usage}");
                switch (args[i])
                {
                    case "--provider": provider = value; break;
                    case "--golden": golden = Path.GetFullPath(value); break;
                    case "--thresholds": thresholds = Path.GetFullPath(value); break;
                    case "--out": outDir = Path.GetFullPath(value); break;
                    case "--delay-ms": delayMs = int.Parse(value, CultureInfo.InvariantCulture); break;
                    default: throw new ArgumentException($"Unknown option {args[i]}. {Usage}");
                }
            }

            if (provider is not ("groq" or "fake"))
            {
                throw new ArgumentException($"Unknown provider '{provider}'. {Usage}");
            }

            var fake = provider == "fake";
            return new CliOptions(fake, golden, thresholds, outDir, TimeSpan.FromMilliseconds(delayMs ?? (fake ? 0 : 2000)));
        }
    }
}
