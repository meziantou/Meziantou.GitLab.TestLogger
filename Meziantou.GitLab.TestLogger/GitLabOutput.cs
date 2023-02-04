using System.Globalization;
using Microsoft.VisualStudio.TestPlatform.Utilities;

namespace Meziantou.GitLab.TestLogger;

internal static class GitLabOutput
{
    private static readonly bool NoColor = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
    private static int s_sectionCount;

    private static AnsiColor GetColor(OutputLevel level)
    {
        return level switch
        {
            OutputLevel.Warning => AnsiColor.Yellow,
            OutputLevel.Error => AnsiColor.RedBackground,
            _ => AnsiColor.Off,
        };
    }

    private static string Format(OutputLevel level, string message)
    {
        return Format(GetColor(level), message);
    }

    private static string Format(AnsiColor color, string message)
    {
        message = message.Replace("\r\n", "\n");
        if (NoColor)
            color = AnsiColor.Off;

        return $"{color}{message}{AnsiColor.Off}";
    }

    public static void Write(string message, OutputLevel outputLevel)
    {
        Console.Write(Format(outputLevel, message));
    }

    public static void Write(string message, AnsiColor color)
    {
        Console.Write(Format(color, message));
    }

    public static void WriteLine(string message, OutputLevel outputLevel)
    {
        Console.WriteLine(Format(outputLevel, message));
    }

    public static void WriteLine(string message, AnsiColor color)
    {
        Console.WriteLine(Format(color, message));
    }

    // https://docs.gitlab.com/ee/ci/jobs/#custom-collapsible-sections
    public static IDisposable BeginCollapsibleSection(string displayName, AnsiColor? color = null, bool collapsed = false)
    {
        var name = "s" + Interlocked.Increment(ref s_sectionCount).ToString(CultureInfo.InvariantCulture);

        var time = (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;
        var collapsedText = collapsed ? "[collapsed=true]" : "";
        Console.WriteLine($"\u001b[0Ksection_start:{time.ToString(CultureInfo.InvariantCulture)}:{name}{collapsedText}\r\u001b[0K{color}{displayName}");
        return new Section(name);
    }

    private sealed class Section : IDisposable
    {
        private readonly string _name;

        public Section(string name) => _name = name;

        public void Dispose()
        {
            var time = (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;
            Console.WriteLine($"\u001b[0Ksection_end:{time.ToString(CultureInfo.InvariantCulture)}:{_name}\r\u001b[0K");
        }
    }
}
