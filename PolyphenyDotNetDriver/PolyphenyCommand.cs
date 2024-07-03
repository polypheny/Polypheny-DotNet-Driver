using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Polypheny.Prism;

namespace PolyphenyDotNetDriver
{
    public class PolyphenyCommand : DbCommand
    {
        public PolyphenyCommand WithConnection(PolyphenyConnection connection)
        {
            this.PolyphenyConnection = connection;
            return this;
        }
        
        public PolyphenyCommand WithCommandText(string commandText)
        {
            this.CommandText = commandText;
            return this;
        }

        public PolyphenyCommand WithParameterValues(object[] values)
        {
            this.ParameterValues = values;
            return this;
        }

        public PolyphenyCommand WithTransaction(PolyphenyTransaction transaction)
        {
            if (transaction.PolyphenyConnection != this.PolyphenyConnection)
            {
                throw new Exception("mismatch connection");
            }
            
            DbTransaction = transaction;
            return this;
        }
        
        public override void Cancel()
        {
            throw new System.NotImplementedException();
        }

        public override int ExecuteNonQuery() => ExecuteNonQueryAsync(CancellationToken.None).GetAwaiter().GetResult();
        
        public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            if (PolyphenyConnection == null)
            {
                throw new InvalidOperationException("Connection property must be non-null");
            }

            if (!_isPrepared)
            {
                return await ExecuteNonQueryAsyncUnprepared(cancellationToken);
            }
            else
            {
                return await ExecuteNonQueryAsyncPrepared(cancellationToken);
            }
        }

        private async Task<int> ExecuteNonQueryAsyncUnprepared(CancellationToken cancellationToken)
        {
            var request = new Request()
            {
                ExecuteUnparameterizedStatementRequest = new ExecuteUnparameterizedStatementRequest()
                {
                    LanguageName = "sql",
                    Statement = CommandText,
                }
            };

            var response = await this.PolyphenyConnection.SendRecv(request);
            var tmpId = response?.StatementResponse?.StatementId;

            var newResponse = await this.PolyphenyConnection.Receive();
            if (newResponse?.StatementResponse?.StatementId != tmpId)
            {
                throw new Exception("StatementId mismatch");
            }

            var stmtResult = newResponse?.StatementResponse?.Result;
            var result = ProtoHelper.FormatResult(stmtResult);
            
            return (int)result.RowsAffected;
        }
        
        private async Task<int> ExecuteNonQueryAsyncPrepared(CancellationToken cancellationToken)
        {
            if (this.ParameterValues == null || this._statementId == null)
            {
                throw new InvalidOperationException("ParameterValues and private StatementId property must be non-null");
            }
            
            this._parameterCollection.FillParameterValues(this.ParameterValues);
            var protoValues = this._parameterCollection.ToProtoValues();
            
            var request = new Request()
            {
                ExecuteIndexedStatementRequest = new ExecuteIndexedStatementRequest()
                {
                    StatementId = this._statementId.Value,
                    Parameters = new IndexedParameters()
                    {
                        Parameters = { protoValues }                    
                    }
                }
            };

            var response = await this.PolyphenyConnection.SendRecv(request);
            var statementResult = response?.StatementResult;
            var rowAffected = statementResult?.Scalar;
            
            if (rowAffected == null)
            {
                throw new Exception("No rows affected");
            }
            
            return (int)rowAffected;
        }

        public override object ExecuteScalar()
        {
            throw new System.NotImplementedException();
        }

        public override void Prepare() => PrepareAsync(CancellationToken.None).GetAwaiter().GetResult();

        public override async Task PrepareAsync(CancellationToken cancellationToken)
        {
            if (PolyphenyConnection == null)
            {
                throw new InvalidOperationException("Connection property must be non-null");
            }

            var request = new Request()
            {
                PrepareIndexedStatementRequest = new PrepareStatementRequest()
                {
                    LanguageName = "sql",
                    Statement = CommandText,
                }
            };
            
            var response = await this.PolyphenyConnection.SendRecv(request);
            var parameterMetas = response?.PreparedStatementSignature?.ParameterMetas;
            var parameterCollections = PolyphenyParameterCollection.FromParameterMetas(parameterMetas);
            this._parameterCollection = parameterCollections;
            this._statementId = response?.PreparedStatementSignature?.StatementId;
            this._isPrepared = true;
        }

        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; } = 30;
        public override CommandType CommandType { get; set; } = CommandType.Text;
        public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.Both;

        public PolyphenyConnection PolyphenyConnection { get; private set; }
        protected override DbConnection DbConnection
        {
            get => this.PolyphenyConnection;
            set
            {
                if (value is PolyphenyConnection polyphenyConnection)
                {
                    this.PolyphenyConnection = polyphenyConnection;
                }
                else
                {
                    throw new InvalidOperationException("Connection must be a PolyphenyConnection");
                }
            }
        }

        private bool _isPrepared = false;
        private int? _statementId = null;
        private PolyphenyParameterCollection _parameterCollection;
        protected override DbParameterCollection DbParameterCollection => this._parameterCollection;
        protected override DbTransaction DbTransaction { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public object[] ParameterValues { get; private set; }

        protected override DbParameter CreateDbParameter()
        {
            return new PolyphenyParameter();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => ExecuteReader(behavior);

        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior,
            CancellationToken cancellationToken) => await ExecuteReaderAsync(behavior, cancellationToken);

        public new PolyphenyDataReader ExecuteReader() => ExecuteReader(CommandBehavior.Default);
        
        public new PolyphenyDataReader ExecuteReader(CommandBehavior behavior) => ExecuteReaderAsync(behavior, CancellationToken.None).GetAwaiter().GetResult();

        private new async Task<PolyphenyDataReader> ExecuteReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        {
            var reader = new PolyphenyDataReader(this);
            await reader.NextResultAsync(cancellationToken);
            return reader;
        }

        public Dictionary<object, object>[] ExecuteQueryMongo() => ExecuteQueryMongoAsync(CancellationToken.None).GetAwaiter().GetResult();
        private async Task<Dictionary<object, object>[]> ExecuteQueryMongoAsync(CancellationToken cancellationToken)
        {
            if (PolyphenyConnection == null)
            {
                throw new InvalidOperationException("Connection property must be non-null");
            }

            var request = new Request()
            {
                ExecuteUnparameterizedStatementRequest = new ExecuteUnparameterizedStatementRequest()
                {
                    LanguageName = "mongo",
                    Statement = CommandText,
                }
            };
            
            var response = await this.PolyphenyConnection.SendRecv(request);
            var tmpId = response?.StatementResponse?.StatementId;
            
            var newResponse = await this.PolyphenyConnection.Receive();
            if (newResponse?.StatementResponse?.StatementId != tmpId)
            {
                throw new Exception("StatementId mismatch");
            }

            var documentFrame = newResponse?.StatementResponse?.Result?.Frame?.DocumentFrame;
            if (documentFrame == null)
            {
                throw new Exception("Query should return document data, however the result is empty");
            }

            var documents = documentFrame.Documents;
            var result = new Dictionary<object, object>[documents.Count];
            for (var i = 0; i < documents.Count; i++)
            {
                var currentRow = new Dictionary<object, object>();
                foreach (var entry in documents[i].Entries)
                {
                    var key = ProtoHelper.FromProtoValue(entry.Key);
                    var value = ProtoHelper.FromProtoValue(entry.Value);
                    currentRow[key] = value;
                }
                result[i] = currentRow;
            }

            return result;
        }
    }
}