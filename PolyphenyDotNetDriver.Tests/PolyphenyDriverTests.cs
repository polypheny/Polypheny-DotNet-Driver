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
        var connection = PolyphenyDriver.OpenConnection("localhost:20590,pa:");
        connection.Open();
        connection.Close();
    }
    
    [Test]
    public async Task ShouldPingServer()
    {
        var connection = PolyphenyDriver.OpenConnection("localhost:20590,pa:");
        connection.Open();
        await connection.Ping();
        connection.Close();
    }
    
    [Test]
    public Task ShouldExecuteNonQuery()
    {
        var connection = PolyphenyDriver.OpenConnection("localhost:20590,pa:");
        connection.Open();
        var command = new PolyphenyCommand().
            WithConnection(connection).
            WithCommandText("DROP TABLE IF EXISTS test");
        var result = command.ExecuteNonQuery();
        Assert.That(result, Is.EqualTo(1));
        
        command = new PolyphenyCommand().
            WithConnection(connection).
            WithCommandText("CREATE TABLE test (id INT PRIMARY KEY, name VARCHAR(255))");
        result = command.ExecuteNonQuery();
        Assert.That(result, Is.EqualTo(1));
        
        command = new PolyphenyCommand().
            WithConnection(connection).
            WithCommandText("DROP TABLE IF EXISTS test");
        result = command.ExecuteNonQuery();
        Assert.That(result, Is.EqualTo(1));      
        
        connection.Close();
        return Task.CompletedTask;
    }

    [Test]
    public Task ShouldExecuteNonQueryPrepared()
    {
        var connection = PolyphenyDriver.OpenConnection("localhost:20590,pa:");
        connection.Open();
        var command = new PolyphenyCommand().
            WithConnection(connection).
            WithCommandText("DROP TABLE IF EXISTS test");
        var result = command.ExecuteNonQuery();
        Assert.That(result, Is.EqualTo(1));
        
        command = new PolyphenyCommand().
            WithConnection(connection).
            WithCommandText("CREATE TABLE test (id INT PRIMARY KEY, name VARCHAR(255))");
        result = command.ExecuteNonQuery();
        Assert.That(result, Is.EqualTo(1));
        
        // insert into test table
        command = new PolyphenyCommand().
            WithConnection(connection).
            WithCommandText("INSERT INTO test (id, name) VALUES (?, ?)").
            WithParameterValues(new object[] { 1, "test" });
        command.Prepare();
        result = command.ExecuteNonQuery();
        Assert.That(result, Is.EqualTo(1));
        
        command = new PolyphenyCommand().
            WithConnection(connection).
            WithCommandText("DROP TABLE IF EXISTS test");
        result = command.ExecuteNonQuery();
        Assert.That(result, Is.EqualTo(1));      
        
        connection.Close();
        return Task.CompletedTask;
    }
    
    [Test]
    public Task ShouldAbleToQueryData()
    {
        var connection = PolyphenyDriver.OpenConnection("localhost:20590,pa:");
        connection.Open();

        var command = new PolyphenyCommand().
            WithConnection(connection).
            WithCommandText("SELECT name FROM emps WHERE name = 'Bill'");
        var reader = command.ExecuteReader();
        var columns = reader.Columns;
        Assert.That(columns, Has.Length.EqualTo(1));
        Assert.That(columns[0], Is.EqualTo("name"));
        
        connection.Close();
        return Task.CompletedTask;
    }
    
    [Test]
    public Task ShouldAbleToQueryDataAfterInsert()
    {
        var connection = PolyphenyDriver.OpenConnection("localhost:20590,pa:");
        connection.Open();
        var command = new PolyphenyCommand().
            WithConnection(connection).
            WithCommandText("DROP TABLE IF EXISTS test");
        var result = command.ExecuteNonQuery();
        Assert.That(result, Is.EqualTo(1));
        
        command = new PolyphenyCommand().
            WithConnection(connection).
            WithCommandText("CREATE TABLE test (id INT PRIMARY KEY, name VARCHAR(255))");
        result = command.ExecuteNonQuery();
        Assert.That(result, Is.EqualTo(1));
        
        // insert into test table
        command = new PolyphenyCommand().
            WithConnection(connection).
            WithCommandText("INSERT INTO test (id, name) VALUES (?, ?)").
            WithParameterValues(new object[] { 1, "testname" });
        command.Prepare();
        result = command.ExecuteNonQuery();
        Assert.That(result, Is.EqualTo(1));
        
        // insert into test table
        command = new PolyphenyCommand().
            WithConnection(connection).
            WithCommandText("INSERT INTO test (id, name) VALUES (?, ?)").
            WithParameterValues(new object[] { 2, "testname2" });
        command.Prepare();
        result = command.ExecuteNonQuery();
        Assert.That(result, Is.EqualTo(1));
        
        command = new PolyphenyCommand().
            WithConnection(connection).
            WithCommandText("SELECT * FROM test");
        var reader = command.ExecuteReader();
        var resultSets = reader.ResultSets;
        Assert.That(resultSets.Columns, Has.Length.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(resultSets.Columns[0], Is.EqualTo("id"));
            Assert.That(resultSets.Columns[1], Is.EqualTo("name"));
        });
        var id = reader.GetInt32(0);
        var name = reader.GetString(1);
        Assert.Multiple(() =>
        {
            Assert.That(id, Is.EqualTo(1));
            Assert.That(name, Is.EqualTo("testname"));
        });
        
        var next = reader.NextResult();
        Assert.That(next, Is.True);
        
        id = reader.GetInt32(0);
        name = reader.GetString(1);
        Assert.Multiple(() =>
        {
            Assert.That(id, Is.EqualTo(2));
            Assert.That(name, Is.EqualTo("testname2"));
        });
        next = reader.NextResult();
        Assert.That(next, Is.False);
        
        command = new PolyphenyCommand().
            WithConnection(connection).
            WithCommandText("DROP TABLE IF EXISTS test");
        result = command.ExecuteNonQuery();
        Assert.That(result, Is.EqualTo(1));      
        
        connection.Close();
        return Task.CompletedTask;
    }
}
