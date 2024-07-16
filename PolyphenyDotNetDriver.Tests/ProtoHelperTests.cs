using Polypheny.Prism;

namespace PolyphenyDotNetDriver.Tests;

public class ProtoHelperTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void FormatResult_WithNull_ReturnsEmpty()
    {
        var formatted = ProtoHelper.FormatResult(null);
        var expected = PolyphenyResult.Empty();
        
        Assert.Multiple(() =>
        {
            Assert.That(formatted.RowsAffected, Is.EqualTo(expected.RowsAffected));
            Assert.That(formatted.LastInsertedId, Is.EqualTo(expected.LastInsertedId));
        });
    }

    [Test]
    public void FormatResult_WithNonNull_ReturnsFilledResult()
    {
        var result = new StatementResult()
        {
            Scalar = 5,
        };
        var formatted = ProtoHelper.FormatResult(result);
        var expected = new PolyphenyResult(-1, 5);
        
        Assert.Multiple(() =>
        {
            Assert.That(formatted.RowsAffected, Is.EqualTo(expected.RowsAffected));
            Assert.That(formatted.LastInsertedId, Is.EqualTo(expected.LastInsertedId));
        });
    }
    
    [Test]
    public void ToProtoValue_WithUnsupportedType_ThrowsArgumentException()
    {
        var input = DateTime.Now;

        var ex = Assert.Throws<ArgumentException>(() => ProtoHelper.ToProtoValue(input));
        Assert.That(ex?.Message, Does.Contain("Unsupported value type"));
    }
    
    [Test]
    public void ToProtoValue_WithSupportedType_ReturnsAppropriateProtoValue()
    {
        var input = new object[]
        {
            123,
            123L,
            123.45f,
            123.45,
            "test",
            true
        };

        var expected = new[]
        {
            new ProtoValue { Integer = new ProtoInteger { Integer = 123 } },
            new ProtoValue { Long = new ProtoLong { Long = 123 } },
            new ProtoValue { Float = new ProtoFloat { Float = 123.45f } },
            new ProtoValue { Double = new ProtoDouble { Double = 123.45 } },
            new ProtoValue { String = new ProtoString { String = "test" } },
            new ProtoValue { Boolean = new ProtoBoolean { Boolean = true } }
        };

        for (var i = 0; i < input.Length; i++)
        {
            var result = ProtoHelper.ToProtoValue(input[i]);
            Assert.That(result, Is.EqualTo(expected[i]));
        }
    }
    
    [Test]
    public void FromProtoValue_WithSupportedType_ReturnsAppropriateValue()
    {
        var input = new[]
        {
            new ProtoValue { Integer = new ProtoInteger { Integer = 123 } },
            new ProtoValue { Long = new ProtoLong { Long = 123 } },
            new ProtoValue { Float = new ProtoFloat { Float = 123.45f } },
            new ProtoValue { Double = new ProtoDouble { Double = 123.45 } },
            new ProtoValue { String = new ProtoString { String = "test" } },
            new ProtoValue { Boolean = new ProtoBoolean { Boolean = true } },
            new ProtoValue { BigDecimal = new ProtoBigDecimal { Scale = 5 } }
        };

        var expected = new object?[]
        {
            123,
            123L,
            123.45f,
            123.45,
            "test",
            true,
            null
        };

        for (var i = 0; i < input.Length; i++)
        {
            var result = ProtoHelper.FromProtoValue(input[i]);
            Assert.That(result, Is.EqualTo(expected[i]));
        }
    }
    
    [Test]
    public void FromProtoValue_WithUnsupportedType_ThrowsException()
    {
        var input = new ProtoValue { Date = new ProtoDate() };

        var ex = Assert.Throws<Exception>(() => ProtoHelper.FromProtoValue(input));
        Assert.That(ex?.Message, Does.Contain("Failed to convert ProtoValue. This is likely a bug."));
    }
}
