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

using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Google.Protobuf;
using Moq;
using Polypheny.Prism;
using PolyphenyDotNetDriver.Interface;
using ProtoGraphElement = Polypheny.Prism.GraphElement;

namespace PolyphenyDotNetDriver.Tests;

public class CypherTests
{
    [Test]
    public void DecodesEveryPrimitiveCaseInsideGraphProperties()
    {
        var bytes = ByteString.CopyFrom(new byte[] { 0, 128, 255 });
        var wire = Node("types", "Typed");
        wire.Properties.Add("binary", new ProtoValue { Binary = new ProtoBinary { Binary = bytes } });
        wire.Properties.Add("file", new ProtoValue { File = new ProtoFile { Binary = bytes } });
        wire.Properties.Add("date", new ProtoValue { Date = new ProtoDate { Date = -1 } });
        wire.Properties.Add("time", new ProtoValue { Time = new ProtoTime { Time = 86_399_999 } });
        wire.Properties.Add("timestamp", new ProtoValue { Timestamp = new ProtoTimestamp { Timestamp = -1 } });
        wire.Properties.Add("interval", new ProtoValue { Interval = new ProtoInterval { Months = -13, Milliseconds = 86_400_001 } });
        wire.Properties.Add("decimal", new ProtoValue
        {
            BigDecimal = new ProtoBigDecimal { Scale = 2, UnscaledValue = ByteString.CopyFrom(new byte[] { 0x80 }) }
        });
        var node = (GraphNode)ProtoHelper.FromProtoValue(ProtoValue.Parser.ParseFrom(new ProtoValue { Node = wire }.ToByteArray()));
        Assert.Multiple(() =>
        {
            Assert.That(node.Properties["binary"], Is.EqualTo(new byte[] { 0, 128, 255 }));
            Assert.That(node.Properties["file"], Is.EqualTo(new byte[] { 0, 128, 255 }));
            Assert.That(node.Properties["date"], Is.EqualTo(DateTime.UnixEpoch.AddDays(-1)));
            Assert.That(((DateTime)node.Properties["date"]).Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(node.Properties["time"], Is.EqualTo(TimeSpan.FromMilliseconds(86_399_999)));
            Assert.That(node.Properties["timestamp"], Is.EqualTo(DateTime.UnixEpoch.AddMilliseconds(-1)));
            Assert.That(((DateTime)node.Properties["timestamp"]).Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(node.Properties["interval"], Is.EqualTo(new PolyphenyInterval(-13, 86_400_001)));
            Assert.That(node.Properties["decimal"], Is.EqualTo(new PolyphenyDecimal(-128, 2)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PendingExecutionKeepsItsOriginalConnectionWhenTheCommandIsReused(bool fail)
    {
        var first = new Mock<IPolyphenyConnection>(MockBehavior.Strict);
        var other = new Mock<IPolyphenyConnection>(MockBehavior.Strict);
        var acknowledgment = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Setup(connection => connection.SendRecv(It.Is<Request>(request => request.ExecuteUnparameterizedStatementRequest != null)))
            .Returns(acknowledgment.Task);
        first.Setup(connection => connection.SendRecv(It.Is<Request>(request => request.CloseStatementRequest != null)))
            .ReturnsAsync(Closed());
        first.Setup(connection => connection.Receive(8)).ReturnsAsync(fail
            ? new Response { Last = true, ErrorResponse = new ErrorResponse { Message = "Invalid Cypher" } }
            : Result(new StatementResult { Scalar = 1 }));
        var command = new PolyphenyCommand().WithConnection(first.Object).WithCommandText("CREATE (:Person)");
        var executing = command.ExecuteCypherAsync("patients");
        command.WithConnection(other.Object).WithCommandText("MATCH (n) RETURN n");
        acknowledgment.SetResult(Acknowledgment());
        if (fail)
        {
            var error = Assert.ThrowsAsync<PolyphenyException>(async () => await executing);
            Assert.That(error!.Message, Is.EqualTo("Invalid Cypher"));
        }
        else
        {
            await using var result = await executing;
            Assert.That(result.RowsAffected, Is.EqualTo(1));
        }
        first.Verify(connection => connection.Receive(8), Times.Once);
        first.Verify(connection => connection.SendRecv(It.Is<Request>(request => request.CloseStatementRequest != null)), Times.Once);
        other.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ConcurrentFetchAndDisposalSerializeTheResultRequests()
    {
        var mock = new Mock<IPolyphenyConnection>(MockBehavior.Strict);
        var page = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);
        mock.SetupSequence(connection => connection.SendRecv(It.IsAny<Request>()))
            .ReturnsAsync(Acknowledgment()).Returns(page.Task).ReturnsAsync(Closed());
        mock.Setup(connection => connection.Receive(8)).ReturnsAsync(Result(new StatementResult
        {
            Frame = new Frame { IsLast = false, GraphFrame = new GraphFrame() }
        }));
        var result = await new PolyphenyCommand().WithConnection(mock.Object).WithCommandText("MATCH (n) RETURN n")
            .ExecuteCypherAsync("patients");
        var fetching = result.FetchNextAsync();
        var secondFetch = result.FetchNextAsync();
        var disposing = result.DisposeAsync().AsTask();
        Assert.That(disposing.IsCompleted, Is.False);
        mock.Verify(connection => connection.SendRecv(It.Is<Request>(request => request.CloseStatementRequest != null)), Times.Never);
        page.SetResult(new Response { Last = true, Frame = new Frame { IsLast = true, GraphFrame = new GraphFrame() } });
        Assert.That(await fetching, Is.True);
        Assert.That(await secondFetch, Is.False);
        await disposing;
        await result.DisposeAsync();
        Assert.That(result.IsClosed, Is.True);
        mock.Verify(connection => connection.SendRecv(It.Is<Request>(request => request.FetchRequest != null)), Times.Once);
        mock.Verify(connection => connection.SendRecv(It.Is<Request>(request => request.CloseStatementRequest != null)), Times.Once);
    }

    [TestCase("123456789012345678901234567890123456789", 30, "123456789012345678901234567890123456789E-30")]
    [TestCase("-129", 2, "-129E-2")]
    [TestCase("128", -2, "128E2")]
    [TestCase("0", 0, "0")]
    [TestCase("1", int.MinValue, "1E2147483648")]
    public void ExactDecimalsPreserveSignedUnscaledValueAndScale(string unscaled, int scale, string formatted)
    {
        var integer = BigInteger.Parse(unscaled);
        var value = new ProtoValue
        {
            BigDecimal = new ProtoBigDecimal
            {
                Scale = scale,
                UnscaledValue = ByteString.CopyFrom(integer.ToByteArray(isUnsigned: false, isBigEndian: true))
            }
        };
        var decoded = (PolyphenyDecimal)ProtoHelper.FromProtoValue(ProtoValue.Parser.ParseFrom(value.ToByteArray()));
        Assert.That(decoded.UnscaledValue, Is.EqualTo(integer));
        Assert.That(decoded.Scale, Is.EqualTo(scale));
        Assert.That(decoded.ToString(), Is.EqualTo(formatted));
    }

    private static ProtoNode Node(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Labels = { "Person", "Patient" },
        Properties =
        {
            { "age", new ProtoValue { Integer = new ProtoInteger { Integer = 42 } } },
            { "name", new ProtoValue { String = new ProtoString { String = name } } },
            { "null", new ProtoValue { Null = new ProtoNull() } },
            { "active", new ProtoValue { Boolean = new ProtoBoolean { Boolean = true } } },
            {
                "measurements", new ProtoValue
                {
                    List = new ProtoList
                    {
                        Values =
                        {
                            new ProtoValue { Long = new ProtoLong { Long = 1L << 40 } },
                            new ProtoValue { Double = new ProtoDouble { Double = 2.5 } },
                            new ProtoValue { Null = new ProtoNull() }
                        }
                    }
                }
            },
            {
                "nested", new ProtoValue
                {
                    Document = new ProtoDocument
                    {
                        Entries = { { "score", new ProtoValue { Float = new ProtoFloat { Float = 1.25f } } } }
                    }
                }
            }
        }
    };

    private static ProtoEdge Edge() => new()
    {
        Id = "e1",
        Name = "relationship",
        Left = "alice",
        Right = "bob",
        Direction = ProtoEdge.Types.Direction.RightToLeft,
        Labels = { "KNOWS" },
        Properties = { { "since", new ProtoValue { Integer = new ProtoInteger { Integer = 2020 } } } }
    };

    [Test]
    public void ConvertsEveryGraphValueAndRetainsTraversalOrder()
    {
        var alice = Node("alice", "Alice");
        var bob = Node("bob", "Bob");
        var edge = Edge();
        var node = (GraphNode)ProtoHelper.FromProtoValue(ProtoValue.Parser.ParseFrom(new ProtoValue { Node = alice }.ToByteArray()));
        Assert.Multiple(() =>
        {
            Assert.That(node.Id, Is.EqualTo("alice"));
            Assert.That(node.Name, Is.EqualTo("Alice"));
            Assert.That(node.Labels, Is.EqualTo(new[] { "Person", "Patient" }));
            Assert.That(node.Properties["age"], Is.EqualTo(42));
            Assert.That(node.Properties["active"], Is.True);
            Assert.That(node.Properties["null"], Is.Null);
            Assert.That(node.Properties["measurements"], Is.EqualTo(new object?[] { 1L << 40, 2.5, null }));
            Assert.That(((Dictionary<object, object>)node.Properties["nested"])["score"], Is.EqualTo(1.25f));
        });

        var nativeEdge = (GraphEdge)ProtoHelper.FromProtoValue(new ProtoValue { Edge = edge });
        Assert.Multiple(() =>
        {
            Assert.That(nativeEdge.Id, Is.EqualTo("e1"));
            Assert.That(nativeEdge.Name, Is.EqualTo("relationship"));
            Assert.That(nativeEdge.Left, Is.EqualTo("alice"));
            Assert.That(nativeEdge.Right, Is.EqualTo("bob"));
            Assert.That(nativeEdge.Direction, Is.EqualTo(GraphDirection.RightToLeft));
            Assert.That(nativeEdge.Labels.Single(), Is.EqualTo("KNOWS"));
            Assert.That(nativeEdge.Properties["since"], Is.EqualTo(2020));
        });

        var path = (GraphPath)ProtoHelper.FromProtoValue(new ProtoValue
        {
            Path = new ProtoPath { Nodes = { bob, alice, bob }, Edges = { edge, edge } }
        });
        Assert.That(path.Nodes.Select(item => item.Id), Is.EqualTo(new[] { "bob", "alice", "bob" }));
        Assert.That(path.Edges.Select(item => (item.Left, item.Right, item.Direction)),
            Is.EqualTo(new[] { ("alice", "bob", GraphDirection.RightToLeft), ("alice", "bob", GraphDirection.RightToLeft) }));

        var graph = (GraphValue)ProtoHelper.FromProtoValue(new ProtoValue
        {
            Graph = new ProtoGraph { Nodes = { alice, bob, Node("isolated", "Isolated") }, Edges = { edge } }
        });
        Assert.That(graph.Nodes.Select(item => item.Id), Is.EqualTo(new[] { "alice", "bob", "isolated" }));
        Assert.That(graph.Edges.Single().Id, Is.EqualTo("e1"));

        var nested = (object[])ProtoHelper.FromProtoValue(new ProtoValue
        {
            List = new ProtoList
            {
                Values =
                {
                    new ProtoValue { Node = alice },
                    new ProtoValue { Edge = edge },
                    new ProtoValue { Path = new ProtoPath { Nodes = { alice, bob }, Edges = { edge } } },
                    new ProtoValue { Document = new ProtoDocument { Entries = { { "graph", new ProtoValue { Graph = new ProtoGraph() } } } } }
                }
            }
        });
        Assert.That(nested[0], Is.TypeOf<GraphNode>());
        Assert.That(nested[1], Is.TypeOf<GraphEdge>());
        Assert.That(nested[2], Is.TypeOf<GraphPath>());
        Assert.That(((Dictionary<object, object>)nested[3])["graph"], Is.TypeOf<GraphValue>());
    }

    [Test]
    public async Task GraphResultFetchesEmptyAndNonemptyPagesAndClosesOnce()
    {
        var mock = new Mock<IPolyphenyConnection>(MockBehavior.Strict);
        mock.SetupSequence(connection => connection.SendRecv(It.IsAny<Request>()))
            .ReturnsAsync(Acknowledgment())
            .ReturnsAsync(new Response { Last = true, Frame = new Frame { IsLast = false, GraphFrame = new GraphFrame() } })
            .ReturnsAsync(new Response
            {
                Last = true,
                Frame = new Frame
                {
                    IsLast = true,
                    GraphFrame = new GraphFrame { Element = { new ProtoGraphElement { Edge = Edge() }, new ProtoGraphElement { Node = Node("bob", "Bob") } } }
                }
            })
            .ReturnsAsync(Closed());
        mock.Setup(connection => connection.Receive(8)).ReturnsAsync(Result(new StatementResult
        {
            Frame = new Frame { IsLast = false, GraphFrame = new GraphFrame { Element = { new ProtoGraphElement { Node = Node("alice", "Alice") } } } }
        }));

        var command = new PolyphenyCommand().WithConnection(mock.Object).WithCommandText("MATCH (n) RETURN n");
        await using var result = await command.ExecuteCypherAsync("patients", 1);
        Assert.That(result.Kind, Is.EqualTo(CypherResultKind.Graph));
        Assert.That(((GraphNode)result.Elements.Single()).Id, Is.EqualTo("alice"));
        Assert.That(result.Rows, Is.Empty);
        Assert.That(await result.FetchNextAsync(), Is.True);
        Assert.That(result.Elements, Is.Empty);
        Assert.That(result.IsLast, Is.False);
        Assert.That(result.FetchNext(), Is.True);
        Assert.That(result.Elements.Select(element => element.Id), Is.EqualTo(new[] { "e1", "bob" }));
        Assert.That(result.IsLast, Is.True);
        Assert.That(result.FetchNext(), Is.False);
        await result.DisposeAsync();
        result.Dispose();
        Assert.That(result.IsClosed, Is.True);
        mock.Verify(connection => connection.SendRecv(It.Is<Request>(request =>
            request.ExecuteUnparameterizedStatementRequest != null && request.ExecuteUnparameterizedStatementRequest.NamespaceName == "patients"
            && request.ExecuteUnparameterizedStatementRequest.FetchSize == 1
            && request.ExecuteUnparameterizedStatementRequest.LanguageName == "cypher"
            && request.ExecuteUnparameterizedStatementRequest.Statement == "MATCH (n) RETURN n")), Times.Once);
        mock.Verify(connection => connection.SendRecv(It.Is<Request>(request =>
            request.FetchRequest != null && request.FetchRequest.StatementId == 7 && request.FetchRequest.FetchSize == 1)), Times.Exactly(2));
        mock.Verify(connection => connection.SendRecv(It.Is<Request>(request => request.CloseStatementRequest != null && request.CloseStatementRequest.StatementId == 7)), Times.Once);
    }

    [Test]
    public void MixedResultPreservesColumnsRowsNullAndGraphCells()
    {
        var first = RowFrame(false, Node("alice", "Alice"));
        var second = RowFrame(true, Node("bob", "Bob"));
        var mock = new Mock<IPolyphenyConnection>();
        mock.SetupSequence(connection => connection.SendRecv(It.IsAny<Request>()))
            .ReturnsAsync(Acknowledgment()).ReturnsAsync(new Response { Last = true, Frame = second }).ReturnsAsync(Closed());
        mock.Setup(connection => connection.Receive(8)).ReturnsAsync(Result(new StatementResult { Frame = first }));

        using var result = new PolyphenyCommand().WithConnection(mock.Object).WithCommandText("MATCH (n) RETURN n,n.name")
            .ExecuteCypher("patients", 1);
        Assert.That(result.Kind, Is.EqualTo(CypherResultKind.Rows));
        Assert.That(result.Columns.Select(column => column.ColumnLabel), Is.EqualTo(new[] { "node", "name", "missing" }));
        Assert.That(((GraphNode)result.Rows.Single()[0]).Id, Is.EqualTo("alice"));
        Assert.That(result.Rows.Single()[1], Is.EqualTo("Alice"));
        Assert.That(result.Rows.Single()[2], Is.Null);
        Assert.That(result.FetchNext(), Is.True);
        Assert.That(((GraphNode)result.Rows.Single()[0]).Id, Is.EqualTo("bob"));
        Assert.That(result.Rows.Single()[1], Is.EqualTo("Bob"));
    }

    [Test]
    public void CypherScalarRetainsUpdateCountAndClosesItsStatement()
    {
        var mock = new Mock<IPolyphenyConnection>();
        mock.SetupSequence(connection => connection.SendRecv(It.IsAny<Request>())).ReturnsAsync(Acknowledgment()).ReturnsAsync(Closed());
        mock.Setup(connection => connection.Receive(8)).ReturnsAsync(Result(new StatementResult { Scalar = 3 }));
        using (var result = new PolyphenyCommand().WithConnection(mock.Object).WithCommandText("CREATE (:Person)").ExecuteCypher("patients"))
        {
            Assert.That(result.Kind, Is.EqualTo(CypherResultKind.Scalar));
            Assert.That(result.RowsAffected, Is.EqualTo(3));
            Assert.That(result.IsLast, Is.True);
            Assert.That(result.FetchNext(), Is.False);
            Assert.That(result.Rows, Is.Empty);
            Assert.That(result.Elements, Is.Empty);
        }
        mock.Verify(connection => connection.SendRecv(It.Is<Request>(request => request.CloseStatementRequest != null && request.CloseStatementRequest.StatementId == 7)), Times.Once);
    }

    [Test]
    public async Task EarlyDisposalClosesAnUnfinishedResult()
    {
        var mock = new Mock<IPolyphenyConnection>();
        mock.SetupSequence(connection => connection.SendRecv(It.IsAny<Request>())).ReturnsAsync(Acknowledgment()).ReturnsAsync(Closed());
        mock.Setup(connection => connection.Receive(8)).ReturnsAsync(Result(new StatementResult
        {
            Frame = new Frame { GraphFrame = new GraphFrame(), IsLast = false }
        }));
        var result = await new PolyphenyCommand().WithConnection(mock.Object).WithCommandText("MATCH (n) RETURN n").ExecuteCypherAsync("patients");
        await result.DisposeAsync();
        Assert.That(result.IsClosed, Is.True);
        Assert.That(await result.FetchNextAsync(), Is.False);
        mock.Verify(connection => connection.SendRecv(It.Is<Request>(request => request.CloseStatementRequest != null && request.CloseStatementRequest.StatementId == 7)), Times.Once);
        mock.Verify(connection => connection.SendRecv(It.Is<Request>(request => request.FetchRequest != null)), Times.Never);
    }

    [Test]
    public async Task FetchFailureClosesStatementAndRetainsOriginalError()
    {
        var mock = new Mock<IPolyphenyConnection>(MockBehavior.Strict);
        mock.SetupSequence(connection => connection.SendRecv(It.IsAny<Request>()))
            .ReturnsAsync(Acknowledgment())
            .ReturnsAsync(new Response
            {
                Last = true,
                ErrorResponse = new ErrorResponse
                {
                    Message = "Cypher cursor expired",
                    Details = new ErrorDetails { State = "34000", ErrorCode = 0 }
                }
            })
            .ReturnsAsync(new Response
            {
                Last = true,
                ErrorResponse = new ErrorResponse
                {
                    Message = "Cleanup failed",
                    Details = new ErrorDetails { State = "XX000" }
                }
            });
        mock.Setup(connection => connection.Receive(8)).ReturnsAsync(Result(new StatementResult
        {
            Frame = new Frame { IsLast = false, GraphFrame = new GraphFrame() }
        }));
        await using var result = await new PolyphenyCommand().WithConnection(mock.Object).WithCommandText("MATCH (n) RETURN n")
            .ExecuteCypherAsync("patients");
        var error = Assert.ThrowsAsync<PolyphenyException>(() => result.FetchNextAsync());
        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Is.EqualTo("Cypher cursor expired"));
            Assert.That(error.SqlState, Is.EqualTo("34000"));
            Assert.That(error.ServerErrorCode, Is.Zero);
            Assert.That(result.IsClosed, Is.True);
        });
        Assert.That(await result.FetchNextAsync(), Is.False);
        mock.Verify(connection => connection.SendRecv(It.Is<Request>(request => request.FetchRequest != null && request.FetchRequest.StatementId == 7)), Times.Once);
        mock.Verify(connection => connection.SendRecv(It.Is<Request>(request => request.CloseStatementRequest != null && request.CloseStatementRequest.StatementId == 7)), Times.Once);
    }

    [Test]
    public async Task ExplicitStatementCloseReportsServerError()
    {
        var mock = new Mock<IPolyphenyConnection>(MockBehavior.Strict);
        mock.SetupSequence(connection => connection.SendRecv(It.IsAny<Request>()))
            .ReturnsAsync(Acknowledgment())
            .ReturnsAsync(new Response
            {
                Last = true,
                ErrorResponse = new ErrorResponse
                {
                    Message = "Invalid cursor",
                    Details = new ErrorDetails { State = "34000" }
                }
            });
        mock.Setup(connection => connection.Receive(8)).ReturnsAsync(Result(new StatementResult { Scalar = 0 }));
        var result = await new PolyphenyCommand().WithConnection(mock.Object).WithCommandText("MATCH (n) DELETE n")
            .ExecuteCypherAsync("patients");

        var error = Assert.ThrowsAsync<PolyphenyException>(() => result.DisposeAsync().AsTask());
        Assert.That(error!.SqlState, Is.EqualTo("34000"));
        Assert.That(result.IsClosed, Is.True);
        await result.DisposeAsync();
        mock.Verify(connection => connection.SendRecv(It.Is<Request>(request => request.CloseStatementRequest != null && request.CloseStatementRequest.StatementId == 7)), Times.Once);
    }

    [Test]
    public void InitialCypherErrorClosesAcknowledgedStatement()
    {
        var mock = new Mock<IPolyphenyConnection>();
        mock.SetupSequence(connection => connection.SendRecv(It.IsAny<Request>())).ReturnsAsync(Acknowledgment()).ReturnsAsync(Closed());
        mock.Setup(connection => connection.Receive(8)).ReturnsAsync(new Response
        {
            Last = true,
            ErrorResponse = new ErrorResponse { Message = "Prism requires exactly one Cypher statement per request." }
        });
        var error = Assert.Throws<PolyphenyException>(() =>
            new PolyphenyCommand().WithConnection(mock.Object).WithCommandText("RETURN 1; RETURN 2").ExecuteCypher("patients"));
        Assert.That(error!.Message, Does.Contain("exactly one Cypher statement"));
        mock.Verify(connection => connection.SendRecv(It.Is<Request>(request => request.CloseStatementRequest != null && request.CloseStatementRequest.StatementId == 7)), Times.Once);
    }

    [Test]
    public void UnknownGraphElementAndChangedFrameFormatFailExplicitly()
    {
        var mock = new Mock<IPolyphenyConnection>();
        mock.SetupSequence(connection => connection.SendRecv(It.IsAny<Request>())).ReturnsAsync(Acknowledgment()).ReturnsAsync(Closed());
        mock.Setup(connection => connection.Receive(8)).ReturnsAsync(Result(new StatementResult
        {
            Frame = new Frame { IsLast = true, GraphFrame = new GraphFrame { Element = { new ProtoGraphElement() } } }
        }));
        Assert.Throws<InvalidDataException>(() =>
            new PolyphenyCommand().WithConnection(mock.Object).WithCommandText("MATCH (n) RETURN n").ExecuteCypher("patients"));

        mock = new Mock<IPolyphenyConnection>();
        mock.SetupSequence(connection => connection.SendRecv(It.IsAny<Request>()))
            .ReturnsAsync(Acknowledgment())
            .ReturnsAsync(new Response { Last = true, Frame = RowFrame(true, Node("alice", "Alice")) })
            .ReturnsAsync(Closed());
        mock.Setup(connection => connection.Receive(8)).ReturnsAsync(Result(new StatementResult
        {
            Frame = new Frame { IsLast = false, GraphFrame = new GraphFrame() }
        }));
        using var result = new PolyphenyCommand().WithConnection(mock.Object).WithCommandText("MATCH (n) RETURN n").ExecuteCypher("patients");
        Assert.Throws<InvalidDataException>(() => result.FetchNext());
    }

    [TestCase("id")]
    [TestCase("last")]
    [TestCase("statement")]
    [TestCase("width")]
    public void MalformedInitialResponseClosesItsAcknowledgedStatement(string defect)
    {
        var mock = new Mock<IPolyphenyConnection>();
        mock.SetupSequence(connection => connection.SendRecv(It.IsAny<Request>())).ReturnsAsync(Acknowledgment()).ReturnsAsync(Closed());
        var response = Result(new StatementResult { Frame = RowFrame(true, Node("alice", "Alice")) });
        switch (defect)
        {
            case "id":
                response.Id = 99;
                break;
            case "last":
                response.Last = false;
                break;
            case "statement":
                response.StatementResponse.StatementId = 99;
                break;
            case "width":
                response.StatementResponse.Result.Frame.RelationalFrame.Rows[0].Values.Clear();
                break;
        }
        mock.Setup(connection => connection.Receive(8)).ReturnsAsync(response);
        var error = Assert.Catch(() =>
            new PolyphenyCommand().WithConnection(mock.Object).WithCommandText("MATCH (n) RETURN n,n.name").ExecuteCypher("patients"));
        Assert.That(error, Is.TypeOf<InvalidOperationException>().Or.TypeOf<InvalidDataException>());
        mock.Verify(connection => connection.SendRecv(It.Is<Request>(request => request.CloseStatementRequest != null)), Times.Once);
    }

    [TestCase("id")]
    [TestCase("last")]
    [TestCase("frame")]
    public void MalformedFetchResponseClosesTheCursor(string defect)
    {
        var response = new Response { Last = true, Frame = new Frame { IsLast = true, GraphFrame = new GraphFrame() } };
        if (defect == "id") response.Id = 99;
        if (defect == "last") response.Last = false;
        if (defect == "frame") response.Frame = null;
        var mock = new Mock<IPolyphenyConnection>();
        mock.SetupSequence(connection => connection.SendRecv(It.IsAny<Request>()))
            .ReturnsAsync(Acknowledgment()).ReturnsAsync(response).ReturnsAsync(Closed());
        mock.Setup(connection => connection.Receive(8)).ReturnsAsync(Result(new StatementResult
        {
            Frame = new Frame { IsLast = false, GraphFrame = new GraphFrame() }
        }));
        using var result = new PolyphenyCommand().WithConnection(mock.Object).WithCommandText("MATCH (n) RETURN n").ExecuteCypher("patients");
        Assert.Throws<InvalidDataException>(() => result.FetchNext());
        Assert.That(result.IsClosed, Is.True);
        mock.Verify(connection => connection.SendRecv(It.Is<Request>(request => request.CloseStatementRequest != null)), Times.Once);
    }

    [Test]
    public void ValidatesNamespaceFetchSizeStatementAndParametersBeforeSending()
    {
        var mock = new Mock<IPolyphenyConnection>(MockBehavior.Strict);
        var command = new PolyphenyCommand().WithConnection(mock.Object).WithCommandText("MATCH (n) RETURN n");
        Assert.Throws<ArgumentException>(() => command.ExecuteCypher(""));
        Assert.Throws<ArgumentOutOfRangeException>(() => command.ExecuteCypher("patients", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => command.ExecuteCypher("patients", -1));
        command.WithCommandText("");
        Assert.Throws<InvalidOperationException>(() => command.ExecuteCypher("patients"));
        command.WithCommandText("RETURN ?").WithParameterValues(new object[] { "value" });
        Assert.Throws<NotSupportedException>(() => command.ExecuteCypher("patients"));
        command.WithParameterValues(Array.Empty<object>());
        command.Parameters.Add(new PolyphenyParameter { ParameterName = "value", Value = 1 });
        Assert.Throws<NotSupportedException>(() => command.ExecuteCypher("patients"));
        Assert.Throws<InvalidOperationException>(() => new PolyphenyCommand().WithCommandText("RETURN 1").ExecuteCypher("patients"));
        mock.VerifyNoOtherCalls();
    }

    private static Response Acknowledgment() => new() { StatementResponse = new StatementResponse { StatementId = 7 } };
    private static Response Result(StatementResult result) => new()
    {
        Last = true,
        StatementResponse = new StatementResponse { StatementId = 7, Result = result }
    };
    private static Response Closed() => new() { Last = true, CloseStatementResponse = new CloseStatementResponse() };

    private static Frame RowFrame(bool last, ProtoNode node) => new()
    {
        IsLast = last,
        RelationalFrame = new RelationalFrame
        {
            ColumnMeta =
            {
                new ColumnMeta { ColumnLabel = "node" },
                new ColumnMeta { ColumnLabel = "name" },
                new ColumnMeta { ColumnLabel = "missing" }
            },
            Rows =
            {
                new Row
                {
                    Values =
                    {
                        new ProtoValue { Node = node },
                        new ProtoValue { String = new ProtoString { String = node.Name } },
                        new ProtoValue { Null = new ProtoNull() }
                    }
                }
            }
        }
    };
}
