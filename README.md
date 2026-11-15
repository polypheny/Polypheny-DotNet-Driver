<p align="center">
    <a href="https://polypheny.org/">
        <picture><source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/polypheny/Admin/master/Logo/logo-white-text_cropped.png">
            <img width='50%' alt="Light: 'Resume application project app icon' Dark: 'Resume application project app icon'" src="https://raw.githubusercontent.com/polypheny/Admin/master/Logo/logo-transparent_cropped.png">
        </picture>
    </a>
</p>


# Polypheny .NET Driver

Connect .NET applications to Polypheny-DB over the Prism query interface. The driver supports SQL and MongoQL (MQL).

## Requirements

- The driver targets .NET 6. Use the .NET 8 SDK to run this repository's tests.
- Connect to a Polypheny-DB server with a compatible Prism 2.x interface. Document collection metadata needs server support introduced in Prism 2.2. A Prism 1.x server requires an earlier driver.
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
