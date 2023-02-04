using Xunit.Abstractions;

namespace Tests;

public class UnitTest1
{
    private readonly ITestOutputHelper _outputHelper;

    public UnitTest1(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
    }

    [Fact]
    public void Success1()
    {

    }

    [Fact]
    public void Fail1()
    {
        Assert.Fail("Custom Error message");
    }

    [Fact]
    public void Fail2()
    {
        _outputHelper.WriteLine("Line1");
        _outputHelper.WriteLine("Line2");
        _outputHelper.WriteLine("Line3");
        Assert.Fail("test");
    }

    [Fact]
    public void Fail3()
    {
        _outputHelper.WriteLine("Line1");
        Assert.Fail("Custom Error message");
    }
}