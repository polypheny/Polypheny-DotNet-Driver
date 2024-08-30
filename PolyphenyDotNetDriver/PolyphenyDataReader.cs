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
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Polypheny.Prism;
using PolyphenyDotNetDriver.Interface;
using Type = System.Type;

namespace PolyphenyDotNetDriver
{
    public class PolyphenyDataReader: DbDataReader
    {
        private bool _isOpen = true;
        private readonly IPolyphenyCommand _cmd;
        public PolyphenyResultSets ResultSets { get; set; } = null;
        public string[] Columns => this.ResultSets?.Columns;

        public PolyphenyDataReader(IPolyphenyCommand cmd)
        {
            this._cmd = cmd;
        }
        
        private T GetCast<T>(int ordinal)
        {
            var val = GetValue(ordinal);
            if (val is T t)
                return t;

            throw new Exception($"Wrong type accessing: expected {typeof(T)}, got {val?.GetType()}");
        }
        
        public override bool GetBoolean(int ordinal)
        {
            return GetCast<bool>(ordinal);
        }

        public override byte GetByte(int ordinal)
        {
            return GetCast<byte>(ordinal);
        }

        public override long GetBytes(int ordinal, long fieldOffset, byte[] buffer, int bufferoffset, int length)
        {
            var val = GetValue(ordinal);
            if (val is not byte[] bytes) throw new Exception("Wrong type accessing");
            
            if (buffer == null)
            {
                return bytes.Length;
            }
                
            if (bufferoffset >= buffer.Length || bufferoffset < 0)
                throw(new IndexOutOfRangeException("Buffer index must be a valid index in buffer"));
            if (buffer.Length < (bufferoffset + length))
                throw(new ArgumentException("Buffer is not large enough to hold the requested data"));
            if (fieldOffset < 0 ||
                ((ulong)fieldOffset >= (ulong)bytes.Length && (ulong)bytes.Length > 0))
                throw(new IndexOutOfRangeException("Data index must be a valid index in the field"));

            if ((ulong)bytes.Length < (ulong)(fieldOffset + length))
            {
                length = (int)((ulong)bytes.Length - (ulong)fieldOffset);
            }

            Buffer.BlockCopy(bytes, (int)fieldOffset, buffer, (int)bufferoffset, (int)length);

            return length;
        }

        public override char GetChar(int ordinal)
        {
            return GetString(ordinal)[0];
        }

        public override long GetChars(int i, long fieldoffset, char[] buffer, int bufferoffset, int length)
        {
            if (i >= FieldCount)
                throw new IndexOutOfRangeException();

            var valAsString = GetString(i);

            if (buffer == null) return valAsString.Length;

            if (bufferoffset >= buffer.Length || bufferoffset < 0)
                throw new IndexOutOfRangeException("Buffer index must be a valid index in buffer");
            if (buffer.Length < (bufferoffset + length))
                throw (new ArgumentException("Buffer is not large enough to hold the requested data"));
            if (fieldoffset < 0 || fieldoffset >= valAsString.Length)
                throw (new IndexOutOfRangeException("Field offset must be a valid index in the field"));

            if (valAsString.Length < length)
                length = valAsString.Length;
            valAsString.CopyTo((int)fieldoffset, buffer, bufferoffset, length);
            return length;
        }

        public override string GetDataTypeName(int ordinal)
        {
            return GetValue(ordinal).GetType().ToString();
        }

        public override DateTime GetDateTime(int ordinal)
        {
            return GetCast<DateTime>(ordinal);
        }

        public override decimal GetDecimal(int ordinal)
        {
            return GetCast<decimal>(ordinal);
        }

        public override double GetDouble(int ordinal)
        {
            return GetCast<double>(ordinal);
        }

        public override Type GetFieldType(int ordinal)
        {
            return GetValue(ordinal).GetType();
        }
        
        public override float GetFloat(int ordinal)
        {
            return GetCast<float>(ordinal);
        }

        public override Guid GetGuid(int ordinal)
        {
            return GetCast<Guid>(ordinal);
        }

        public override short GetInt16(int ordinal)
        {
            return GetCast<short>(ordinal);
        }

        public override int GetInt32(int ordinal)
        {
            return GetCast<int>(ordinal);
        }

        public override long GetInt64(int ordinal)
        {
            return GetCast<long>(ordinal);
        }

        public override string GetName(int ordinal)
        {
            if (IsClosed)
                throw new Exception("No current query in data reader");

            if (ordinal < 0 || ordinal >= ResultSets.Columns.Length)
                throw new IndexOutOfRangeException();

            return ResultSets.Columns[ordinal];
        }

        public override int GetOrdinal(string name)
        {
            if (IsClosed || ResultSets == null)
                throw new Exception("No current query in data reader");

            var ordinal = -1;
            for (var i = 0; i < ResultSets.Columns.Length; i++)
            {
                if (ResultSets.Columns[i] == name)
                {
                    ordinal = i;
                }
            }

            return ordinal;
        }

        public override string GetString(int ordinal)
        {
            return GetValue(ordinal).ToString() ?? string.Empty;
        }

        public override object GetValue(int ordinal)
        {
            return ResultSets.GetByOrdinal(ordinal);
        }

        public override int GetValues(object[] values)
        {
            var numCols = Math.Min(values.Length, FieldCount);
            for (var i = 0; i < numCols; i++)
            {
                values[i] = GetValue(i);
            }
            return numCols;
        }

        public override bool IsDBNull(int ordinal)
        {
            if (ResultSets == null)
                throw new Exception("ResultSet is null");

            return ResultSets.GetByOrdinal(ordinal) == null;
        }

        public override int FieldCount => this.ResultSets != null ? this.ResultSets.Columns.Length : 0;

        public override object this[int ordinal] => GetValue(ordinal);

        public override object this[string name] => GetValue(GetOrdinal(name));

        public override int RecordsAffected => 0;
        public override bool HasRows => this.ResultSets != null && !this.ResultSets.Finish();
        public override bool IsClosed => !_isOpen;

        public override bool NextResult() => NextResultAsync(CancellationToken.None).GetAwaiter().GetResult();

        public override async Task<bool> NextResultAsync(CancellationToken cancellationToken)
        {
            if (IsClosed)
                throw new Exception("DataReader is closed");

            if (this.ResultSets != null) return !this.ResultSets.Finish() && this.ResultSets.Next();
            
            await Fetch();
            return this.ResultSets != null && !this.ResultSets.Finish();

        }

        public override bool Read() => ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        
        public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            if (IsClosed)
                throw new Exception("DataReader is closed");

            if (this.ResultSets != null) return !this.ResultSets.Finish() && this.ResultSets.Next();
            
            await Fetch();
            return this.ResultSets != null && !this.ResultSets.Finish();

        }

        public override int Depth => 0;

        public override IEnumerator GetEnumerator()
        {
            return new DbEnumerator(this);
        }

        private async Task Fetch()
        {
            var request = new Request()
            {
                ExecuteUnparameterizedStatementRequest = new ExecuteUnparameterizedStatementRequest()
                {
                    LanguageName = "sql",
                    Statement = this._cmd.CommandText,
                }
            };

            var response = await this._cmd.SendRecv(request);
            var tmpId = response?.StatementResponse?.StatementId;

            var newResponse = await this._cmd.Receive(8);
            var statementId = newResponse?.StatementResponse?.StatementId;
            if (statementId != tmpId)
            {
                throw new Exception("StatementID mismatch");
            }

            var resultSets = ExtractResultSet(newResponse);
            this.ResultSets = resultSets;
        }

        private static PolyphenyResultSets ExtractResultSet(Response response)
        {
            var relationalFrame = response?.StatementResponse?.Result?.Frame?.RelationalFrame;
            if (relationalFrame == null)
            {
                throw new Exception("Query expects to return data, however the result is empty");
            }

            var columnResponse = relationalFrame.ColumnMeta;
            var columns = columnResponse.Select(x => x.ColumnName).ToArray();
            var rows = relationalFrame.Rows;
            
            var values = rows.Select(x => x.Values.Select(ProtoHelper.FromProtoValue).ToArray()).ToArray();

            return new PolyphenyResultSets(columns, values);
        }
        
        public override void Close()
        {
            _isOpen = false;
            base.Close();
        }
    }
}
