namespace PolyphenyDotNetDriver.Tests;

public class PolyphenyDriverTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void ShouldOpenAndCloseConnection()
    {
        var connection = new PolyphenyConnection("localhost:20590,pa:");
        connection.Open();
        connection.Close();
    }
    
    [Test]
    public async Task ShouldPingServer()
    {
        var connection = new PolyphenyConnection("localhost:20590,pa:");
        connection.Open();
        await connection.Ping();
        connection.Close();
    }
}
