namespace Meziantou.GitLab.TestLogger;
internal static class CommandLineResources
{
    public const string FailedTestIndicator = "Failed";
    public const string PassedTestIndicator = "Passed";
    public const string SkippedTestIndicator = "Skipped";
    public const string None = "None";

    public const string ExecutionTimeFormatString = "Total time: {0:0.0000} {1}";
    public const string Days = "Days";
    public const string Hours = "Hours";
    public const string Minutes = "Minutes";
    public const string Seconds = "Seconds";

    public const string ErrorMessageBanner = "Error Message:";
    public const string StackTraceBanner = "Stack Trace:";
    public const string StdOutMessagesBanner = "Standard Output Messages:";
    public const string StdErrMessagesBanner = "Standard Error Messages:";
    public const string DbgTrcMessagesBanner = "Debug Traces Messages:";
    public const string AdditionalInfoMessagesBanner = "Additional Information Messages:";
    public const string TestSourcesDiscovered = "A total of {0} test files matched the specified pattern.";

    public const string AttachmentOutputFormat = "  {0}";
    public const string AttachmentsBanner = "Attachments:";

    public const string TestRunSummary = "{0} - Failed: {1}, Passed: {2}, Skipped: {3}, Total: {4}, Duration: {5}";
    public const string TestRunSummaryAssemblyAndFramework = " - {0} {1}";
    public const string TestRunCanceled = "Test Run Canceled.";
    public const string TestRunAborted = "Test Run Aborted.";
    public const string TestRunAbortedWithError = "Test Run Aborted with error {0}.";
    public const string TestRunFailed = "Test Run Failed.";
    public const string TestRunSuccessful = "Test Run Successful.";
    public const string TestRunSummaryForCanceledOrAbortedRun = "Total tests: Unknown";
    public const string TestRunSummaryPassedTests = "     Passed: {0}";
    public const string TestRunSummaryFailedTests = "     Failed: {0}";
    public const string TestRunSummarySkippedTests = "     Skipped: {0}";
    public const string TestRunSummaryTotalTests = "Total tests: {0}";
}
