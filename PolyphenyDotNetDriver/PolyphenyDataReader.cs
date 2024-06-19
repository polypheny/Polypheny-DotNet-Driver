using System;
using System.Collections;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Polypheny.Prism;
using Type = System.Type;

namespace PolyphenyDotNetDriver
{
    public class PolyphenyDataReader: DbDataReader
    {
        private bool _isOpen = true;
        private PolyphenyCommand cmd;
        public PolyphenyResultSets ResultSets { get; private set; } = null;

        public PolyphenyDataReader(PolyphenyCommand cmd)
        {
            this.cmd = cmd;
        }
        
        public override bool GetBoolean(int ordinal)
        {
            var val = GetValue(ordinal);
            if (val is bool b)
                return b;

            throw new Exception("Wrong type accessing");
        }

        public override byte GetByte(int ordinal)
        {
            var val = GetValue(ordinal);
            if (val is byte b)
                return b;

            throw new Exception("Wrong type accessing");
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
            throw new NotImplementedException();
        }

        public override DateTime GetDateTime(int ordinal)
        {
            var val = GetValue(ordinal);
            if (val is DateTime dateTime)
                return dateTime;

            throw new Exception("Wrong type accessing");
        }

        public override decimal GetDecimal(int ordinal)
        {
            var val = GetValue(ordinal);
            if (val is decimal d)
                return d;

            throw new Exception("Wrong type accessing");
        }

        public override double GetDouble(int ordinal)
        {
            var val = GetValue(ordinal);
            if (val is double d)
                return d;

            throw new Exception("Wrong type accessing");
        }

        public override Type GetFieldType(int ordinal)
        {
            return GetValue(ordinal).GetType();
        }

        public override float GetFloat(int ordinal)
        {
            var val = GetValue(ordinal);
            if (val is float f)
                return f;

            throw new Exception("Wrong type accessing");
        }

        public override Guid GetGuid(int ordinal)
        {
            var val = GetValue(ordinal);
            if (val is Guid g)
                return g;

            throw new Exception("Wrong type accessing");
        }

        public override short GetInt16(int ordinal)
        {
            var val = GetValue(ordinal);
            if (val is short s)
                return s;

            throw new Exception("Wrong type accessing");
        }

        public override int GetInt32(int ordinal)
        {
            var val = GetValue(ordinal);
            if (val is int i)
                return i;

            throw new Exception("Wrong type accessing");
        }

        public override long GetInt64(int ordinal)
        {
            var val = GetValue(ordinal);
            if (val is long l)
                return l;

            throw new Exception("Wrong type accessing");
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

        public override object this[int ordinal] => throw new NotImplementedException();

        public override object this[string name] => throw new NotImplementedException();

        public override int RecordsAffected => 0;
        public override bool HasRows => this.ResultSets != null && !this.ResultSets.Finish();
        public override bool IsClosed => !_isOpen;

        public override bool NextResult() => NextResultAsync(CancellationToken.None).GetAwaiter().GetResult();

        public override async Task<bool> NextResultAsync(CancellationToken cancellationToken)
        {
            if (IsClosed)
                throw new Exception("DataReader is closed");

            if (this.ResultSets == null)
            {
                await Fetch();
                return this.ResultSets != null && !this.ResultSets.Finish();
            }
            
            if (this.ResultSets.Finish())
                return false;

            return this.ResultSets.Next();
        }

        public override bool Read() => ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        
        public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            if (IsClosed)
                throw new Exception("DataReader is closed");

            if (this.ResultSets == null)
            {
                await Fetch();
                return this.ResultSets != null && !this.ResultSets.Finish();
            }
            
            if (this.ResultSets.Finish())
                return false;

            return this.ResultSets.Next();
        }

        public override int Depth => 0;

        public override IEnumerator GetEnumerator()
        {
            throw new NotImplementedException();
        }

        private async Task Fetch()
        {
            var request = new Request()
            {
                ExecuteUnparameterizedStatementRequest = new ExecuteUnparameterizedStatementRequest()
                {
                    LanguageName = "sql",
                    Statement = this.cmd.CommandText,
                }
            };

            var response = await this.cmd.PolyphenyConnection.SendRecv(request);
            Console.WriteLine("response"+response);
            var tmpId = response?.StatementResponse?.StatementId;

            var newResponse = await this.cmd.PolyphenyConnection.Receive(8);
            var statementId = newResponse?.StatementResponse?.StatementId;
            if (statementId != tmpId)
            {
                throw new Exception("StatementID mismatch");
            }

            Console.WriteLine(newResponse);
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
    }
}