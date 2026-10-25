using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace IcarusFleet.Tests;

public enum TestStatus
{
    Passed,
    Failed
}

public record TestCaseResult(
    string TestName,
    string Category,
    string Description,
    TestStatus Status,
    long DurationMs,
    string? ErrorMessage = null
);

public static class TestReporter
{
    private static readonly List<TestCaseResult> Results = new();
    private static readonly object LockObj = new();
    private static readonly Stopwatch SuiteStopwatch = Stopwatch.StartNew();

    private static readonly Dictionary<string, (string Category, string Description)> Metadata = new()
    {
        ["Enqueue_AssignsTagsFrom100WithStep10"] = (
            "Tag Generation",
            "Verifies sequential service tags start at 100 and increment by +10 (100, 110, 120...)."
        ),
        ["Enqueue_RegularJob_KeepsBaseCost"] = (
            "Pricing & Surcharges",
            "Ensures regular priority jobs retain their original cost without additional charges."
        ),
        ["Enqueue_ExpressJob_Applies15PercentSurcharge"] = (
            "Pricing & Surcharges",
            "Validates express priority jobs apply an atomic +15% surcharge at enqueue time."
        ),
        ["Cost_IsRoundedToTwoDecimals"] = (
            "Pricing & Surcharges",
            "Ensures currency costs are rounded to exactly two decimal places."
        ),
        ["Enqueue_SortsJobsIntoTheirPriorityQueue"] = (
            "Queue Separation",
            "Verifies jobs are correctly partitioned into separate Regular and Express FIFO queues."
        ),
        ["Enqueue_MissingRequiredFields_Throws"] = (
            "Input Validation",
            "Verifies ArgumentException is thrown when Client, Model, or Problem are empty or whitespace."
        ),
        ["Enqueue_NegativeCost_Throws"] = (
            "Input Validation",
            "Verifies ArgumentException is thrown when a negative job cost is provided."
        ),
        ["ClientName_IsStoredTitleCased"] = (
            "Text Normalization",
            "Ensures client names are trimmed and normalized to Title Case."
        ),
        ["Problem_IsStoredSentenceCased"] = (
            "Text Normalization",
            "Ensures problem descriptions are trimmed and normalized to Sentence Case."
        ),
        ["Process_MovesJobToFinishedAndStampsIt"] = (
            "Lifecycle & Processing",
            "Ensures processing moves job from queue to Finished list and stamps completion timestamp."
        ),
        ["Process_PreservesFifoOrder"] = (
            "Queue Ordering",
            "Ensures jobs are processed in strict First-In, First-Out (FIFO) arrival order."
        ),
        ["Process_UnknownTag_ReturnsNull"] = (
            "Error Handling",
            "Ensures attempting to process a non-existent tag returns null without corrupting state."
        ),
        ["RemoveFinished_DeletesOnlyOnce"] = (
            "State Consistency",
            "Ensures finished jobs can be removed only once, returning false on subsequent attempts."
        ),
        ["GetHealth_ReturnsOkAndStatusHealthy"] = (
            "API Health Check",
            "Validates GET /health returns HTTP 200 with status Healthy."
        ),
        ["GetRoot_ServesDashboardHtml"] = (
            "API Web Dashboard",
            "Validates GET / serves the HTML web dashboard with HTTP 200."
        ),
        ["GetServices_ReturnsSnapshot"] = (
            "API Queue Snapshot",
            "Validates GET /api/services returns current Regular, Express, and Finished queues."
        ),
        ["PostService_EnqueuesRegularJob"] = (
            "API Job Creation",
            "Validates POST /api/services creates a regular priority job and returns HTTP 201 Created."
        ),
        ["PostService_EnqueuesExpressJobWithSurcharge"] = (
            "API Job Creation",
            "Validates POST /api/services creates an express priority job with 15% atomic surcharge."
        ),
        ["ProcessJob_MovesJobToFinished"] = (
            "API Job Processing",
            "Validates POST /api/services/{tag}/process completes a queued job and moves it to Finished."
        ),
        ["DeleteFinishedJob_ReturnsNoContent"] = (
            "API Job Lifecycle",
            "Validates DELETE /api/services/finished/{tag} removes a finished job returning HTTP 204."
        ),
        ["GetSwagger_ServesInteractiveDocumentation"] = (
            "API Documentation",
            "Validates GET /swagger serves interactive Swagger UI documentation."
        )
    };

    public static async Task RecordAsync(Func<Task> testAction, [CallerMemberName] string testName = "")
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await testAction();
            sw.Stop();

            var (category, description) = GetMetadata(testName);
            lock (LockObj)
            {
                Results.Add(new TestCaseResult(testName, category, description, TestStatus.Passed, sw.ElapsedMilliseconds));
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            var (category, description) = GetMetadata(testName);
            lock (LockObj)
            {
                Results.Add(new TestCaseResult(testName, category, description, TestStatus.Failed, sw.ElapsedMilliseconds, ex.Message));
            }
            throw;
        }
    }

    public static void Record(Action testAction, [CallerMemberName] string testName = "")
    {
        var sw = Stopwatch.StartNew();
        try
        {
            testAction();
            sw.Stop();

            var (category, description) = GetMetadata(testName);
            lock (LockObj)
            {
                Results.Add(new TestCaseResult(testName, category, description, TestStatus.Passed, sw.ElapsedMilliseconds));
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            var (category, description) = GetMetadata(testName);
            lock (LockObj)
            {
                Results.Add(new TestCaseResult(testName, category, description, TestStatus.Failed, sw.ElapsedMilliseconds, ex.Message));
            }
            throw;
        }
    }

    private static (string Category, string Description) GetMetadata(string testName)
    {
        if (Metadata.TryGetValue(testName, out var meta))
        {
            return meta;
        }

        var parts = testName.Split('_');
        var category = parts.Length > 0 ? parts[0] : "General";
        var description = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : testName;
        return (category, description);
    }

    public static void GenerateReport()
    {
        List<TestCaseResult> snapshot;
        lock (LockObj)
        {
            snapshot = Results.ToList();
        }

        if (snapshot.Count == 0)
        {
            return;
        }

        SuiteStopwatch.Stop();
        var reportPath = ResolveReportPath();
        var markdown = BuildMarkdown(snapshot, SuiteStopwatch.ElapsedMilliseconds);

        File.WriteAllText(reportPath, markdown, Encoding.UTF8);
        Console.WriteLine($"\n[TestReporter] Test report generated: {reportPath}");
    }

    private static string ResolveReportPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "IcarusFleet.sln")) ||
                Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return Path.Combine(dir.FullName, "TEST_REPORT.md");
            }
            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "TEST_REPORT.md");
    }

    private static string BuildMarkdown(List<TestCaseResult> results, long totalDurationMs)
    {
        var total = results.Count;
        var passed = results.Count(r => r.Status == TestStatus.Passed);
        var failed = results.Count(r => r.Status == TestStatus.Failed);
        var passRate = total > 0 ? (passed * 100.0 / total) : 0.0;
        var nowUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");

        var sb = new StringBuilder();
        sb.AppendLine("# 🧪 Icarus Fleet - Test Execution Report");
        sb.AppendLine();
        sb.AppendLine($"> **Generated:** {nowUtc}  ");
        sb.AppendLine($"> **Environment:** .NET {Environment.Version} | {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})  ");
        sb.AppendLine($"> **Test Framework:** xUnit | Target: `IcarusFleet.Api.Services.ServiceQueueManager`  ");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 📊 Executive Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| :--- | :--- |");
        sb.AppendLine($"| **Total Tests Evaluated** | `{total}` |");
        sb.AppendLine($"| **Passed** | `{passed}` ✅ |");
        sb.AppendLine($"| **Failed** | `{failed}` {(failed > 0 ? "❌" : "")} |");
        sb.AppendLine($"| **Success Rate** | **{passRate:F1}%** |");
        sb.AppendLine($"| **Total Suite Duration** | `{totalDurationMs} ms` |");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 📋 Test Suite Breakdown");
        sb.AppendLine();
        sb.AppendLine("| Category | Test Method | Status | Duration | Evaluated Business Rule |");
        sb.AppendLine("| :--- | :--- | :---: | :---: | :--- |");

        foreach (var r in results)
        {
            var statusBadge = r.Status == TestStatus.Passed ? "✅ PASS" : "❌ FAIL";
            sb.AppendLine($"| **{r.Category}** | `{r.TestName}` | {statusBadge} | {r.DurationMs} ms | {r.Description} |");
        }

        if (failed > 0)
        {
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## ❌ Failures & Diagnostic Details");
            sb.AppendLine();
            foreach (var r in results.Where(r => r.Status == TestStatus.Failed))
            {
                sb.AppendLine($"### `{r.TestName}`");
                sb.AppendLine($"- **Category:** {r.Category}");
                sb.AppendLine($"- **Duration:** {r.DurationMs} ms");
                sb.AppendLine($"- **Error:**");
                sb.AppendLine("```text");
                sb.AppendLine(r.ErrorMessage ?? "Unknown assertion error");
                sb.AppendLine("```");
            }
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 🛡️ Business Invariants Validated");
        sb.AppendLine();
        sb.AppendLine("1. **Sequential Tag Numbering:** Enqueued service items are guaranteed unique tags incrementing in steps of 10 from 100.");
        sb.AppendLine("2. **Express Job Surcharge:** Exactly 15% surcharge is atomically compounded at insertion, preventing post-quote price drift.");
        sb.AppendLine("3. **Input Sanitization & Invariants:** Client names strictly title-cased, issues sentence-cased, and blank inputs rejected.");
        sb.AppendLine("4. **Queue Integrity (FIFO):** Regular and Express streams operate independently while preserving arrival chronology.");
        sb.AppendLine("5. **Safe Processing & Deletion:** Tag transitions to Finished with ISO timestamps; deletion is strictly idempotent.");
        sb.AppendLine();
        sb.AppendLine("_Report automatically generated by `IcarusFleet.Tests.TestReporter`._");

        return sb.ToString();
    }
}

public class TestReporterFixture : IDisposable
{
    public void Dispose()
    {
        TestReporter.GenerateReport();
    }
}
