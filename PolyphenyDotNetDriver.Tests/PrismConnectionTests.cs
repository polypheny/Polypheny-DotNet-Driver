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

    [Test]
    public async Task SearchEntities_UsesCompatibleOlderMinorAndReportsPrismError()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = RunServer(listener, 1, true, async (stream, cancellationToken) =>
        {
            var request = await ReadRequest(stream, cancellationToken);
            await WriteResponse(stream, new Response
            {
                Id = request.Id,
                Last = true,
                ErrorResponse = new ErrorResponse { Message = "Document collection metadata is unavailable." }
            }, cancellationToken);
        }, timeout.Token);

        using var connection = OpenableConnection(listener);
        await connection.OpenAsync(timeout.Token);
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => connection.SearchEntitiesAsync("patients"));
        Assert.That(error!.Message, Is.EqualTo("Document collection metadata is unavailable."));
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

    private static PolyphenyConnection OpenableConnection(TcpListener listener)
        => PolyphenyDriver.OpenConnection($"127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port},pa:");

    private static async Task RunServer(
        TcpListener listener, int serverMinor, bool compatible, Func<NetworkStream, CancellationToken, Task>? exchange,
        CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        client.NoDelay = true;
        using var stream = client.GetStream();
        var transport = Encoding.ASCII.GetBytes("plain-v1@polypheny.com\n");
        await stream.WriteAsync(new[] { (byte)transport.Length }, cancellationToken);
        await stream.WriteAsync(transport, cancellationToken);
        var handshake = new byte[transport.Length + 1];
        await stream.ReadExactlyAsync(handshake, cancellationToken);
        Assert.That(handshake[0], Is.EqualTo(transport.Length));
        Assert.That(handshake.Skip(1), Is.EqualTo(transport));

        var connect = await ReadRequest(stream, cancellationToken);
        Assert.That(connect.ConnectionRequest.MajorApiVersion, Is.EqualTo(2));
        Assert.That(connect.ConnectionRequest.MinorApiVersion, Is.EqualTo(2));
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
        var disconnect = await ReadRequest(stream, cancellationToken);
        Assert.That(disconnect.DisconnectRequest, Is.Not.Null);
        await WriteResponse(stream, new Response { Id = disconnect.Id, Last = true, DisconnectResponse = new DisconnectResponse() },
            cancellationToken);
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
