namespace PolyphenyDotNetDriver.Tests;

public class PolyphenyParameterCollectionTests
{
    [SetUp]
    public void SetUp()
    {
    }
    
    [Test]
    public void Add_WithPolyphenyParameter_ReturnIndex()
    {
        var collection = new PolyphenyParameterCollection();
        var param = new PolyphenyParameter()
        {
            ParameterName = "param1",
            Value = "value1"
        };
        var idx = collection.Add(param);
        Assert.That(idx, Is.EqualTo(0));
    }
    
    [Test]
    public void Add_WithNonPolyphenyParameter_ThrowsException()
    {
        var collection = new PolyphenyParameterCollection();
        Assert.Throws<ArgumentException>(() =>
        {
            collection.Add(1);
        });
    }        
    
    [Test]
    public void Contains_ShouldReturnTrueIfParameterExists()
    {
        var collection = new PolyphenyParameterCollection();
        var param = new PolyphenyParameter()
        {
            ParameterName = "param1",
            Value = "value1"
        };
        collection.Add(param);
        Assert.That(collection.Contains(param), Is.True);
    }
    
    [Test]
    public void Contains_WithNonPolyphenyParameter_ThrowsException()
    {
        var collection = new PolyphenyParameterCollection();
        Assert.Throws<ArgumentException>(() =>
        {
            collection.Contains(1);
        });
    }
    
    [Test]
    public void IndexOf_WithValidArgument_ReturnIdx()
    {
        var collection = new PolyphenyParameterCollection();
        var param = new PolyphenyParameter()
        {
            ParameterName = "param1",
            Value = "value1"
        };
        var idx = collection.Add(param);
        var idx2 = collection.IndexOf(param);
        Assert.That(idx, Is.EqualTo(idx2));
    }
    
    [Test]
    public void IndexOf_WithNonPolyphenyParameter_ThrowsException()
    {
        var collection = new PolyphenyParameterCollection();
        Assert.Throws<ArgumentException>(() =>
        {
            collection.IndexOf(1);
        });
    }
        
    [Test]
    public void Insert_WithValidArgument_ReturnIdx()
    {
        var collection = new PolyphenyParameterCollection();
        var param = new PolyphenyParameter()
        {
            ParameterName = "param1",
            Value = "value1"
        };
        collection.Insert(0, param);
        var idx = collection.IndexOf(param);
        Assert.That(idx, Is.EqualTo(0));
    }
    
    [Test]
    public void Insert_WithNonPolyphenyParameter_ThrowsException()
    {
        var collection = new PolyphenyParameterCollection();
        Assert.Throws<ArgumentException>(() =>
        {
            collection.Insert(0, 1);
        });
    }
    
    [Test]
    public void Remove_WithValidArgument_RemoveParameter()
    {
        var collection = new PolyphenyParameterCollection();
        var param = new PolyphenyParameter()
        {
            ParameterName = "param1",
            Value = "value1"
        };
        collection.Add(param);
        collection.Remove(param);
        Assert.That(collection, Is.Empty);
    }
    
    [Test]
    public void Remove_WithNonPolyphenyParameter_ThrowsException()
    {
        var collection = new PolyphenyParameterCollection();
        Assert.Throws<ArgumentException>(() =>
        {
            collection.Remove(1);
        });
    }
    
    [Test]
    public void RemoveAt_RemoveParameter()
    {
        var collection = new PolyphenyParameterCollection();
        var param = new PolyphenyParameter()
        {
            ParameterName = "param1",
            Value = "value1"
        };
        collection.Add(param);
        collection.RemoveAt(0);
        Assert.That(collection, Is.Empty);
    }

    [Test]
    public void IndexOf_WithParameterName_ReturnIndex()
    {
        var collection = new PolyphenyParameterCollection();
        var param = new PolyphenyParameter()
        {
            ParameterName = "param1",
            Value = "value1"
        };
        collection.Add(param);
        var idx = collection.IndexOf("param1");
        Assert.That(idx, Is.EqualTo(0));
    }
    
    [Test]
    public void IndexOf_WithInvalidParameterName_ReturnMinusOne()
    {
        var collection = new PolyphenyParameterCollection();
        var param = new PolyphenyParameter()
        {
            ParameterName = "param1",
            Value = "value1"
        };
        collection.Add(param);
        var idx = collection.IndexOf("param2");
        Assert.That(idx, Is.EqualTo(-1));
    }
    
    [Test]
    public void RemoveAt_WithParameterName_RemoveParameter()
    {
        var collection = new PolyphenyParameterCollection();
        var param = new PolyphenyParameter()
        {
            ParameterName = "param1",
            Value = "value1"
        };
        collection.Add(param);
        collection.RemoveAt("param1");
        Assert.That(collection, Is.Empty);
    }

    [Test]
    public void Contain_WithParameterName()
    {
        var collection = new PolyphenyParameterCollection();
        var param = new PolyphenyParameter()
        {
            ParameterName = "param1",
            Value = "value1"
        };
        Assert.That(collection.Contains("param1"), Is.False);
    }
    
    [Test]
    public void CopyTo_CopyParameterList()
    {
        var collection = new PolyphenyParameterCollection();
        var param = new PolyphenyParameter()
        {
            ParameterName = "param1",
            Value = "value1"
        };
        var param2 = new PolyphenyParameter()
        {
            ParameterName = "param2",
            Value = "value2",
        };
        collection.Add(param);
        collection.Add(param2);
        
        var array = new PolyphenyParameter[2];
        collection.CopyTo(array, 0);
        
        Assert.Multiple(() =>
        {
            Assert.That(array[0], Is.EqualTo(param));
            Assert.That(array[1], Is.EqualTo(param2));
        });
    }

    [Test]
    public void GetEnumerator_ReturnListEnumerator()
    {
        var collection = new PolyphenyParameterCollection();
        var param = new PolyphenyParameter()
        {
            ParameterName = "param1",
            Value = "value1"
        };
        var param2 = new PolyphenyParameter()
        {
            ParameterName = "param2",
            Value = "value2",
        };
        collection.Add(param);
        collection.Add(param2);
        
        var array = new PolyphenyParameter[2];
        collection.CopyTo(array, 0);

        var idx = 0;
        foreach (var p in collection)
        {
            Assert.That(p, Is.EqualTo(array[idx]));
            idx++;
        }
    }
    
    [Test]
    public void GetParameter_ReturnParameter()
    {
        var collection = new PolyphenyParameterCollection();
        var param = new PolyphenyParameter()
        {
            ParameterName = "param1",
            Value = "value1"
        };
        var param2 = new PolyphenyParameter()
        {
            ParameterName = "param2",
            Value = "value2",
        };
        
        var array = new PolyphenyParameter[2];
        array[0] = param;
        array[1] = param2;
        
        collection.AddRange(array);
        Assert.That(collection, Has.Count.EqualTo(2));                                                                                                                                                                                                                                                                                   
    }
}
