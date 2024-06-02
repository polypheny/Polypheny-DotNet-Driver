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
    
    [Test]
    public async Task ShouldExecuteNonQuery()
    {
        var connection = new PolyphenyConnection("localhost:20590,pa:");
        connection.Open();
        var command = new PolyphenyCommand().WithConnection(connection).WithCommandText("DROP TABLE IF EXISTS test");
        var result = command.ExecuteNonQuery();
        Console.WriteLine("result: " + result);
        Assert.That(result, Is.EqualTo(1));
        
        command = command.WithCommandText("CREATE TABLE test (id INT PRIMARY KEY, name VARCHAR(255))");
        result = command.ExecuteNonQuery();
        Console.WriteLine("result: " + result);
        Assert.That(result, Is.EqualTo(1));
        
        command = command.WithCommandText("DROP TABLE IF EXISTS test");
        result = command.ExecuteNonQuery();
        Console.WriteLine("result: " + result);
        Assert.That(result, Is.EqualTo(1));      
        
        connection.Close();
    }
}
