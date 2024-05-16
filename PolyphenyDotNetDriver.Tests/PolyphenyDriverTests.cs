namespace PolyphenyDotNetDriver.Tests;

public class PolyphenyDriverTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void ShouldHaveCorrectName()
    {
        var driver = new PolyphenyDriver();
        Assert.That(driver.Name, Is.EqualTo("PolyphenyDotNetDriver"));
    }
}