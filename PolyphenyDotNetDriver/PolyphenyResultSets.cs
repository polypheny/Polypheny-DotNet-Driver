using System;
using System.Collections.Generic;
using System.Threading;
using Google.Protobuf.WellKnownTypes;

namespace PolyphenyDotNetDriver;

public class PolyphenyResultSets
{
    private object[][] result;
    private int index;

    public string[] Columns { get; }

    public PolyphenyResultSets(string[] columns, object[][] result)
    {
        this.Columns = columns;
        this.result = result;
        this.index = 0;
    }

    public bool Finish()
    {
        return this.index >= result.Length;
    }

    public object[] GetCurrent()
    {
        if (index < 0 || index >= result.Length)
            throw new IndexOutOfRangeException();

        return result[index];
    }
    
    public object GetByOrdinal(int ordinal)
    {
        if (index < 0 || index >= result.Length || ordinal < 0 || ordinal >= Columns.Length)
            throw new IndexOutOfRangeException();

        return result[index][ordinal];
    }
    
    public bool Next()
    {
        Interlocked.Increment(ref index);
        return index >= 0 && index < result.Length;
    }
}