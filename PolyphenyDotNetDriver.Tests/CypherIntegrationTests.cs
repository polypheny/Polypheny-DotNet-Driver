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

using Polypheny.Prism;
using GraphElement = PolyphenyDotNetDriver.GraphElement;

namespace PolyphenyDotNetDriver.Tests;

[Category("Integration")]
[NonParallelizable]
public class CypherIntegrationTests
{
    private readonly string _namespace = "dotnet_graph_" + Guid.NewGuid().ToString("N");

    private static PolyphenyConnection Connect(bool autoCommit = false)
    {
        var connection = new PolyphenyConnection(
            Environment.GetEnvironmentVariable("POLYPHENY_TEST_CONNECTION") ?? "localhost:20590,pa:",
            new PolyphenyConfig { AutoCommit = autoCommit });
        connection.Open();
        return connection;
    }

    [OneTimeSetUp]
    public void CreateGraph()
    {
        using var connection = Connect();
        using var create = new PolyphenyCommand().WithConnection(connection)
            .WithCommandText("CREATE GRAPH NAMESPACE " + _namespace);
        create.ExecuteNonQuery();
        using var command = new PolyphenyCommand().WithConnection(connection).WithCommandText(
            "CREATE (:Person {id:1,name:'Alice'})-[:KNOWS {since:2020}]->"
            + "(:Person {id:2,name:'Bob'})-[:KNOWS {since:2021}]->(:Person {id:3,name:'Charlie'}),"
            + "(:Isolated {id:4,name:'Isolated'})");
        using var result = command.ExecuteCypher(_namespace);
        Assert.That(result.Kind, Is.EqualTo(CypherResultKind.Scalar));
        Assert.That(result.RowsAffected, Is.GreaterThan(0));
        connection.BeginTransaction().Commit();
    }

    [OneTimeTearDown]
    public void DropGraph()
    {
        using var connection = Connect();
        using var drop = new PolyphenyCommand().WithConnection(connection).WithCommandText("DROP NAMESPACE IF EXISTS " + _namespace);
        drop.ExecuteNonQuery();
    }

    [Test]
    public void PagesNodesAndKeepsEveryField()
    {
        using var connection = Connect();
        using var command = new PolyphenyCommand().WithConnection(connection).WithCommandText("MATCH (n:Person) RETURN n ORDER BY n.id");
        using var result = command.ExecuteCypher(_namespace, 1);
        var nodes = new List<GraphNode>();
        do
        {
            Assert.That(result.Kind, Is.EqualTo(CypherResultKind.Graph));
            Assert.That(result.Elements, Has.Count.EqualTo(1));
            nodes.Add((GraphNode)result.Elements.Single());
        } while (result.FetchNext());
        Assert.That(nodes.Select(node => node.Properties["name"]), Is.EqualTo(new[] { "Alice", "Bob", "Charlie" }));
        Assert.That(nodes.Select(node => node.Id).Distinct().Count(), Is.EqualTo(3));
        foreach (var node in nodes)
        {
            Assert.That(node.Id, Is.Not.Empty);
            Assert.That(node.Labels, Does.Contain("Person"));
            Assert.That(Convert.ToInt64(node.Properties["id"]), Is.InRange(1, 3));
        }
        Assert.That(result.IsLast, Is.True);
        Assert.That(result.FetchNext(), Is.False);
    }

    [Test]
    public void GraphTriplesAndPathsRetainEndpointsAndTraversalOrder()
    {
        using var connection = Connect();
        using var triple = new PolyphenyCommand().WithConnection(connection)
            .WithCommandText("MATCH (a:Person {id:1})-[r:KNOWS]->(b:Person) RETURN a,r,b");
        using (var result = triple.ExecuteCypher(_namespace, 1))
        {
            Assert.That(result.Elements, Has.Count.EqualTo(3));
            var alice = (GraphNode)result.Elements[0];
            var edge = (GraphEdge)result.Elements[1];
            var bob = (GraphNode)result.Elements[2];
            Assert.That(alice.Properties["name"], Is.EqualTo("Alice"));
            Assert.That(bob.Properties["name"], Is.EqualTo("Bob"));
            Assert.That(edge.Id, Is.Not.Empty);
            Assert.That(edge.Labels, Does.Contain("KNOWS"));
            Assert.That(edge.Left, Is.EqualTo(alice.Id));
            Assert.That(edge.Right, Is.EqualTo(bob.Id));
            Assert.That(edge.Direction, Is.EqualTo(GraphDirection.LeftToRight));
            Assert.That(Convert.ToInt64(edge.Properties["since"]), Is.EqualTo(2020));
            Assert.That(result.IsLast, Is.True);
        }

        using var path = new PolyphenyCommand().WithConnection(connection).WithCommandText(
            "MATCH p=(:Person {id:1})-[:KNOWS*1..3]->(:Person {id:3}) RETURN p");
        using var pathResult = path.ExecuteCypher(_namespace, 1);
        Assert.That(pathResult.Elements.Select(element => element.GetType()),
            Is.EqualTo(new[] { typeof(GraphNode), typeof(GraphEdge), typeof(GraphNode), typeof(GraphEdge), typeof(GraphNode) }));
        Assert.That(pathResult.Elements.OfType<GraphNode>().Select(node => node.Properties["name"]),
            Is.EqualTo(new[] { "Alice", "Bob", "Charlie" }));
    }

    [Test]
    public void WholeGraphContainsIsolatedNode()
    {
        using var connection = Connect();
        using var command = new PolyphenyCommand().WithConnection(connection).WithCommandText("MATCH (*) RETURN *");
        using var result = command.ExecuteCypher(_namespace, 1);
        var elements = ReadGraphPages(result);
        Assert.That(elements.OfType<GraphNode>().Count(), Is.EqualTo(4));
        Assert.That(elements.OfType<GraphEdge>().Count(), Is.EqualTo(2));
        Assert.That(elements.OfType<GraphNode>().Single(node => node.Labels.Contains("Isolated")).Properties["name"], Is.EqualTo("Isolated"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MixedNodeProjectionKeepsColumnOrderAndPages(bool reverseColumns)
    {
        using var connection = Connect();
        var projection = reverseColumns ? "n.name AS name,n AS node" : "n AS node,n.name AS name";
        using var command = new PolyphenyCommand().WithConnection(connection)
            .WithCommandText("MATCH (n:Person) RETURN " + projection + " ORDER BY n.id");
        using var result = command.ExecuteCypher(_namespace, 1);
        var names = new List<string>();
        do
        {
            Assert.That(result.Kind, Is.EqualTo(CypherResultKind.Rows));
            Assert.That(result.Columns.Select(column => column.ColumnLabel),
                Is.EqualTo(reverseColumns ? new[] { "name", "node" } : new[] { "node", "name" }));
            var nodeIndex = reverseColumns ? 1 : 0;
            var nameIndex = reverseColumns ? 0 : 1;
            Assert.That(result.Columns[nodeIndex].TypeMeta.ProtoValueType, Is.EqualTo(ProtoPolyType.Node));
            var row = result.Rows.Single();
            var node = (GraphNode)row[nodeIndex];
            Assert.That(row[nameIndex], Is.EqualTo(node.Properties["name"]));
            names.Add((string)row[nameIndex]);
        } while (result.FetchNext());
        Assert.That(names, Is.EqualTo(new[] { "Alice", "Bob", "Charlie" }));
    }

    [Test]
    public void MixedPathRetainsReverseTraversalAndOriginalEdgeOrientation()
    {
        using var connection = Connect();
        using var command = new PolyphenyCommand().WithConnection(connection).WithCommandText(
            "MATCH p=(:Person {id:3})<-[:KNOWS*1..3]-(:Person {id:1}) RETURN p,'route' AS title");
        using var result = command.ExecuteCypher(_namespace);
        Assert.That(result.Kind, Is.EqualTo(CypherResultKind.Rows));
        var path = (GraphPath)result.Rows.Single()[0];
        Assert.That(path.Nodes.Select(node => node.Properties["name"]), Is.EqualTo(new[] { "Charlie", "Bob", "Alice" }));
        Assert.That(path.Edges, Has.Count.EqualTo(2));
        Assert.That(path.Edges[0].Left, Is.EqualTo(path.Nodes[1].Id));
        Assert.That(path.Edges[0].Right, Is.EqualTo(path.Nodes[0].Id));
        Assert.That(path.Edges[0].Direction, Is.EqualTo(GraphDirection.LeftToRight));
        Assert.That(result.Rows.Single()[1], Is.EqualTo("route"));
    }

    [Test]
    public void ScalarAggregationAndEmptyResultsKeepTheirFormats()
    {
        using var connection = Connect();
        using var command = new PolyphenyCommand().WithConnection(connection).WithCommandText("MATCH (n:Person) RETURN count(n) AS count");
        using (var count = command.ExecuteCypher(_namespace, 1))
        {
            Assert.That(count.Kind, Is.EqualTo(CypherResultKind.Rows));
            Assert.That(Convert.ToInt64(count.Rows.Single().Single()), Is.EqualTo(3));
        }
        command.WithCommandText("MATCH (n:Person {id:999}) RETURN n");
        using (var emptyGraph = command.ExecuteCypher(_namespace, 1))
        {
            Assert.That(emptyGraph.Kind, Is.EqualTo(CypherResultKind.Graph));
            Assert.That(emptyGraph.Elements, Is.Empty);
            Assert.That(emptyGraph.IsLast, Is.True);
        }
        command.WithCommandText("MATCH (n:Person {id:999}) RETURN n,n.name AS name");
        using var emptyRows = command.ExecuteCypher(_namespace, 1);
        Assert.That(emptyRows.Kind, Is.EqualTo(CypherResultKind.Rows));
        Assert.That(emptyRows.Columns, Has.Count.EqualTo(2));
        Assert.That(emptyRows.Rows, Is.Empty);
        Assert.That(emptyRows.IsLast, Is.True);
    }

    [Test]
    public void NumericListProjectionRetainsExactLiteralValuesAndNull()
    {
        using var connection = Connect();
        using var command = new PolyphenyCommand().WithConnection(connection)
            .WithCommandText("MATCH (n:Person {id:1}) RETURN [1,2,null] AS values");
        using var result = command.ExecuteCypher(_namespace);
        Assert.That(result.Kind, Is.EqualTo(CypherResultKind.Rows));
        Assert.That(result.Columns.Single().ColumnLabel, Is.EqualTo("values"));
        var values = (object[])result.Rows.Single().Single();
        Assert.That(values, Is.EqualTo(new object?[] { new PolyphenyDecimal(1, 0), new PolyphenyDecimal(2, 0), null }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EarlyCloseAndErrorsLeaveTheConnectionUsable(bool autoCommit)
    {
        using var connection = Connect(autoCommit);
        using var command = new PolyphenyCommand().WithConnection(connection).WithCommandText("MATCH (n:Person) RETURN n ORDER BY n.id");
        var first = await command.ExecuteCypherAsync(_namespace, 1);
        Assert.That(first.IsLast, Is.False);
        await first.DisposeAsync();
        Assert.That(await first.FetchNextAsync(), Is.False);
        await using (var second = await command.ExecuteCypherAsync(_namespace, 3))
        {
            Assert.That(second.Elements, Has.Count.EqualTo(3));
        }

        command.WithCommandText("MATCH (n RETURN n");
        Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteCypherAsync(_namespace));
        command.WithCommandText("MATCH (n) RETURN n; MATCH (n) RETURN n");
        var multiple = Assert.Throws<InvalidOperationException>(() => command.ExecuteCypher(_namespace));
        Assert.That(multiple!.Message, Does.Contain("exactly one Cypher statement"));
        foreach (var named in new[] { false, true })
        {
            var prepare = new PrepareStatementRequest { NamespaceName = _namespace, LanguageName = "cypher", Statement = "MATCH (n) RETURN n" };
            var request = named
                ? new Request { PrepareNamedStatementRequest = prepare }
                : new Request { PrepareIndexedStatementRequest = prepare };
            var rejected = await connection.SendRecv(request);
            Assert.That(rejected.ErrorResponse.Message, Does.Contain("Prepared Cypher statements are not supported"));
        }
        command.WithCommandText("MATCH (n:Person) RETURN count(n)");
        await using var recovered = await command.ExecuteCypherAsync(_namespace);
        Assert.That(Convert.ToInt64(recovered.Rows.Single().Single()), Is.EqualTo(3));
    }

    [Test]
    public void CypherWritesRespectManualRollbackAndCommit()
    {
        using var connection = Connect();
        using var command = new PolyphenyCommand().WithConnection(connection).WithCommandText("CREATE (:Probe {id:100})");
        using (var created = command.ExecuteCypher(_namespace))
        {
            Assert.That(created.Kind, Is.EqualTo(CypherResultKind.Scalar));
        }
        connection.BeginTransaction().Rollback();
        command.WithCommandText("MATCH (n:Probe) RETURN count(n)");
        using (var empty = command.ExecuteCypher(_namespace))
        {
            Assert.That(Convert.ToInt64(empty.Rows.Single().Single()), Is.Zero);
        }
        command.WithCommandText("CREATE (:Probe {id:100})");
        using (command.ExecuteCypher(_namespace)) { }
        connection.BeginTransaction().Commit();
        command.WithCommandText("MATCH (n:Probe) RETURN count(n)");
        using (var count = command.ExecuteCypher(_namespace))
        {
            Assert.That(Convert.ToInt64(count.Rows.Single().Single()), Is.EqualTo(1));
        }
        command.WithCommandText("MATCH (n:Probe) DELETE n");
        using (var deleted = command.ExecuteCypher(_namespace))
        {
            TestContext.WriteLine($"DELETE format {deleted.Kind}, rows {deleted.Rows.Count}, count {deleted.RowsAffected}");
        }
        connection.BeginTransaction().Commit();
        command.WithCommandText("MATCH (n:Probe) RETURN count(n)");
        using var afterDelete = command.ExecuteCypher(_namespace);
        Assert.That(Convert.ToInt64(afterDelete.Rows.Single().Single()), Is.Zero);
    }

    private static List<GraphElement> ReadGraphPages(PolyphenyCypherResult result)
    {
        var elements = new List<GraphElement>();
        do
        {
            elements.AddRange(result.Elements);
        } while (result.FetchNext());
        return elements;
    }
}
