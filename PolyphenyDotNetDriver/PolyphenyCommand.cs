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
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Polypheny.Prism;
using PolyphenyDotNetDriver.Interface;

namespace PolyphenyDotNetDriver
{
    public class PolyphenyCommand : DbCommand, IPolyphenyCommand
    {
        public PolyphenyCommand WithConnection(IPolyphenyConnection connection)
        {
            this.connection = connection;

            if (connection is DbConnection c)
            {
                this.DbConnection = c;
            }
            else
            {
                this.DbConnection = null;
            }
            
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
            if (transaction.PolyphenyConnection != this.connection)
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
            if (connection == null)
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

            var response = await ExecuteUnparameterizedAsync(request, "StatementId mismatch").ConfigureAwait(false);
            var stmtResult = response?.StatementResponse?.Result;
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

            var response = await SendRecv(request);
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
            if (connection == null)
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
            
            var response = await SendRecv(request);
            var parameterMetas = response?.PreparedStatementSignature?.ParameterMetas;
            var parameterCollections = PolyphenyParameterCollection.FromParameterMetas(parameterMetas);
            this._parameterCollection = parameterCollections;
            this._statementId = response?.PreparedStatementSignature?.StatementId;
            this._isPrepared = true;
        }

        public async Task<Response> SendRecv(Request m)
        {
            var response = await this.connection.SendRecv(m).ConfigureAwait(false);
            PrismResponse.ThrowIfError(response, m.Id);
            return response;
        }

        public async Task<Response> Receive(int lengthSize)
        {
            var response = await this.connection.Receive(lengthSize).ConfigureAwait(false);
            PrismResponse.ThrowIfError(response);
            return response;
        }

        internal Task<Response> ExecuteUnparameterizedAsync(Request request, string mismatchMessage)
        {
            var executionConnection = connection;
            return PrismStatement.ExecuteUnparameterizedAsync(
                request, executionConnection.SendRecv, executionConnection.Receive, mismatchMessage, executionConnection);
        }

        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; } = 30;
        public override CommandType CommandType { get; set; } = CommandType.Text;
        public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.Both;

        private IPolyphenyConnection connection { get; set; }
        
        private bool _isPrepared = false;
        private int? _statementId = null;
        private PolyphenyParameterCollection _parameterCollection = new();
        protected override DbConnection DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => this._parameterCollection;
        protected override DbTransaction DbTransaction { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public object[] ParameterValues { get; private set; }

        /// <summary>Executes one Cypher statement and returns its first result page.</summary>
        /// <param name="namespaceName">The graph namespace.</param>
        /// <param name="fetchSize">The maximum source rows per page. One row may contain multiple graph elements.</param>
        public PolyphenyCypherResult ExecuteCypher(string namespaceName, int fetchSize = 100)
            => ExecuteCypherAsync(namespaceName, fetchSize).GetAwaiter().GetResult();

        public Task<PolyphenyCypherResult> ExecuteCypherAsync(string namespaceName, int fetchSize = 100)
            => ExecuteCypherAsync(namespaceName, null, fetchSize);

        /// <summary>Executes Cypher with named Prism parameters on a server with prepared Cypher support.</summary>
        public PolyphenyCypherResult ExecuteCypher(string namespaceName, IReadOnlyDictionary<string, ProtoValue> parameters, int fetchSize = 100)
            => ExecuteCypherAsync(namespaceName, parameters, fetchSize).GetAwaiter().GetResult();

        public async Task<PolyphenyCypherResult> ExecuteCypherAsync(string namespaceName, IReadOnlyDictionary<string, ProtoValue> parameters, int fetchSize = 100)
        {
            var executionConnection = connection;
            var statement = CommandText;
            if (executionConnection == null)
            {
                throw new InvalidOperationException("Connection property must be non-null.");
            }
            var sessionVersion = (executionConnection as PolyphenyConnection)?.SessionVersion;
            if (!PrismStatement.IsCurrentSession(executionConnection, sessionVersion))
            {
                throw new InvalidOperationException("Open the connection before executing Cypher.");
            }
            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                throw new ArgumentException("A graph namespace is required.", nameof(namespaceName));
            }
            if (string.IsNullOrWhiteSpace(statement))
            {
                throw new InvalidOperationException("A Cypher statement is required.");
            }
            if (fetchSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(fetchSize), "The fetch size must be positive.");
            }
            if (_isPrepared || ParameterValues?.Length > 0 || _parameterCollection.Count > 0)
            {
                throw new NotSupportedException("Use the named Prism parameter overload to bind Cypher parameters.");
            }

            int? statementId = null;
            try
            {
                if (parameters != null)
                {
                    var bindings = new NamedParameters();
                    foreach (var parameter in parameters)
                    {
                        if (string.IsNullOrEmpty(parameter.Key) || parameter.Value == null
                            || parameter.Value.ValueCase == ProtoValue.ValueOneofCase.None)
                        {
                            throw new ArgumentException("Each Cypher parameter needs a name and a Prism value.", nameof(parameters));
                        }
                        bindings.Parameters.Add(parameter.Key, parameter.Value);
                    }
                    var prepare = new Request
                    {
                        PrepareNamedStatementRequest = new PrepareStatementRequest
                        {
                            LanguageName = "cypher",
                            NamespaceName = namespaceName,
                            Statement = statement
                        }
                    };
                    var signature = await executionConnection.SendRecv(prepare).ConfigureAwait(false);
                    PolyphenyCypherResult.ValidateResponse(signature, prepare.Id);
                    if (!PrismStatement.IsCurrentSession(executionConnection, sessionVersion)
                        || !signature.Last || signature.PreparedStatementSignature?.StatementId is not > 0)
                    {
                        throw new InvalidOperationException("Expected a prepared Cypher statement signature.");
                    }
                    statementId = signature.PreparedStatementSignature.StatementId;

                    var execute = new Request
                    {
                        ExecuteNamedStatementRequest = new ExecuteNamedStatementRequest
                        {
                            StatementId = statementId.Value,
                            Parameters = bindings,
                            FetchSize = fetchSize
                        }
                    };
                    var prepared = await executionConnection.SendRecv(execute).ConfigureAwait(false);
                    PolyphenyCypherResult.ValidateResponse(prepared, execute.Id);
                    if (!PrismStatement.IsCurrentSession(executionConnection, sessionVersion)
                        || !prepared.Last || prepared.StatementResult == null)
                    {
                        throw new InvalidOperationException("Expected a prepared Cypher statement result.");
                    }
                    return new PolyphenyCypherResult(executionConnection, statementId.Value, fetchSize, prepared.StatementResult, sessionVersion);
                }

                var request = new Request
                {
                    ExecuteUnparameterizedStatementRequest = new ExecuteUnparameterizedStatementRequest
                    {
                        LanguageName = "cypher",
                        Statement = statement,
                        NamespaceName = namespaceName,
                        FetchSize = fetchSize
                    }
                };
                var acknowledgment = await executionConnection.SendRecv(request).ConfigureAwait(false);
                if (!PrismStatement.IsCurrentSession(executionConnection, sessionVersion))
                {
                    throw new InvalidOperationException("The connection session ended during Cypher execution.");
                }
                PolyphenyCypherResult.ValidateResponse(acknowledgment, request.Id);
                if (acknowledgment.Id != request.Id || acknowledgment.StatementResponse == null)
                {
                    throw new InvalidOperationException("Expected a Cypher statement acknowledgment.");
                }
                statementId = acknowledgment.StatementResponse.StatementId;
                if (acknowledgment.Last || acknowledgment.StatementResponse.Result != null)
                {
                    throw new InvalidOperationException("Expected an intermediate Cypher statement acknowledgment.");
                }

                var response = await executionConnection.Receive().ConfigureAwait(false);
                if (!PrismStatement.IsCurrentSession(executionConnection, sessionVersion))
                {
                    throw new InvalidOperationException("The connection session ended during Cypher execution.");
                }
                PolyphenyCypherResult.ValidateResponse(response, request.Id);
                if (response.Id != acknowledgment.Id || !response.Last
                    || response.StatementResponse?.StatementId != statementId)
                {
                    throw new InvalidOperationException("The Cypher result does not match its statement acknowledgment.");
                }
                return new PolyphenyCypherResult(executionConnection, statementId.Value, fetchSize, response.StatementResponse.Result, sessionVersion);
            }
            catch
            {
                if (statementId.HasValue && PrismStatement.IsCurrentSession(executionConnection, sessionVersion))
                {
                    await PrismStatement.TryCloseAsync(executionConnection.SendRecv, statementId.Value).ConfigureAwait(false);
                }
                throw;
            }
        }

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

        public Dictionary<object, object>[] ExecuteQueryMongo() => ExecuteQueryMongo(null);

        public Dictionary<object, object>[] ExecuteQueryMongo(string namespaceName)
            => ExecuteQueryMongoAsync(namespaceName).GetAwaiter().GetResult();

        private async Task<Dictionary<object, object>[]> ExecuteQueryMongoAsync(string namespaceName)
        {
            if (connection == null)
            {
                throw new InvalidOperationException("Connection property must be non-null");
            }

            var execute = new ExecuteUnparameterizedStatementRequest
            {
                LanguageName = "mongo",
                Statement = CommandText
            };
            if (namespaceName != null)
            {
                execute.NamespaceName = namespaceName;
            }

            var request = new Request { ExecuteUnparameterizedStatementRequest = execute };
            var response = await ExecuteUnparameterizedAsync(request, "StatementId mismatch").ConfigureAwait(false);
            var documentFrame = response?.StatementResponse?.Result?.Frame?.DocumentFrame;
            if (documentFrame == null)
            {
                throw new Exception("Query should return document data, however the result is empty");
            }

            return documentFrame.Documents.Select(ProtoHelper.FromProtoDocument).ToArray();
        }
    }
}
