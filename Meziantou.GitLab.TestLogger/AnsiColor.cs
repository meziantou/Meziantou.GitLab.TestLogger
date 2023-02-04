namespace Meziantou.GitLab.TestLogger;

internal sealed class AnsiColor
{
    private readonly string _color;

    private AnsiColor(string value) => _color = value;

    public override string ToString()
    {
        return $"\u001b[{_color}m";
    }

    public static AnsiColor Off { get; } = new("0");
    public static AnsiColor Italic { get; } = new("3");
    public static AnsiColor Gray { get; } = new("1;30");
    public static AnsiColor Red { get; } = new("31");
    public static AnsiColor Green = new("32");
    public static AnsiColor Yellow { get; } = new("33");
    public static AnsiColor Blue { get; } = new("34");
    public static AnsiColor Magenta { get; } = new("35");
    public static AnsiColor Cyan { get; } = new("36");
    public static AnsiColor RedBackground { get; } = new("41");
}
