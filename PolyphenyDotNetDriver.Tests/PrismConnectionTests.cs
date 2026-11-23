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

using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Google.Protobuf;
using Polypheny.Prism;

namespace PolyphenyDotNetDriver.Tests;

public class PrismConnectionTests
{
    [Test]
    public async Task SearchEntities_ReadsFragmentedDocumentAndTableMetadata()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var expectedJson = "{\"notes\":\"" + new string('x', 8192) + "\"}";
        var server = RunServer(listener, 2, true, async (stream, cancellationToken) =>
        {
            var documentRequest = await ReadRequest(stream, cancellationToken);
            Assert.That(documentRequest.EntitiesRequest.NamespaceName, Is.EqualTo("patients"));
            Assert.That(documentRequest.EntitiesRequest.HasEntityPattern, Is.False);
            await WriteResponse(stream, new Response
            {
                Id = documentRequest.Id,
                Last = true,
                EntitiesResponse = new EntitiesResponse
                {
                    Entities =
                    {
                        new Entity
                        {
                            Document = new Document
                            {
                                NamespaceName = "patients",
                                CollectionName = "symptoms",
                                Fields =
                                {
                                    new DocumentField { FieldPath = "patient_id", TypeName = "string" },
                                    new DocumentField { FieldPath = "complications.renal", TypeName = "bool", IsOptional = true }
                                },
                                SampleDocumentJson = expectedJson,
                                DocumentIndexes =
                                {
                                    new DocumentIndex { FieldPath = "patient_id", IndexType = "btree", Unique = true }
                                }
                            }
                        },
                        new Entity { Document = new Document { NamespaceName = "patients", CollectionName = "empty" } }
                    }
                }
            }, cancellationToken, fragment: true);

            var tableRequest = await ReadRequest(stream, cancellationToken);
            Assert.That(tableRequest.EntitiesRequest.NamespaceName, Is.EqualTo("public"));
            Assert.That(tableRequest.EntitiesRequest.EntityPattern, Is.EqualTo("patients%"));
            await WriteResponse(stream, new Response
            {
                Id = tableRequest.Id,
                Last = true,
                EntitiesResponse = new EntitiesResponse
                {
                    Entities = { new Entity { Table = new Table { NamespaceName = "public", TableName = "patients" } } }
                }
            }, cancellationToken);
        }, timeout.Token);

        using var connection = OpenableConnection(listener);
        await connection.OpenAsync(timeout.Token);
        var entities = await connection.SearchEntitiesAsync("patients");
        var document = entities[0].Document;
        Assert.That(document.NamespaceName, Is.EqualTo("patients"));
        Assert.That(document.CollectionName, Is.EqualTo("symptoms"));
        Assert.That(document.Fields.Select(field => (field.FieldPath, field.TypeName, field.IsOptional)),
            Is.EqualTo(new[] { ("patient_id", "string", false), ("complications.renal", "bool", true) }));
        Assert.That(document.HasSampleDocumentJson, Is.True);
        Assert.That(document.SampleDocumentJson, Is.EqualTo(expectedJson));
        Assert.That(document.DocumentIndexes.Select(index => (index.FieldPath, index.IndexType, index.Unique)),
            Is.EqualTo(new[] { ("patient_id", "btree", true) }));
        Assert.That(entities[1].Document.CollectionName, Is.EqualTo("empty"));
        Assert.That(entities[1].Document.HasSampleDocumentJson, Is.False);
        Assert.That(entities[1].Document.Fields, Is.Empty);
        Assert.That(entities[1].Document.DocumentIndexes, Is.Empty);

        var tables = connection.SearchEntities("public", "patients%");
        Assert.That(tables.Single().Table.TableName, Is.EqualTo("patients"));
        await connection.CloseAsync();
        await server;
    }

    [TestCase(false, null, null)]
    [TestCase(true, null, null)]
    [TestCase(true, "XX000", null)]
    [TestCase(true, null, 0)]
    [TestCase(true, "42P01", 7)]
    public async Task SearchEntities_ReportsPrismErrorWithOrWithoutDetails(bool hasDetails, string? state, int? serverCode)
    {
        const string message = "  Metadata unavailable. (SQLSTATE 23505)\n";
        var wireError = new ErrorResponse { Message = message };
        if (hasDetails)
        {
            wireError.Details = new ErrorDetails { Message = "Different details message" };
            if (state != null) wireError.Details.State = state;
            if (serverCode.HasValue) wireError.Details.ErrorCode = serverCode.Value;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = RunServer(listener, hasDetails ? 4 : 1, true, async (stream, cancellationToken) =>
        {
            var request = await ReadRequest(stream, cancellationToken);
            Assert.That(request.EntitiesRequest.NamespaceName, Is.EqualTo("patients"));
            await WriteResponse(stream, new Response
            {
                Id = request.Id,
                Last = true,
                ErrorResponse = wireError
            }, cancellationToken);
        }, timeout.Token);

        using var connection = OpenableConnection(listener);
        await connection.OpenAsync(timeout.Token);
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
        var error = Assert.ThrowsAsync<PolyphenyException>(() => connection.SearchEntitiesAsync("patients"));
        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Is.EqualTo(message));
            Assert.That(((DbException)error).SqlState, Is.EqualTo(state));
            Assert.That(error.ServerErrorCode, Is.EqualTo(serverCode));
        });
        await connection.CloseAsync();
        await server;
    }

    [Test]
    public async Task Open_RejectedHandshakeClosesTransport()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = RunServer(listener, 9, false, null, timeout.Token);

        using var connection = OpenableConnection(listener);
        Assert.ThrowsAsync<InvalidOperationException>(() => connection.OpenAsync(timeout.Token));
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
        await server;
    }

    [Test]
    public async Task Open_LoginErrorReportsPrismDetailsAndClosesTransport()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = RejectLogin();

        using var connection = OpenableConnection(listener);
        var error = Assert.ThrowsAsync<PolyphenyException>(() => connection.OpenAsync(timeout.Token));
        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Is.EqualTo("Authentication failed."));
            Assert.That(error.SqlState, Is.EqualTo("28000"));
            Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
        });
        await server;

        async Task RejectLogin()
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream = client.GetStream();
            var connect = await ReadLoginRequest(stream, timeout.Token);
            await WriteResponse(stream, new Response
            {
                Id = connect.Id,
                Last = true,
                ErrorResponse = new ErrorResponse
                {
                    Message = "Authentication failed.",
                    Details = new ErrorDetails { State = "28000" }
                }
            }, timeout.Token);
            Assert.That(await stream.ReadAsync(new byte[1], timeout.Token), Is.Zero);
        }
    }

    [TestCase(nameof(PolyphenyCommand.ExecuteNonQuery), "success")]
    [TestCase(nameof(PolyphenyCommand.ExecuteNonQuery), "error")]
    [TestCase(nameof(PolyphenyCommand.ExecuteNonQuery), "nonterminal")]
    [TestCase(nameof(PolyphenyCommand.ExecuteNonQuery), "wrong-type")]
    [TestCase(nameof(PolyphenyCommand.ExecuteReader), "success")]
    [TestCase(nameof(PolyphenyCommand.ExecuteReader), "error")]
    [TestCase(nameof(PolyphenyCommand.ExecuteQueryMongo), "success")]
    [TestCase(nameof(PolyphenyCommand.ExecuteQueryMongo), "error")]
    public async Task UnparameterizedExecution_ClosesAcknowledgedStatementAndPreservesPrimaryError(
        string operation, string cleanupReply)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var malformedCleanup = cleanupReply is "nonterminal" or "wrong-type";
        var server = RunServer(listener, 4, true, async (stream, cancellationToken) =>
        {
            var statement = await ReadRequest(stream, cancellationToken);
            var language = operation == nameof(PolyphenyCommand.ExecuteQueryMongo) ? "mongo" : "sql";
            Assert.That(statement.ExecuteUnparameterizedStatementRequest.LanguageName, Is.EqualTo(language));
            await WriteResponse(stream, new Response
            {
                Id = statement.Id,
                StatementResponse = new StatementResponse { StatementId = 7 }
            }, cancellationToken);
            await WriteResponse(stream, new Response
            {
                Id = statement.Id,
                Last = true,
                ErrorResponse = new ErrorResponse
                {
                    Message = "Duplicate key.",
                    Details = new ErrorDetails { State = "23505", ErrorCode = 0 }
                }
            }, cancellationToken);

            var close = await ReadRequest(stream, cancellationToken);
            Assert.That(close.CloseStatementRequest?.StatementId, Is.EqualTo(7));
            var closeResponse = new Response { Id = close.Id, Last = cleanupReply != "nonterminal" };
            if (cleanupReply == "error")
            {
                closeResponse.ErrorResponse = new ErrorResponse
                {
                    Message = "Could not close statement.",
                    Details = new ErrorDetails { State = "XX000", ErrorCode = 3 }
                };
            }
            else if (cleanupReply == "wrong-type")
            {
                closeResponse.ConnectionCheckResponse = new ConnectionCheckResponse();
            }
            else
            {
                closeResponse.CloseStatementResponse = new CloseStatementResponse();
            }
            await WriteResponse(stream, closeResponse, cancellationToken);
            if (malformedCleanup)
            {
                Assert.That(await stream.ReadAsync(new byte[1], cancellationToken), Is.Zero,
                    "The client must close the transport before it can send a rollback.");
                return;
            }

            var rollback = await ReadRequest(stream, cancellationToken);
            Assert.That(rollback.RollbackRequest, Is.Not.Null);
            await WriteResponse(stream, new Response
            {
                Id = rollback.Id, Last = true, RollbackResponse = new RollbackResponse()
            }, cancellationToken);

            var ping = await ReadRequest(stream, cancellationToken);
            Assert.That(ping.ConnectionCheckRequest, Is.Not.Null);
            await WriteResponse(stream, new Response
            {
                Id = ping.Id, Last = true, ConnectionCheckResponse = new ConnectionCheckResponse()
            }, cancellationToken);
        }, timeout.Token, expectDisconnect: !malformedCleanup);

        using var connection = OpenableConnection(listener);
        await connection.OpenAsync(timeout.Token);
        using var transaction = connection.BeginTransaction();
        using var command = new PolyphenyCommand().WithConnection(connection);
        TestDelegate execute = operation switch
        {
            nameof(PolyphenyCommand.ExecuteNonQuery) => () => command.WithCommandText("INSERT INTO patients VALUES (1)").ExecuteNonQuery(),
            nameof(PolyphenyCommand.ExecuteReader) => () => command.WithCommandText("SELECT * FROM patients").ExecuteReader(),
            nameof(PolyphenyCommand.ExecuteQueryMongo) => () => command.WithCommandText("db.patients.find()").ExecuteQueryMongo(),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };
        var error = Assert.Throws<PolyphenyException>(execute);
        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Is.EqualTo("Duplicate key."));
            Assert.That(error.SqlState, Is.EqualTo("23505"));
            Assert.That(error.ServerErrorCode, Is.Zero);
            Assert.That(connection.State, Is.EqualTo(malformedCleanup ? ConnectionState.Closed : ConnectionState.Open));
        });
        if (malformedCleanup)
        {
            Assert.ThrowsAsync<InvalidOperationException>(() => transaction.RollbackAsync(timeout.Token));
        }
        else
        {
            await transaction.RollbackAsync(timeout.Token);
            await connection.Ping(timeout.Token);
        }
        await connection.CloseAsync();
        await server;
    }

    [Test]
    public async Task SequentialSends_PreserveResponseOrderThroughAnIntermediateFrameAndServerError()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = RunServer(listener, 4, true, async (stream, cancellationToken) =>
        {
            var execute = await ReadRequest(stream, cancellationToken);
            var ping = await ReadRequest(stream, cancellationToken);
            Assert.That(execute.Id, Is.EqualTo(41));
            Assert.That(ping.Id, Is.EqualTo(42));
            await WriteResponse(stream, new Response
            {
                Id = execute.Id, StatementResponse = new StatementResponse { StatementId = 7 }
            }, cancellationToken);
            await WriteResponse(stream, new Response
            {
                Id = execute.Id,
                Last = true,
                ErrorResponse = new ErrorResponse
                {
                    Message = "Unknown table.",
                    Details = new ErrorDetails { State = "42P01" }
                }
            }, cancellationToken);
            await WriteResponse(stream, new Response
            {
                Id = ping.Id, Last = true, ConnectionCheckResponse = new ConnectionCheckResponse()
            }, cancellationToken);
        }, timeout.Token);

        using var connection = OpenableConnection(listener);
        await connection.OpenAsync(timeout.Token);
        await connection.Send(new Request
        {
            Id = 41,
            ExecuteUnparameterizedStatementRequest = new ExecuteUnparameterizedStatementRequest
            {
                LanguageName = "sql", Statement = "SELECT * FROM missing"
            }
        });
        await connection.Send(new Request { Id = 42, ConnectionCheckRequest = new ConnectionCheckRequest() });

        var acknowledgment = await connection.Receive();
        Assert.That(acknowledgment.Id, Is.EqualTo(41));
        Assert.That(acknowledgment.Last, Is.False);
        Assert.That(acknowledgment.StatementResponse?.StatementId, Is.EqualTo(7));
        var error = Assert.ThrowsAsync<PolyphenyException>(() => connection.Receive());
        Assert.That(error!.SqlState, Is.EqualTo("42P01"));
        var response = await connection.Receive();
        Assert.That(response.Id, Is.EqualTo(42));
        Assert.That(response.ConnectionCheckResponse, Is.Not.Null);
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
        await connection.CloseAsync();
        await server;
    }

    [Test]
    public async Task ReceiveStartedBeforeSend_LeavesNoPendingResponse()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = RunServer(listener, 4, true, async (stream, cancellationToken) =>
        {
            foreach (var expectedId in new ulong[] { 41, 42 })
            {
                var request = await ReadRequest(stream, cancellationToken);
                Assert.That(request.Id, Is.EqualTo(expectedId));
                Assert.That(request.ConnectionCheckRequest, Is.Not.Null);
                await WriteResponse(stream, new Response
                {
                    Id = request.Id, Last = true, ConnectionCheckResponse = new ConnectionCheckResponse()
                }, cancellationToken);
            }
        }, timeout.Token);

        using var connection = OpenableConnection(listener);
        await connection.OpenAsync(timeout.Token);
        var receiving = connection.Receive();
        Assert.That(receiving.IsCompleted, Is.False);
        await connection.Send(new Request { Id = 41, ConnectionCheckRequest = new ConnectionCheckRequest() });
        var first = await receiving.WaitAsync(timeout.Token);
        Assert.That(first.Id, Is.EqualTo(41));
        Assert.That(first.Last, Is.True);

        var second = await connection.SendRecv(new Request { Id = 42, ConnectionCheckRequest = new ConnectionCheckRequest() })
            .WaitAsync(timeout.Token);
        Assert.That(second.Id, Is.EqualTo(42));
        Assert.That(second.ConnectionCheckResponse, Is.Not.Null);
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
        await connection.CloseAsync();
        await server;
    }

    [Test]
    public async Task PreparedExecution_RetainsStatementForRetryAfterErrorAndRollback()
    {
        const string sql = "INSERT INTO patients VALUES (?)";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = RunServer(listener, 4, true, async (stream, cancellationToken) =>
        {
            var prepare = await ReadRequest(stream, cancellationToken);
            Assert.That(prepare.PrepareIndexedStatementRequest?.Statement, Is.EqualTo(sql));
            await WriteResponse(stream, new Response
            {
                Id = prepare.Id,
                Last = true,
                PreparedStatementSignature = new PreparedStatementSignature
                {
                    StatementId = 7,
                    ParameterMetas = { new ParameterMeta { Name = "id", TypeName = "INTEGER" } }
                }
            }, cancellationToken);

            var execute = await ReadRequest(stream, cancellationToken);
            Assert.That(execute.ExecuteIndexedStatementRequest?.StatementId, Is.EqualTo(7));
            Assert.That(execute.ExecuteIndexedStatementRequest!.Parameters.Parameters.Single().Integer.Integer, Is.EqualTo(1));
            await WriteResponse(stream, new Response
            {
                Id = execute.Id,
                Last = true,
                ErrorResponse = new ErrorResponse
                {
                    Message = "Duplicate key.",
                    Details = new ErrorDetails { State = "23505", ErrorCode = 0 }
                }
            }, cancellationToken);

            var rollback = await ReadRequest(stream, cancellationToken);
            Assert.That(rollback.RollbackRequest, Is.Not.Null);
            await WriteResponse(stream, new Response
            {
                Id = rollback.Id, Last = true, RollbackResponse = new RollbackResponse()
            }, cancellationToken);

            var retry = await ReadRequest(stream, cancellationToken);
            Assert.That(retry.ExecuteIndexedStatementRequest?.StatementId, Is.EqualTo(7));
            Assert.That(retry.ExecuteIndexedStatementRequest!.Parameters.Parameters.Single().Integer.Integer, Is.EqualTo(2));
            await WriteResponse(stream, new Response
            {
                Id = retry.Id, Last = true, StatementResult = new StatementResult { Scalar = 1 }
            }, cancellationToken);
        }, timeout.Token);

        using var connection = OpenableConnection(listener);
        await connection.OpenAsync(timeout.Token);
        using var transaction = connection.BeginTransaction();
        using var command = new PolyphenyCommand().WithConnection(connection).WithCommandText(sql)
            .WithParameterValues(new object[] { 1 });
        await command.PrepareAsync(timeout.Token);
        var error = Assert.ThrowsAsync<PolyphenyException>(() => command.ExecuteNonQueryAsync(timeout.Token));
        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Is.EqualTo("Duplicate key."));
            Assert.That(error.SqlState, Is.EqualTo("23505"));
            Assert.That(error.ServerErrorCode, Is.Zero);
        });
        await transaction.RollbackAsync(timeout.Token);
        command.WithParameterValues(new object[] { 2 });
        Assert.That(await command.ExecuteNonQueryAsync(timeout.Token), Is.EqualTo(1));
        await connection.CloseAsync();
        await server;
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task ExecuteNonQuery_RejectsMalformedErrorFrame(bool afterAcknowledgment, bool wrongResponseId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = RunServer(listener, 4, true, async (stream, cancellationToken) =>
        {
            var statement = await ReadRequest(stream, cancellationToken);
            Assert.That(statement.ExecuteUnparameterizedStatementRequest, Is.Not.Null);
            if (afterAcknowledgment)
            {
                await WriteResponse(stream, new Response
                {
                    Id = statement.Id,
                    StatementResponse = new StatementResponse { StatementId = 7 }
                }, cancellationToken);
            }
            await WriteResponse(stream, new Response
            {
                Id = wrongResponseId ? statement.Id + 1 : statement.Id,
                Last = wrongResponseId,
                ErrorResponse = new ErrorResponse
                {
                    Message = "Duplicate key.",
                    Details = new ErrorDetails { State = "23505", ErrorCode = 0 }
                }
            }, cancellationToken);
            Assert.That(await stream.ReadAsync(new byte[1], cancellationToken), Is.Zero);
        }, timeout.Token, expectDisconnect: false);

        using var connection = OpenableConnection(listener);
        await connection.OpenAsync(timeout.Token);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO patients VALUES (1)";
        Assert.ThrowsAsync<InvalidDataException>(() => command.ExecuteNonQueryAsync(timeout.Token));
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
        await server;
    }

    [TestCase("terminal")]
    [TestCase("wrong-id")]
    [TestCase("nonterminal")]
    public async Task Close_ClosesTransportWhenDisconnectReportsAServerError(string errorFrame)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = RunServer(listener, 4, true, async (stream, cancellationToken) =>
        {
            var disconnect = await ReadRequest(stream, cancellationToken);
            Assert.That(disconnect.DisconnectRequest, Is.Not.Null);
            await WriteResponse(stream, new Response
            {
                Id = errorFrame == "wrong-id" ? disconnect.Id + 1 : disconnect.Id,
                Last = errorFrame != "nonterminal",
                ErrorResponse = new ErrorResponse { Message = "Session already closed." }
            }, cancellationToken);
            Assert.That(await stream.ReadAsync(new byte[1], cancellationToken), Is.Zero);
        }, timeout.Token, expectDisconnect: false);

        using var connection = OpenableConnection(listener);
        await connection.OpenAsync(timeout.Token);
        if (errorFrame == "terminal")
        {
            connection.Close();
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => connection.Close());
        }
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
        await server;
    }

    [Test]
    public async Task Close_WithPendingRequestClosesTransportWithoutSendingDisconnect()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = RunServer(listener, 4, true, async (stream, cancellationToken) =>
        {
            var pending = await ReadRequest(stream, cancellationToken);
            Assert.That(pending.ConnectionCheckRequest, Is.Not.Null);
            Assert.That(await stream.ReadAsync(new byte[1], cancellationToken), Is.Zero);
        }, timeout.Token, expectDisconnect: false);

        using var connection = OpenableConnection(listener);
        await connection.OpenAsync(timeout.Token);
        await connection.Send(new Request { Id = 43, ConnectionCheckRequest = new ConnectionCheckRequest() });
        await connection.CloseAsync();
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
        await server;
    }

    [Test]
    public async Task CypherResultCanBeDisposedAfterItsConnection()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = RunServer(listener, 3, true, async (stream, cancellationToken) =>
        {
            await RespondWithCypherGraphResult(stream, isLast: false, cancellationToken);
            Assert.That(await stream.ReadAsync(new byte[1], cancellationToken), Is.Zero);
        }, timeout.Token, expectDisconnect: false);

        using var connection = OpenableConnection(listener);
        await connection.OpenAsync(timeout.Token);
        var result = await new PolyphenyCommand().WithConnection(connection).WithCommandText("MATCH (n) RETURN n")
            .ExecuteCypherAsync("patients", 1);
        connection.Dispose();
        await result.DisposeAsync();
        Assert.That(result.IsClosed, Is.True);
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
        await server;
    }

    [Test]
    public async Task CypherResultDispose_ClosesTransportOnNonterminalStatementCloseReply()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = RunServer(listener, 4, true, async (stream, cancellationToken) =>
        {
            await RespondWithCypherGraphResult(stream, isLast: false, cancellationToken);
            var close = await ReadRequest(stream, cancellationToken);
            Assert.That(close.CloseStatementRequest?.StatementId, Is.EqualTo(7));
            await WriteResponse(stream, new Response
            {
                Id = close.Id, CloseStatementResponse = new CloseStatementResponse()
            }, cancellationToken);
            Assert.That(await stream.ReadAsync(new byte[1], cancellationToken), Is.Zero);
        }, timeout.Token, expectDisconnect: false);

        using var connection = OpenableConnection(listener);
        await connection.OpenAsync(timeout.Token);
        await using var result = await new PolyphenyCommand().WithConnection(connection).WithCommandText("MATCH (n) RETURN n")
            .ExecuteCypherAsync("patients", 1);
        Assert.ThrowsAsync<InvalidDataException>(() => result.DisposeAsync().AsTask());
        Assert.That(result.IsClosed, Is.True);
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
        await server;
    }

    [Test]
    public async Task TruncatedCypherPageRetainsTheTransportFailureAndClosesTheResult()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = RunServer(listener, 3, true, async (stream, cancellationToken) =>
        {
            await RespondWithCypherGraphResult(stream, isLast: false, cancellationToken);
            var fetch = await ReadRequest(stream, cancellationToken);
            Assert.That(fetch.FetchRequest.StatementId, Is.EqualTo(7));
            var header = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(header, 100);
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(new byte[3], cancellationToken);
        }, timeout.Token, expectDisconnect: false);

        using var connection = OpenableConnection(listener);
        await connection.OpenAsync(timeout.Token);
        await using var result = await new PolyphenyCommand().WithConnection(connection).WithCommandText("MATCH (n) RETURN n")
            .ExecuteCypherAsync("patients", 1);
        Assert.ThrowsAsync<EndOfStreamException>(() => result.FetchNextAsync());
        Assert.That(result.IsClosed, Is.True);
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
        await server;
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OldResultCannotUseAReopenedConnectionSession(bool attemptFetch)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var firstSession = RunServer(listener, 3, true, async (stream, cancellationToken) =>
        {
            await RespondWithCypherGraphResult(stream, isLast: false, cancellationToken);
        }, timeout.Token);

        using var connection = OpenableConnection(listener);
        await connection.OpenAsync(timeout.Token);
        var command = new PolyphenyCommand().WithConnection(connection).WithCommandText("MATCH (n) RETURN n");
        var oldResult = await command.ExecuteCypherAsync("patients", 1);
        await connection.CloseAsync();
        await firstSession;

        var secondSession = RunServer(listener, 3, true, async (stream, cancellationToken) =>
        {
            await RespondWithCypherGraphResult(stream, isLast: true, cancellationToken);
            var close = await ReadRequest(stream, cancellationToken);
            Assert.That(close.CloseStatementRequest.StatementId, Is.EqualTo(7));
            await WriteResponse(stream, new Response { Id = close.Id, Last = true, CloseStatementResponse = new CloseStatementResponse() },
                cancellationToken);
        }, timeout.Token);

        await connection.OpenAsync(timeout.Token);
        if (attemptFetch)
        {
            var error = Assert.ThrowsAsync<InvalidOperationException>(() => oldResult.FetchNextAsync());
            Assert.That(error!.Message, Does.Contain("session"));
        }
        await oldResult.DisposeAsync();
        Assert.That(oldResult.IsClosed, Is.True);
        await using (var current = await command.ExecuteCypherAsync("patients", 1))
        {
            Assert.That(current.IsLast, Is.True);
        }
        await connection.CloseAsync();
        await secondSession;
    }

    private static async Task RespondWithCypherGraphResult(Stream stream, bool isLast, CancellationToken cancellationToken)
    {
        var execute = await ReadRequest(stream, cancellationToken);
        Assert.That(execute.ExecuteUnparameterizedStatementRequest?.LanguageName, Is.EqualTo("cypher"));
        await WriteResponse(stream, new Response
        {
            Id = execute.Id, StatementResponse = new StatementResponse { StatementId = 7 }
        }, cancellationToken);
        await WriteResponse(stream, new Response
        {
            Id = execute.Id, Last = true,
            StatementResponse = new StatementResponse
            {
                StatementId = 7,
                Result = new StatementResult { Frame = new Frame { IsLast = isLast, GraphFrame = new GraphFrame() } }
            }
        }, cancellationToken);
    }

    private static PolyphenyConnection OpenableConnection(TcpListener listener)
        => PolyphenyDriver.OpenConnection($"127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port},pa:");

    private static async Task RunServer(
        TcpListener listener, int serverMinor, bool compatible, Func<NetworkStream, CancellationToken, Task>? exchange,
        CancellationToken cancellationToken, bool expectDisconnect = true)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        client.NoDelay = true;
        using var stream = client.GetStream();
        var connect = await ReadLoginRequest(stream, cancellationToken);
        await WriteResponse(stream, new Response
        {
            Id = connect.Id,
            Last = true,
            ConnectionResponse = new ConnectionResponse
            {
                MajorApiVersion = compatible ? 2 : 1,
                MinorApiVersion = serverMinor,
                IsCompatible = compatible
            }
        }, cancellationToken);

        if (!compatible)
        {
            var remaining = await stream.ReadAsync(new byte[1], cancellationToken);
            Assert.That(remaining, Is.Zero);
            return;
        }

        await exchange!(stream, cancellationToken);
        if (!expectDisconnect)
        {
            return;
        }
        var disconnect = await ReadRequest(stream, cancellationToken);
        Assert.That(disconnect.DisconnectRequest, Is.Not.Null);
        await WriteResponse(stream, new Response { Id = disconnect.Id, Last = true, DisconnectResponse = new DisconnectResponse() },
            cancellationToken);
    }

    private static async Task<Request> ReadLoginRequest(NetworkStream stream, CancellationToken cancellationToken)
    {
        var transport = Encoding.ASCII.GetBytes("plain-v1@polypheny.com\n");
        await stream.WriteAsync(new[] { (byte)transport.Length }, cancellationToken);
        await stream.WriteAsync(transport, cancellationToken);
        var handshake = new byte[transport.Length + 1];
        await stream.ReadExactlyAsync(handshake, cancellationToken);
        Assert.That(handshake[0], Is.EqualTo(transport.Length));
        Assert.That(handshake.Skip(1), Is.EqualTo(transport));

        var connect = await ReadRequest(stream, cancellationToken);
        Assert.That(connect.ConnectionRequest.MajorApiVersion, Is.EqualTo(2));
        Assert.That(connect.ConnectionRequest.MinorApiVersion, Is.EqualTo(4));
        return connect;
    }

    private static async Task<Request> ReadRequest(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[8];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var body = new byte[checked((int)BinaryPrimitives.ReadUInt64LittleEndian(header))];
        await stream.ReadExactlyAsync(body, cancellationToken);
        return Request.Parser.ParseFrom(body);
    }

    private static async Task WriteResponse(Stream stream, Response response, CancellationToken cancellationToken, bool fragment = false)
    {
        var body = response.ToByteArray();
        var header = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(header, (ulong)body.Length);
        if (fragment)
        {
            // Separate the initial header and payload bytes so the client must finish both reads.
            await stream.WriteAsync(header.AsMemory(0, 3), cancellationToken);
            await Task.Delay(30, cancellationToken);
            await stream.WriteAsync(header.AsMemory(3), cancellationToken);
            await stream.WriteAsync(body.AsMemory(0, 10), cancellationToken);
            await Task.Delay(30, cancellationToken);
            await stream.WriteAsync(body.AsMemory(10), cancellationToken);
            return;
        }
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
    }
}
