/*
 * Copyright 2019-2024 The Polypheny Project
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

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
        this._index = -1;
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

    public int RowCount()
    {
        return this._result.Length;
    }
}
