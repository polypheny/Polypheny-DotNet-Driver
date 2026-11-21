<p align="center">
    <a href="https://polypheny.org/">
        <picture><source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/polypheny/Admin/master/Logo/logo-white-text_cropped.png">
            <img width='50%' alt="Light: 'Resume application project app icon' Dark: 'Resume application project app icon'" src="https://raw.githubusercontent.com/polypheny/Admin/master/Logo/logo-transparent_cropped.png">
        </picture>
    </a>
</p>


# Polypheny .NET Driver

Connect .NET applications to Polypheny-DB over the Prism query interface. The driver supports SQL, MongoQL (MQL) and Cypher.

## Requirements

- The driver targets .NET 6. Use the .NET 8 SDK to run this repository's tests.
- Connect to a Polypheny-DB server with a compatible Prism 2.x interface. Document collection metadata needs server support introduced in Prism 2.2. A Prism 1.x server requires an earlier driver.
- Cypher support requires a server with the merged graph Prism implementation and Prism 2.3. The driver uses the published `Polypheny.Prism` 2.3.0 NuGet package.
- Enable the Prism query interface on the server. The examples use its default TCP port `20590` and the local development credentials `pa` with an empty password.

See the [Polypheny-DB setup instructions](https://github.com/polypheny/Polypheny-DB) to start a server.

## Installation

Clone the repository and reference the driver project from your .NET application:

```sh
git clone https://github.com/polypheny/Polypheny-DotNet-Driver.git
dotnet add MyApp.csproj reference ./Polypheny-DotNet-Driver/PolyphenyDotNetDriver/PolyphenyDotNetDriver.csproj
```

Run the second command from the directory containing `MyApp.csproj` and the checkout. To build a local NuGet package, run the following from the repository root:

```sh
dotnet pack PolyphenyDotNetDriver/PolyphenyDotNetDriver.csproj -c Release
```

## Run SQL

```csharp
using PolyphenyDotNetDriver;

using var connection = PolyphenyDriver.OpenConnection("localhost:20590,pa:");
connection.Open();

using var command = new PolyphenyCommand()
    .WithConnection(connection)
    .WithCommandText("SELECT * FROM emps");
using var reader = command.ExecuteReader();
while (reader.Read())
{
    Console.WriteLine(reader["name"]);
}
```

Use `ExecuteNonQuery()` for DDL and DML. Use parameters for SQL data values. This example assumes an existing `customers` table with `id` and `name` columns.

```csharp
using var transaction = connection.BeginTransaction();
using var insert = new PolyphenyCommand()
    .WithConnection(connection)
    .WithTransaction(transaction)
    .WithCommandText("INSERT INTO customers (id, name) VALUES (?, ?)")
    .WithParameterValues(new object[] { 1, "Alice" });
insert.Prepare();
insert.ExecuteNonQuery();
transaction.Commit();
```

Call `transaction.Rollback()` to discard uncommitted DML.

## Query documents

Supply the namespace when executing a query against a document collection. In this example, `patients` contains a collection named `symptoms`.

```csharp
using var query = new PolyphenyCommand()
    .WithConnection(connection)
    .WithCommandText("db.symptoms.find()");

foreach (var document in query.ExecuteQueryMongo("patients"))
{
    Console.WriteLine(document["patient_id"]);
}
```

Document keys are strings. Nested documents are dictionaries and lists are object arrays.

## Query graphs with Cypher

Set the graph namespace explicitly. A graph-only projection returns a flat page of nodes and edges in `Elements`.
This example assumes a graph namespace named `social` containing `Person` nodes.

```csharp
using var query = new PolyphenyCommand()
    .WithConnection(connection)
    .WithCommandText("MATCH (n:Person) RETURN n");
using var result = query.ExecuteCypher("social", fetchSize: 100);
do
{
    foreach (var node in result.Elements.OfType<GraphNode>())
        Console.WriteLine($"{node.Id}: {node.Properties["name"]}");
} while (result.FetchNext());
```

Use `ExecuteCypherAsync`, `FetchNextAsync` and `await using` for asynchronous access.
An empty non-final page can occur, so continue fetching until `FetchNext` returns false.
Fetch size counts source rows. A row containing a node, an edge and another node can therefore produce three elements.

Mixed graph and scalar projections return columns and rows. The example keeps the column order from `RETURN`.

```csharp
using var mixed = new PolyphenyCommand()
    .WithConnection(connection)
    .WithCommandText("MATCH (n:Person) RETURN n AS node,n.name AS name")
    .ExecuteCypher("social", fetchSize: 100);
do
{
    foreach (var row in mixed.Rows)
    {
        var node = (GraphNode)row[0];
        Console.WriteLine($"{node.Id}: {row[1]}");
    }
} while (mixed.FetchNext());
```

`GraphNode` and `GraphEdge` retain identifiers, names, labels and recursively decoded properties.
Edges also expose `Left`, `Right` and `Direction`.
A path in a mixed projection is a `GraphPath`, whose `Nodes` and `Edges` follow traversal order.
An edge retains its original endpoints and direction when traversed in reverse.
A whole-graph value in a row is a `GraphValue`, including isolated nodes.
Standalone paths and whole-graph queries use the flat graph stream, where path boundaries are not available.
Exact decimal values, including Cypher numeric literals, are represented by `PolyphenyDecimal`.
Its `UnscaledValue` and `Scale` preserve the value as `UnscaledValue * 10^(-Scale)`.
`ToString()` uses an exact base-10 exponent representation.
Binary and file values decode to byte arrays. Dates and timestamps are UTC `DateTime` values,
times are `TimeSpan` values, and `PolyphenyInterval` preserves the separate `Months` and
`Milliseconds` components of an interval.

Inspect `Kind` to distinguish `Graph`, `Rows` and `Scalar` results.
`RowsAffected` is populated for scalar command acknowledgments.
Cypher statements producing relational results, including update-count rows, expose those values through `Rows`.
Dispose every result to close its server statement, including partially fetched results.
The connection can then execute another query.
Run one operation at a time on a connection. Fetching and disposing the same result are serialized
so disposal waits for its pending page response.
Results belong to the session that created them. Reopening a connection does not reactivate old results.

Connections use manual transactions by default. Use `connection.BeginTransaction().Commit()` or `Rollback()`
for graph writes. For autocommit, construct a connection with `new PolyphenyConfig { AutoCommit = true }`.
Prism accepts one Cypher statement per request. Prepared and parameterized Cypher statements are unsupported.

## Discover entity metadata

Use `SearchEntities` or `SearchEntitiesAsync` on an open connection. Supply a server-side name pattern to filter results, or omit it to list the namespace.

```csharp
foreach (var entity in connection.SearchEntities("patients", "symptoms"))
{
    if (entity.Document is not { } collection)
        continue;

    Console.WriteLine($"{collection.NamespaceName}.{collection.CollectionName}");
    foreach (var field in collection.Fields)
        Console.WriteLine($"{field.FieldPath}: {field.TypeName}, optional: {field.IsOptional}");

    if (collection.HasSampleDocumentJson)
        Console.WriteLine(collection.SampleDocumentJson);

    foreach (var index in collection.DocumentIndexes)
        Console.WriteLine($"{index.FieldPath}: {index.IndexType}, unique: {index.Unique}");
}
```

Fields may be inferred from a bounded sample. An empty collection can have no sample JSON. Index metadata depends on what the server and storage adapter expose. Searching a relational namespace returns table metadata in `Entity.Table`.

## Run the tests

Run the unit and loopback protocol tests without a database:

```sh
dotnet test PolyphenyDotNetDriver.Tests/PolyphenyDotNetDriver.Tests.csproj --filter 'TestCategory!=Integration'
```

Use a dedicated Polypheny test server for the integration tests, which create and drop tables. The default endpoint is `localhost:20590,pa:`. Set `POLYPHENY_TEST_CONNECTION` to use another endpoint.
The graph integration tests also create a private graph namespace, exercise paging and transaction handling,
and remove that namespace. They require the Prism 2.3 graph server described above.

```sh
dotnet test PolyphenyDotNetDriver.Tests/PolyphenyDotNetDriver.Tests.csproj --filter 'TestCategory=Integration'
```

For an interactive coverage report, install `dotnet-reportgenerator-globaltool` and run `test.sh` from `PolyphenyDotNetDriver.Tests` with the test server running.

## Release the driver

Run the **.NET driver release** workflow with a stable tag such as `v2.0.0` to test and download the NuGet package from GitHub Actions. Publishing a stable GitHub release with that tag attaches the package to the release and pushes the same package to NuGet.org.

## Roadmap
See the [open issues](https://github.com/polypheny/Polypheny-DB/issues) for a list of proposed features (and known issues).


## Contributing
We highly welcome your contributions to the _Polypheny .NET Driver_. If you would like to contribute, please fork the repository and submit your changes as a pull request. Please consult our [Admin Repository](https://github.com/polypheny/Admin) and our [Website](https://polypheny.org) for guidelines and additional information.

Please note that we have a [code of conduct](https://github.com/polypheny/Admin/blob/master/CODE_OF_CONDUCT.md). Please follow it in all your interactions with the project.


## License
The Apache 2.0 License
