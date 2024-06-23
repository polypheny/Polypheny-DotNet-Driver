using System;
using System.Collections.Generic;
using System.Threading;
using Google.Protobuf.WellKnownTypes;

namespace PolyphenyDotNetDriver;

public class PolyphenyResultSets
{
    private readonly object[][] _result;
    private int _index;

    public string[] Columns { get; }

    public PolyphenyResultSets(string[] columns, object[][] result)
    {
        this.Columns = columns;
        this._result = result;
        this._index = 0;
    }

    public bool Finish()
    {
        return this._index >= _result.Length;
    }

    public object[] GetCurrent()
    {
        if (_index < 0 || _index >= _result.Length)
            throw new IndexOutOfRangeException();

        return _result[_index];
    }
    
    public object GetByOrdinal(int ordinal)
    {
        if (_index < 0 || _index >= _result.Length || ordinal < 0 || ordinal >= Columns.Length)
            throw new IndexOutOfRangeException();

        return _result[_index][ordinal];
    }
    
    public bool Next()
    {
        Interlocked.Increment(ref _index);
        return _index >= 0 && _index < _result.Length;
    }
}