/*
 * Copyright 2019-2026 The Polypheny Project
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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Polypheny.Prism;
using PolyphenyDotNetDriver.Interface;

namespace PolyphenyDotNetDriver;

/// <summary>The result format selected by the server for a Cypher statement.</summary>
public enum CypherResultKind
{
    Scalar,
    Rows,
    Graph
}

/// <summary>A paged Cypher result. Dispose it before executing another query on the same connection.</summary>
public sealed class PolyphenyCypherResult : IDisposable, IAsyncDisposable
{
    private readonly IPolyphenyConnection _connection;
    private readonly int _statementId;
    private readonly int _fetchSize;
    private readonly long? _sessionVersion;
    private readonly SemaphoreSlim _operation = new(1, 1);

    public CypherResultKind Kind { get; }
    public long? RowsAffected { get; }
    public bool IsLast { get; private set; }
    public bool IsClosed { get; private set; }
    public IReadOnlyList<ColumnMeta> Columns { get; private set; } = Array.Empty<ColumnMeta>();
    public IReadOnlyList<IReadOnlyList<object>> Rows { get; private set; } = Array.Empty<IReadOnlyList<object>>();
    public IReadOnlyList<GraphElement> Elements { get; private set; } = Array.Empty<GraphElement>();

    internal PolyphenyCypherResult(IPolyphenyConnection connection, int statementId, int fetchSize, StatementResult result, long? sessionVersion)
    {
        _connection = connection;
        _statementId = statementId;
        _fetchSize = fetchSize;
        _sessionVersion = sessionVersion;
        if (result == null)
        {
            throw new InvalidDataException("The Cypher response has no result.");
        }
        if (result.Frame == null)
        {
            Kind = CypherResultKind.Scalar;
            RowsAffected = result.Scalar;
            IsLast = true;
        }
        else
        {
            Kind = result.Frame.ResultCase switch
            {
                Frame.ResultOneofCase.RelationalFrame => CypherResultKind.Rows,
                Frame.ResultOneofCase.GraphFrame => CypherResultKind.Graph,
                _ => throw new InvalidDataException("Expected a relational or graph frame for Cypher.")
            };
            ApplyFrame(result.Frame);
        }
    }

    /// <summary>Fetches the next page, or returns false after the last page or after disposal.</summary>
    public bool FetchNext() => FetchNextAsync().GetAwaiter().GetResult();

    public async Task<bool> FetchNextAsync()
    {
        await _operation.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsClosed || IsLast)
            {
                return false;
            }
            if (!PrismStatement.IsCurrentSession(_connection, _sessionVersion))
            {
                throw new InvalidOperationException("The connection session that owns this Cypher result has ended.");
            }
            var request = new Request
            {
                FetchRequest = new FetchRequest { StatementId = _statementId, FetchSize = _fetchSize }
            };
            var response = await _connection.SendRecv(request).ConfigureAwait(false);
            if (!PrismStatement.IsCurrentSession(_connection, _sessionVersion))
            {
                throw new InvalidOperationException("The connection session that owns this Cypher result has ended.");
            }
            ValidateResponse(response, request.Id);
            if (response.Id != request.Id || response.Frame == null || !response.Last)
            {
                throw new InvalidDataException("Expected the final response to a Cypher fetch request.");
            }
            ApplyFrame(response.Frame);
            return true;
        }
        catch
        {
            await CloseAfterError().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _operation.Release();
        }
    }

    private void ApplyFrame(Frame frame)
    {
        switch (Kind)
        {
            case CypherResultKind.Rows when frame.RelationalFrame != null:
                if (frame.RelationalFrame.Rows.Any(row => row.Values.Count != frame.RelationalFrame.ColumnMeta.Count))
                {
                    throw new InvalidDataException("A Cypher row does not match its column metadata.");
                }
                Columns = Array.AsReadOnly(frame.RelationalFrame.ColumnMeta.Select(column => column.Clone()).ToArray());
                Rows = Array.AsReadOnly(frame.RelationalFrame.Rows
                    .Select(row => (IReadOnlyList<object>)Array.AsReadOnly(row.Values.Select(ProtoHelper.FromProtoValue).ToArray()))
                    .ToArray());
                break;
            case CypherResultKind.Graph when frame.GraphFrame != null:
                Elements = Array.AsReadOnly(frame.GraphFrame.Element.Select(element => element.ElementCase switch
                {
                    Polypheny.Prism.GraphElement.ElementOneofCase.Node => (GraphElement)new GraphNode(element.Node),
                    Polypheny.Prism.GraphElement.ElementOneofCase.Edge => new GraphEdge(element.Edge),
                    _ => throw new InvalidDataException("A graph element has no node or edge.")
                }).ToArray());
                break;
            default:
                throw new InvalidDataException("The Cypher result format changed between pages.");
        }
        IsLast = frame.IsLast;
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        await _operation.WaitAsync().ConfigureAwait(false);
        try
        {
            await CloseStatement().ConfigureAwait(false);
        }
        finally
        {
            _operation.Release();
        }
    }

    private async Task CloseStatement()
    {
        if (IsClosed)
        {
            return;
        }
        IsClosed = true;
        if (!PrismStatement.IsCurrentSession(_connection, _sessionVersion)
            || _connection is DbConnection connection && connection.State == ConnectionState.Closed)
        {
            return;
        }
        await PrismStatement.CloseAsync(_connection.SendRecv, _statementId).ConfigureAwait(false);
    }

    private async Task CloseAfterError()
    {
        try
        {
            await CloseStatement().ConfigureAwait(false);
        }
        catch
        {
            // Keep the execution or decoding error when statement cleanup also fails.
        }
    }

    internal static void ValidateResponse(Response response, ulong requestId)
    {
        if (response == null)
        {
            throw new InvalidDataException("The server returned no Prism response.");
        }
        PrismResponse.ThrowIfError(response, requestId);
    }
}
