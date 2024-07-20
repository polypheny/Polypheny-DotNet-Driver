namespace PolyphenyDotNetDriver.Tests;

public class PolyphenyResultSetsTests
{
    [SetUp]
    public void SetUp()
    {
        
    }
    
    [Test]
    public void GetCurrent_ReturnsCurrentRow()
    {
        var columns = new[] { "test1", "test2" };
        var results = new[] { new object[] { 1, "test" } };
        
        var resultSet = new PolyphenyResultSets(columns, results);
        resultSet.Next();

        var current = resultSet.GetCurrent();
        
        Assert.Multiple(() =>
        {
            Assert.That(current[0], Is.EqualTo(1));
            Assert.That(current[1], Is.EqualTo("test"));
        });
    }
    
    [Test]
    public void GetCurrent_WithOutOfIndex_ThrowsIndexOutOfRangeException()
    {
        var columns = new[] { "test1", "test2" };
        var results = new[] { new object[] { 1, "test" } };
        
        var resultSet = new PolyphenyResultSets(columns, results);
        resultSet.Next();

        Assert.Throws<IndexOutOfRangeException>(() =>
        {
            resultSet.Next();
            var current = resultSet.GetCurrent();
        });
    }
    
    [Test]
    public void Next_WithValidIndex_ReturnsNextRow()
    {
        var columns = new[] { "test1", "test2" };
        var results = new[] { new object[] { 1, "test" }, new object[] { 2, "test2" } };
        
        var resultSet = new PolyphenyResultSets(columns, results);
        resultSet.Next();
        resultSet.Next();
        
        var current = resultSet.GetCurrent();
        Assert.Multiple(() =>
        {
            Assert.That(current[0], Is.EqualTo(2));
            Assert.That(current[1], Is.EqualTo("test2"));
        });
    }
    
    [Test]
    public void Next_WithInValidIndex_ReturnsFalse()
    {
        var columns = new[] { "test1", "test2" };
        var results = new[] { new object[] { 1, "test" }, new object[] { 2, "test2" } };
        
        var resultSet = new PolyphenyResultSets(columns, results);
        resultSet.Next();
        resultSet.Next();
        
        var next2 = resultSet.Next();
        Assert.That(next2, Is.False);
    }
    
    [Test]
    public void GetByOrdinal_WithInvalidIndex_ThrowsIndexOutOfRangeException()
    {
        var columns = new[] { "test1", "test2" };
        var results = new[] { new object[] { 1, "test" } };
        
        var resultSet = new PolyphenyResultSets(columns, results);
        resultSet.Next();

        Assert.Throws<IndexOutOfRangeException>(() =>
        {
            var value = resultSet.GetByOrdinal(2);
        });
    }
    
    [Test]
    public void GetByOrdinal_WithInvalidIndex2_ThrowsIndexOutOfRangeException()
    {
        var columns = new[] { "test1", "test2" };
        var results = new[] { new object[] { 1, "test" } };
        
        var resultSet = new PolyphenyResultSets(columns, results);
        resultSet.Next();

        Assert.Throws<IndexOutOfRangeException>(() =>
        {
            var value = resultSet.GetByOrdinal(-1);
        });
    }
    
    [Test]
    public void GetByOrdinal_WithInvalidIndex3_ThrowsIndexOutOfRangeException()
    {
        var columns = new[] { "test1", "test2" };
        var results = new[] { new object[] { 1, "test" } };
        
        var resultSet = new PolyphenyResultSets(columns, results);
        resultSet.Next();
        resultSet.Next();

        Assert.Throws<IndexOutOfRangeException>(() =>
        {
            var value = resultSet.GetByOrdinal(0);
        });
    }
}