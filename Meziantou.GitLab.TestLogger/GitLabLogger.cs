using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Microsoft.VisualStudio.TestPlatform.Utilities;

using NuGet.Frameworks;

namespace Meziantou.GitLab.TestLogger;

[FriendlyName(FriendlyName)]
[ExtensionUri(ExtensionUri)]
public sealed class GitLabLogger : ITestLoggerWithParameters
{
    private const string TestMessageFormattingPrefix = " ";
    private const string TestResultPrefix = "  ";
    private const string TestResultSuffix = " ";

    public const string ExtensionUri = "logger://Meziantou/GitLabLogger/v1";
    public const string FriendlyName = "gitlab";

    public const string VerbosityParam = "verbosity";
    public const string CollapseStackTracesParam = "collapseStackTraces";
    public const string CollapseErrorMessagesParam = "collapseErrorMessages";
    public const string CollapseStandardOutputParam = "collapseStandardOutput";
    public const string CollapseStandardErrorParam = "collapseStandardError";
    public const string FailedTestSeparatorParam = "failedTestSeparator";
    public const string ParentExecutionIdPropertyIdentifier = "ParentExecId";
    public const string ExecutionIdPropertyIdentifier = "ExecutionId";

    // Figure out the longest result string (+1 for ! where applicable), so we don't get misaligned output on non-English systems
    private static readonly int LongestResultIndicator = new[]
    {
        CommandLineResources.FailedTestIndicator.Length + 1,
        CommandLineResources.PassedTestIndicator.Length + 1,
        CommandLineResources.SkippedTestIndicator.Length + 1,
        CommandLineResources.None.Length,
    }.Max();

    private static readonly object OutputLock = new();

    public enum Verbosity
    {
        Quiet,
        Minimal,
        Normal,
        Detailed,
    }

    private bool _testRunHasErrorMessages;
    private string? _targetFramework;

    public Verbosity VerbosityLevel { get; private set; } = Verbosity.Minimal;
    public bool CollapseStackTraces { get; private set; }
    public bool CollapseErrorMessages { get; private set; }
    public bool CollapseStandardOutput { get; private set; }
    public bool CollapseStandardError { get; private set; }
    public string FailedTestSeparator { get; private set; } = "\n \n ";

    /// <summary>
    /// Tracks leaf test outcomes per source. This is needed to correctly count hierarchical tests as well as
    /// tracking counts per source for the minimal and quiet output.
    /// </summary>
    private ConcurrentDictionary<Guid, MinimalTestResult>? LeafTestResults { get; set; }

    [MemberNotNull(nameof(LeafTestResults))]
    public void Initialize(TestLoggerEvents events, string testRunDirectory)
    {
        ValidateArg.NotNull(events, nameof(events));

        // Register for the events.
        events.TestRunMessage += TestMessageHandler;
        events.TestResult += TestResultHandler;
        events.TestRunComplete += TestRunCompleteHandler;

        // Register for the discovery events.
        events.DiscoveryMessage += TestMessageHandler;
        LeafTestResults = new ConcurrentDictionary<Guid, MinimalTestResult>();
    }

    public void Initialize(TestLoggerEvents events, Dictionary<string, string?> parameters)
    {
        ValidateArg.NotNull(parameters, nameof(parameters));

        if (parameters.Count == 0)
            throw new ArgumentException("No default parameters added", nameof(parameters));

        if (parameters.TryGetValue(VerbosityParam, out var verbosity) && Enum.TryParse(verbosity, ignoreCase: true, out Verbosity verbosityLevel))
            VerbosityLevel = verbosityLevel;

        if (parameters.TryGetValue(CollapseErrorMessagesParam, out var collapseErrorMessagesValue) && bool.TryParse(collapseErrorMessagesValue, out var collapseErrorMessages))
            CollapseErrorMessages = collapseErrorMessages;

        if (parameters.TryGetValue(CollapseStackTracesParam, out var collapseStackTracesValue) && bool.TryParse(collapseStackTracesValue, out var collapseStackTraces))
            CollapseStackTraces = collapseStackTraces;

        if (parameters.TryGetValue(CollapseStandardOutputParam, out var collapseStandardOutputValue) && bool.TryParse(collapseStandardOutputValue, out var collapseStandardOutput))
            CollapseStandardOutput = collapseStandardOutput;

        if (parameters.TryGetValue(CollapseStandardErrorParam, out var collapseStandardErrorValue) && bool.TryParse(collapseStandardErrorValue, out var collapseStandardError))
            CollapseStandardError = collapseStandardError;

        if (parameters.TryGetValue(FailedTestSeparatorParam, out var failedTestSeparator))
            FailedTestSeparator = failedTestSeparator;

        parameters.TryGetValue(DefaultLoggerParameterNames.TargetFramework, out _targetFramework);
        _targetFramework = !string.IsNullOrWhiteSpace(_targetFramework) ? NuGetFramework.Parse(_targetFramework).GetShortFolderName() : _targetFramework;

        Initialize(events, string.Empty);
    }

    private static void PrintTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalDays >= 1)
        {
            GitLabOutput.WriteLine(string.Format(CultureInfo.CurrentCulture, CommandLineResources.ExecutionTimeFormatString, timeSpan.TotalDays, CommandLineResources.Days), OutputLevel.Information);
        }
        else if (timeSpan.TotalHours >= 1)
        {
            GitLabOutput.WriteLine(string.Format(CultureInfo.CurrentCulture, CommandLineResources.ExecutionTimeFormatString, timeSpan.TotalHours, CommandLineResources.Hours), OutputLevel.Information);
        }
        else if (timeSpan.TotalMinutes >= 1)
        {
            GitLabOutput.WriteLine(string.Format(CultureInfo.CurrentCulture, CommandLineResources.ExecutionTimeFormatString, timeSpan.TotalMinutes, CommandLineResources.Minutes), OutputLevel.Information);
        }
        else
        {
            GitLabOutput.WriteLine(string.Format(CultureInfo.CurrentCulture, CommandLineResources.ExecutionTimeFormatString, timeSpan.TotalSeconds, CommandLineResources.Seconds), OutputLevel.Information);
        }
    }

    /// <summary>
    /// Constructs a well formatted string using the given prefix before every message content on each line.
    /// </summary>
    private static string GetFormattedOutput(Collection<TestResultMessage> testMessageCollection)
    {
        if (testMessageCollection == null)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var message in testMessageCollection)
        {
            var prefix = string.Format(CultureInfo.CurrentCulture, "{0}{1}", Environment.NewLine, TestMessageFormattingPrefix);
            var messageText = message.Text?.Replace(Environment.NewLine, prefix).TrimEnd(TestMessageFormattingPrefix.ToCharArray());

            if (!string.IsNullOrWhiteSpace(messageText))
                sb.AppendFormat(CultureInfo.CurrentCulture, "{0}{1}", TestMessageFormattingPrefix, messageText);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Collects all the messages of a particular category(Standard Output/Standard Error/Debug Traces) and returns a collection.
    /// </summary>
    private static Collection<TestResultMessage> GetTestMessages(Collection<TestResultMessage> messages, string requiredCategory)
    {
        var selectedMessages = messages.Where(msg => msg.Category.Equals(requiredCategory, StringComparison.OrdinalIgnoreCase));
        var requiredMessageCollection = new Collection<TestResultMessage>(selectedMessages.ToList());
        return requiredMessageCollection;
    }

    private static void DisplayFullInformation(TestResult result)
    {
        // Add newline if it is not in given output data.
        var addAdditionalNewLine = false;

        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            addAdditionalNewLine = true;
            using (GitLabOutput.BeginCollapsibleSection(TestResultPrefix + CommandLineResources.ErrorMessageBanner, AnsiColor.Red))
            {
                var errorMessage = string.Format(CultureInfo.CurrentCulture, "{0}{1}{2}", TestResultPrefix, TestMessageFormattingPrefix, result.ErrorMessage);
                GitLabOutput.WriteLine(errorMessage, AnsiColor.Red);
            }
        }

        if (!string.IsNullOrEmpty(result.ErrorStackTrace))
        {
            addAdditionalNewLine = false;
            using (GitLabOutput.BeginCollapsibleSection(TestResultPrefix + CommandLineResources.StackTraceBanner, AnsiColor.Red))
            {
                var stackTrace = string.Format(CultureInfo.CurrentCulture, "{0}{1}", TestResultPrefix, result.ErrorStackTrace);
                GitLabOutput.WriteLine(stackTrace, AnsiColor.Red);
            }
        }

        var stdOutMessagesCollection = GetTestMessages(result.Messages, TestResultMessage.StandardOutCategory);
        if (stdOutMessagesCollection.Count > 0)
        {
            addAdditionalNewLine = true;
            var stdOutMessages = GetFormattedOutput(stdOutMessagesCollection);

            if (!string.IsNullOrWhiteSpace(stdOutMessages))
            {
                using (GitLabOutput.BeginCollapsibleSection(TestResultPrefix + CommandLineResources.StdOutMessagesBanner, collapsed: true))
                {
                    GitLabOutput.WriteLine(stdOutMessages, OutputLevel.Information);
                }
            }
        }

        var stdErrMessagesCollection = GetTestMessages(result.Messages, TestResultMessage.StandardErrorCategory);
        if (stdErrMessagesCollection.Count > 0)
        {
            addAdditionalNewLine = false;
            var stdErrMessages = GetFormattedOutput(stdErrMessagesCollection);

            if (!string.IsNullOrEmpty(stdErrMessages))
            {
                using (GitLabOutput.BeginCollapsibleSection(TestResultPrefix + CommandLineResources.StdErrMessagesBanner, AnsiColor.Red, collapsed: true))
                {
                    GitLabOutput.WriteLine(stdErrMessages, AnsiColor.Red);
                }
            }
        }

        var dbgTrcMessagesCollection = GetTestMessages(result.Messages, TestResultMessage.DebugTraceCategory);
        if (dbgTrcMessagesCollection.Count > 0)
        {
            addAdditionalNewLine = false;
            var dbgTrcMessages = GetFormattedOutput(dbgTrcMessagesCollection);

            if (!string.IsNullOrEmpty(dbgTrcMessages))
            {
                using (GitLabOutput.BeginCollapsibleSection(TestResultPrefix + CommandLineResources.DbgTrcMessagesBanner))
                {
                    GitLabOutput.WriteLine(dbgTrcMessages, OutputLevel.Information);
                }
            }
        }

        var additionalInfoMessagesCollection = GetTestMessages(result.Messages, TestResultMessage.AdditionalInfoCategory);
        if (additionalInfoMessagesCollection.Count > 0)
        {
            addAdditionalNewLine = false;
            var additionalInfoMessages = GetFormattedOutput(additionalInfoMessagesCollection);

            if (!string.IsNullOrEmpty(additionalInfoMessages))
            {
                using (GitLabOutput.BeginCollapsibleSection(TestResultPrefix + CommandLineResources.AdditionalInfoMessagesBanner))
                {
                    GitLabOutput.WriteLine(additionalInfoMessages, OutputLevel.Information);
                }
            }
        }

        if (addAdditionalNewLine)
            GitLabOutput.WriteLine(string.Empty, OutputLevel.Information);
    }

    private static Guid GetParentExecutionId(TestResult testResult)
    {
        var parentExecutionIdProperty = testResult.Properties.FirstOrDefault(property => property.Id == ParentExecutionIdPropertyIdentifier);
        return parentExecutionIdProperty == null ? Guid.Empty : testResult.GetPropertyValue(parentExecutionIdProperty, Guid.Empty);
    }

    private static Guid GetExecutionId(TestResult testResult)
    {
        var executionIdProperty = testResult.Properties.FirstOrDefault(property => property.Id == ExecutionIdPropertyIdentifier);
        var executionId = Guid.Empty;

        if (executionIdProperty != null)
            executionId = testResult.GetPropertyValue(executionIdProperty, Guid.Empty);

        return executionId.Equals(Guid.Empty) ? Guid.NewGuid() : executionId;
    }

    private void TestMessageHandler(object? sender, TestRunMessageEventArgs e)
    {
        ValidateArg.NotNull(sender, nameof(sender));
        ValidateArg.NotNull(e, nameof(e));

        lock (OutputLock)
        {
            switch (e.Level)
            {
                case TestMessageLevel.Informational:
                    {
                        if (VerbosityLevel is Verbosity.Quiet or Verbosity.Minimal)
                            break;

                        GitLabOutput.WriteLine(e.Message, OutputLevel.Information);
                        break;
                    }

                case TestMessageLevel.Warning:
                    {
                        if (VerbosityLevel == Verbosity.Quiet)
                            break;

                        GitLabOutput.WriteLine(e.Message, OutputLevel.Warning);
                        break;
                    }

                case TestMessageLevel.Error:
                    {
                        _testRunHasErrorMessages = true;
                        GitLabOutput.WriteLine(e.Message, OutputLevel.Error);
                        break;
                    }
                default:
                    EqtTrace.Warning("ConsoleLogger.TestMessageHandler: The test message level is unrecognized: {0}", e.Level.ToString());
                    break;
            }
        }
    }

    private void TestResultHandler(object? sender, TestResultEventArgs e)
    {
        ValidateArg.NotNull(sender, nameof(sender));
        ValidateArg.NotNull(e, nameof(e));
        Debug.Assert(LeafTestResults != null, "Initialize should have been called");

        lock (OutputLock)
        {
            var testDisplayName = e.Result.DisplayName;

            if (string.IsNullOrWhiteSpace(e.Result.DisplayName))
                testDisplayName = e.Result.TestCase.DisplayName;

            var formattedDuration = GetFormattedDurationString(e.Result.Duration);
            if (!string.IsNullOrEmpty(formattedDuration))
                testDisplayName = $"{testDisplayName} [{formattedDuration}]";

            var executionId = GetExecutionId(e.Result);
            var parentExecutionId = GetParentExecutionId(e.Result);

            if (parentExecutionId != Guid.Empty)
                // Not checking the result value.
                // This would return false if the id did not exist,
                // or true if it did exist. In either case the id is not in the dictionary
                // which is our goal.
                LeafTestResults.TryRemove(parentExecutionId, out _);

            if (!LeafTestResults.TryAdd(executionId, new MinimalTestResult(e.Result)))
                // This would happen if the key already exists. This should not happen, because we are
                // inserting by GUID key, so this would mean an error in our code.
                throw new InvalidOperationException($"ExecutionId {executionId} already exists.");

            switch (e.Result.Outcome)
            {
                case TestOutcome.Skipped:
                    if (VerbosityLevel == Verbosity.Quiet)
                        break;

                    GitLabOutput.Write(GetFormattedTestIndicator(CommandLineResources.SkippedTestIndicator), AnsiColor.Yellow);
                    GitLabOutput.WriteLine(testDisplayName, OutputLevel.Information);
                    if (VerbosityLevel == Verbosity.Detailed)
                        DisplayFullInformation(e.Result);

                    break;

                case TestOutcome.Failed:
                    if (VerbosityLevel == Verbosity.Quiet)
                        break;

                    GitLabOutput.Write(GetFormattedTestIndicator(CommandLineResources.FailedTestIndicator), AnsiColor.Red);
                    GitLabOutput.WriteLine(testDisplayName, OutputLevel.Information);
                    DisplayFullInformation(e.Result);
                    if (!string.IsNullOrEmpty(FailedTestSeparator))
                        GitLabOutput.WriteLine(FailedTestSeparator, AnsiColor.Off);

                    break;

                case TestOutcome.Passed:
                    if (VerbosityLevel is Verbosity.Normal or Verbosity.Detailed)
                    {
                        GitLabOutput.Write(GetFormattedTestIndicator(CommandLineResources.PassedTestIndicator), AnsiColor.Green);
                        GitLabOutput.WriteLine(testDisplayName, OutputLevel.Information);
                        if (VerbosityLevel == Verbosity.Detailed)
                            DisplayFullInformation(e.Result);
                    }

                    break;

                default:
                    if (VerbosityLevel == Verbosity.Quiet)
                        break;

                    GitLabOutput.Write(GetFormattedTestIndicator(CommandLineResources.SkippedTestIndicator), AnsiColor.Yellow);
                    GitLabOutput.WriteLine(testDisplayName, OutputLevel.Information);
                    if (VerbosityLevel == Verbosity.Detailed)
                        DisplayFullInformation(e.Result);

                    break;
            }
        }

        // Local functions
        static string GetFormattedTestIndicator(string indicator) => TestResultPrefix + indicator + TestResultSuffix;
    }

    private static string? GetFormattedDurationString(TimeSpan duration)
    {
        if (duration == default)
            return null;

        var time = new List<string>();
        if (duration.Hours > 0)
            time.Add(duration.Hours.ToString(CultureInfo.CurrentCulture) + " h");

        if (duration.Minutes > 0)
            time.Add(duration.Minutes.ToString(CultureInfo.CurrentCulture) + " m");

        if (duration.Hours == 0)
        {
            if (duration.Seconds > 0)
                time.Add(duration.Seconds.ToString(CultureInfo.CurrentCulture) + " s");

            if (duration.Milliseconds > 0 && duration.Minutes == 0 && duration.Seconds == 0)
                time.Add(duration.Milliseconds.ToString(CultureInfo.CurrentCulture) + " ms");
        }

        return time.Count == 0 ? "< 1 ms" : string.Join(" ", time);
    }

    private void TestRunCompleteHandler(object? sender, TestRunCompleteEventArgs e)
    {
        lock (OutputLock)
        {
            var passedTests = 0;
            var failedTests = 0;
            var skippedTests = 0;
            var totalTests = 0;
            GitLabOutput.WriteLine("", OutputLevel.Information);

            // Printing Run-level Attachments
            var runLevelAttachmentsCount = e.AttachmentSets == null ? 0 : e.AttachmentSets.Sum(attachmentSet => attachmentSet.Attachments.Count);
            if (runLevelAttachmentsCount > 0)
            {
                GitLabOutput.WriteLine(CommandLineResources.AttachmentsBanner, OutputLevel.Information);
                Debug.Assert(e.AttachmentSets != null, "e.AttachmentSets should not be null when runLevelAttachmentsCount > 0.");
                foreach (var attachmentSet in e.AttachmentSets)
                {
                    foreach (var uriDataAttachment in attachmentSet.Attachments)
                    {
                        var attachmentOutput = string.Format(CultureInfo.CurrentCulture, CommandLineResources.AttachmentOutputFormat, uriDataAttachment.Uri.LocalPath);
                        GitLabOutput.WriteLine(attachmentOutput, OutputLevel.Information);
                    }
                }
            }

            var leafTestResultsPerSource = LeafTestResults?.Select(p => p.Value)?.GroupBy(r => r.TestCase.Source, StringComparer.Ordinal);
            if (leafTestResultsPerSource is not null)
            {
                foreach (var sd in leafTestResultsPerSource)
                {
                    var source = sd.Key;
                    var sourceSummary = new SourceSummary();

                    var results = sd.ToArray();
                    // duration of the whole source is the difference between the test that ended last and the one that started first
                    sourceSummary.Duration = !results.Any() ? TimeSpan.Zero : results.Max(r => r.EndTime) - results.Min(r => r.StartTime);
                    foreach (var result in results)
                    {
                        switch (result.Outcome)
                        {
                            case TestOutcome.Passed:
                                sourceSummary.TotalTests++;
                                sourceSummary.PassedTests++;
                                break;
                            case TestOutcome.Failed:
                                sourceSummary.TotalTests++;
                                sourceSummary.FailedTests++;
                                break;
                            case TestOutcome.Skipped:
                                sourceSummary.TotalTests++;
                                sourceSummary.SkippedTests++;
                                break;
                            default:
                                break;
                        }
                    }

                    if (VerbosityLevel is Verbosity.Quiet or Verbosity.Minimal)
                    {
                        var sourceOutcome = TestOutcome.None;
                        if (sourceSummary.FailedTests > 0)
                        {
                            sourceOutcome = TestOutcome.Failed;
                        }
                        else if (sourceSummary.PassedTests > 0)
                        {
                            sourceOutcome = TestOutcome.Passed;
                        }
                        else if (sourceSummary.SkippedTests > 0)
                        {
                            sourceOutcome = TestOutcome.Skipped;
                        }

                        var resultString = sourceOutcome switch
                        {
                            TestOutcome.Failed => (CommandLineResources.FailedTestIndicator + "!").PadRight(LongestResultIndicator),
                            TestOutcome.Passed => (CommandLineResources.PassedTestIndicator + "!").PadRight(LongestResultIndicator),
                            TestOutcome.Skipped => (CommandLineResources.SkippedTestIndicator + "!").PadRight(LongestResultIndicator),
                            _ => CommandLineResources.None.PadRight(LongestResultIndicator),
                        };
                        var failed = sourceSummary.FailedTests.ToString(CultureInfo.CurrentCulture).PadLeft(5);
                        var passed = sourceSummary.PassedTests.ToString(CultureInfo.CurrentCulture).PadLeft(5);
                        var skipped = sourceSummary.SkippedTests.ToString(CultureInfo.CurrentCulture).PadLeft(5);
                        var total = sourceSummary.TotalTests.ToString(CultureInfo.CurrentCulture).PadLeft(5);


                        var frameworkString = string.IsNullOrEmpty(_targetFramework)
                            ? string.Empty
                            : $"({_targetFramework})";

                        var duration = GetFormattedDurationString(sourceSummary.Duration);
                        var sourceName = Path.GetFileName(sd.Key);

                        var outputLine = string.Format(CultureInfo.CurrentCulture, CommandLineResources.TestRunSummary,
                            resultString,
                            failed,
                            passed,
                            skipped,
                            total,
                            duration);

                        var color = AnsiColor.Off;
                        if (sourceOutcome == TestOutcome.Failed)
                        {
                            color = AnsiColor.Red;
                        }
                        else if (sourceOutcome == TestOutcome.Passed)
                        {
                            color = AnsiColor.Green;
                        }
                        else if (sourceOutcome == TestOutcome.Skipped)
                        {
                            color = AnsiColor.Yellow;
                        }

                        if (color != null)
                        {
                            GitLabOutput.Write(outputLine, color);
                        }
                        else
                        {
                            GitLabOutput.Write(outputLine, OutputLevel.Information);
                        }

                        GitLabOutput.WriteLine(string.Format(CultureInfo.CurrentCulture, CommandLineResources.TestRunSummaryAssemblyAndFramework, sourceName, frameworkString), OutputLevel.Information);
                    }

                    passedTests += sourceSummary.PassedTests;
                    failedTests += sourceSummary.FailedTests;
                    skippedTests += sourceSummary.SkippedTests;
                    totalTests += sourceSummary.TotalTests;
                }
            }

            if (VerbosityLevel is Verbosity.Quiet or Verbosity.Minimal)
            {
                if (e.IsCanceled)
                {
                    GitLabOutput.WriteLine(CommandLineResources.TestRunCanceled, OutputLevel.Error);
                }
                else if (e.IsAborted)
                {
                    if (e.Error == null)
                    {
                        GitLabOutput.WriteLine(CommandLineResources.TestRunAborted, OutputLevel.Error);
                    }
                    else
                    {
                        GitLabOutput.WriteLine(string.Format(CultureInfo.CurrentCulture, CommandLineResources.TestRunAbortedWithError, e.Error), OutputLevel.Error);
                    }
                }

                return;
            }

            if (e.IsCanceled)
            {
                GitLabOutput.WriteLine(CommandLineResources.TestRunCanceled, OutputLevel.Error);
            }
            else if (e.IsAborted)
            {
                if (e.Error == null)
                {
                    GitLabOutput.WriteLine(CommandLineResources.TestRunAborted, OutputLevel.Error);
                }
                else
                {
                    GitLabOutput.WriteLine(string.Format(CultureInfo.CurrentCulture, CommandLineResources.TestRunAbortedWithError, e.Error), OutputLevel.Error);
                }
            }
            else if (failedTests > 0 || _testRunHasErrorMessages)
            {
                GitLabOutput.WriteLine(CommandLineResources.TestRunFailed, OutputLevel.Error);
            }
            else if (totalTests > 0)
            {
                GitLabOutput.WriteLine(CommandLineResources.TestRunSuccessful, AnsiColor.Green);
            }

            // Output a summary.
            if (totalTests > 0)
            {
                var totalTestsFormat = e.IsAborted || e.IsCanceled ? CommandLineResources.TestRunSummaryForCanceledOrAbortedRun : CommandLineResources.TestRunSummaryTotalTests;
                GitLabOutput.WriteLine(string.Format(CultureInfo.CurrentCulture, totalTestsFormat, totalTests), OutputLevel.Information);

                if (passedTests > 0)
                    GitLabOutput.WriteLine(string.Format(CultureInfo.CurrentCulture, CommandLineResources.TestRunSummaryPassedTests, passedTests), AnsiColor.Green);
                if (failedTests > 0)
                    GitLabOutput.WriteLine(string.Format(CultureInfo.CurrentCulture, CommandLineResources.TestRunSummaryFailedTests, failedTests), AnsiColor.Red);
                if (skippedTests > 0)
                    GitLabOutput.WriteLine(string.Format(CultureInfo.CurrentCulture, CommandLineResources.TestRunSummarySkippedTests, skippedTests), AnsiColor.Yellow);
            }

            if (totalTests > 0)
            {
                if (e.ElapsedTimeInRunningTests.Equals(TimeSpan.Zero))
                {
                    EqtTrace.Info("Skipped printing test execution time on console because it looks like the test run had faced some errors");
                }
                else
                {
                    PrintTimeSpan(e.ElapsedTimeInRunningTests);
                }
            }
        }
    }

    private sealed class MinimalTestResult
    {
        public MinimalTestResult(TestResult testResult)
        {
            TestCase = testResult.TestCase;
            Outcome = testResult.Outcome;
            StartTime = testResult.StartTime;
            EndTime = testResult.EndTime;
        }

        public TestCase TestCase { get; }
        public TestOutcome Outcome { get; }
        public DateTimeOffset StartTime { get; }
        public DateTimeOffset EndTime { get; }
    }
}