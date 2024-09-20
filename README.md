<p align="center">
    <a href="https://polypheny.org/">
        <picture><source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/polypheny/Admin/master/Logo/logo-white-text_cropped.png">
            <img width='50%' alt="Light: 'Resume application project app icon' Dark: 'Resume application project app icon'" src="https://raw.githubusercontent.com/polypheny/Admin/master/Logo/logo-transparent_cropped.png">
        </picture>
    </a>    
</p> 


# Polypheny-DB .NeT Driver


A Polypheny-DB Driver for the .NET programming language which supports multiple models and query languages.


## How To Setup

### Setup Polypheny-DB
We can access the detail documentation regarding Polypheny-DB [here](https://github.com/polypheny/Polypheny-DB). To get start with the Polypheny-DB, the easiest way is to use a [release](https://github.com/polypheny/Polypheny-DB/releases/latest). Alternatively, we can use [Polypheny Control](https://github.com/polypheny/Polypheny-Control) to automatically build Polypheny-DB.

### Setup Driver Inside .NET Project
To setup this driver inside a .NET project, we need to install this driver inside the .NET project. The installation can be done through two ways: relative import and NuGet installation (soon)

#### Relative Import Installation
To install through relative import, we need to clone the driver project and import the driver project to the `.csproj` file. We can clone the driver project by using this command
```
git clone https://github.com/polypheny/Polypheny-DotNet-Driver.git 
```

To import the driver project to the `.csproj` file, we can modify the file, by adding this codes

```xml
<Project>
    ...
    <ItemGroup>
        ...
        <ProjectReference Include="<relative_path>\Polypheny-DotNet-Driver\PolyphenyDotNetDriver.csproj" />
        ...
    </ItemGroup>
    ...
</Project>
```

#### NuGet Package Installation (soon)
_We will be able to install the driver inside a .NET project through NuGet soon_


## How To Use
This driver provides several functionalities to access the Polypheny-DB, which are:
- Execute SQL Query Statement
- Execute DDL & DML SQL Statement
- Prepare SQL Statement
- Execute SQL Transaction
- Execute Document-based NoSQL Database Query

We also provided a simple demo project for this driver, which can be accessed on [here](https://github.com/malikrafsan/Polypheny-DotNet-Driver/tree/project-demo)

### Execute SQL Query Statement
This driver provides the functionality to execute `SELECT` SQL query to the Polypheny-DB. The result of the `SELECT` statement is `PolyphenyDataReader` class which inherits from `DbDataReader` class from .NET. Here is the code example to use execute SQL query statement using this driver

```csharp
using (var connection = PolyphenyDriver.OpenConnection("localhost:20590,pa:"))
{
    connection.Open();
    var query = "SELECT * FROM emps";

    var command = new PolyphenyCommand().
        WithConnection(connection).
        WithCommandText(query);

    var reader = command.ExecuteReader();
    var columns = reader.Columns;
    Console.WriteLine("Columns: " + string.Join(", ", columns));
    Console.WriteLine("Number of rows: " + reader.ResultSets.RowCount());

    // read the result
    Console.WriteLine("Rows: ");
    Console.WriteLine("-----");
    while (reader.Read())
    {
        foreach (var column in columns)
        {
            Console.WriteLine($"{column}: {reader[column]}");
        }
        Console.WriteLine("-----");
    }

    connection.Close();
}
```

### Execute DML & DDL SQL Statement
This driver provides the functionality to execute DML and DDL SQL Stetement to the Polypheny-DB. We can define the schema and data manipulation to the schema. Here is the code example to use execute DML & DDL SQL statement using this driver

#### Drop and Create Table
```csharp
using (var connection = PolyphenyDriver.OpenConnection("localhost:20590,pa:"))
{
    connection.Open();

    // DDL, define a table
    Console.WriteLine("Drop table test if exists");
    var command = new PolyphenyCommand().
        WithConnection(connection).
        WithCommandText("DROP TABLE IF EXISTS test");
    command.ExecuteNonQuery();

    Console.WriteLine("Create table test");
    command = new PolyphenyCommand().
        WithConnection(connection).
        WithCommandText("CREATE TABLE test (id INT PRIMARY KEY, name VARCHAR(100))");
    command.ExecuteNonQuery();

    connection.Close();
}
```

#### Insert Data
```csharp
using (var connection = PolyphenyDriver.OpenConnection("localhost:20590,pa:"))
{
    connection.Open();

    // DML, insert a row
    Console.WriteLine("Insert a row into test");
    command = new PolyphenyCommand().
        WithConnection(connection).
        WithCommandText("INSERT INTO test (id, name) VALUES (1, 'inserted')");
    var rowsAffected = command.ExecuteNonQuery();

    connection.Close();
}
```

#### Update Data
```csharp
using (var connection = PolyphenyDriver.OpenConnection("localhost:20590,pa:"))
{
    connection.Open();

    // DML, update a row
    Console.WriteLine("Update a row in test");
    command = new PolyphenyCommand().
        WithConnection(connection).
        WithCommandText("UPDATE test SET name = 'updated' WHERE id = 1");
    var rowsAffected = command.ExecuteNonQuery();

    connection.Close();
}
```

#### Delete Data
```csharp
using (var connection = PolyphenyDriver.OpenConnection("localhost:20590,pa:"))
{
    connection.Open();

    // DML, update a row
    Console.WriteLine("Update a row in test");
    command = new PolyphenyCommand().
        WithConnection(connection).
        WithCommandText("DELETE FROM test WHERE id = 1");
    var rowsAffected = command.ExecuteNonQuery();

    connection.Close();
}
```

### Prepare SQL Statement
This driver also provides the functionality to sanitize SQL statement, via prepare functionality. By using this feature, we can fill SQL statement template safely. Here is the code example to use prepare SQL statement using this driver

```csharp
using (var connection = PolyphenyDriver.OpenConnection("localhost:20590,pa:"))
{
    connection.Open();

    // DML, insert a row
    Console.WriteLine("Insert a row into test");
    command = new PolyphenyCommand().
        WithConnection(connection).
        WithCommandText("INSERT INTO test (id, name) VALUES (?, ?)").
        WithParameterValues(new object[] { 1, "inserted" });
    command.Prepare();
    var rowsAffected = command.ExecuteNonQuery();

    // DML, update a row
    Console.WriteLine("Update a row in test");
    command = new PolyphenyCommand().
        WithConnection(connection).
        WithCommandText("UPDATE test SET name = ? WHERE id = ?").
        WithParameterValues(new object[] { "updated", 1 });
    command.Prepare();
    rowsAffected = command.ExecuteNonQuery();

    connection.Close();
}
```

### Execute SQL Transaction
This driver also provides the functionality to use SQL transaction. This feature is implemented by providing two interfaces, which are `Commit` to permanently save the changes and `Rollback` to undone the changes. Here is the code example to execute SQL transaction using this driver

```csharp
using (var connection = PolyphenyDriver.OpenConnection("localhost:20590,pa:"))
{
    connection.Open();

    var transaction1 = connection.BeginTransaction();

    // DML, insert a row
    Console.WriteLine("Insert a row into test");
    command = new PolyphenyCommand().
        WithConnection(connection).
        WithTransaction(transaction1).
        WithCommandText("INSERT INTO test (id, name) VALUES (1, 'inserted')");
    command.ExecuteNonQuery();

    transaction1.Commit();

    var transaction2 = connection.BeginTransaction();

    // DML, update a row
    Console.WriteLine("Update a row in test");
    command = new PolyphenyCommand().
        WithConnection(connection).
        WithTransaction(transaction2).
        WithCommandText("UPDATE test SET name = 'updated' WHERE id = 1");
    command.ExecuteNonQuery();

    transaction2.Rollback();

    // SELECT to verify the update
    Console.WriteLine("Select from test");
    command = new PolyphenyCommand().
        WithConnection(connection).
        WithCommandText("SELECT * FROM test");
    var reader = command.ExecuteReader();
    var columns = reader.Columns;

    while (reader.Read())
    {
        foreach (var column in columns)
        {
            Console.WriteLine($"{column}: {reader[column]}");
            // id: 1
            // name: inserted
        }
    }

    connection.Close();
}
```

### Execute Document Query
As Polypheny-DB also provides the functionality to store document-based NoSQL database, this driver also provides the functionality to access the database. We can use Mongo-like query to access this type of database. Here is the code example to execute document query using this driver

```csharp
using (var connection = PolyphenyDriver.OpenConnection("localhost:20590,pa:"))
{
    connection.Open();

    var command = new PolyphenyCommand().
        WithConnection(connection).
        WithCommandText("db.emps.find()");
    var result = command.ExecuteQueryMongo();

    Console.WriteLine("Number of records: " + result.Length);
    Console.WriteLine("Result: ");
    Console.WriteLine("-----");
    foreach (var dictionary in result)
    {
        foreach (KeyValuePair<object, object> kvp in dictionary)
        {
            Console.WriteLine("Key = {0}, Value = {1}", kvp.Key, kvp.Value);
        }
        Console.WriteLine("-----");
    }

    connection.Close();
}
```

## How To Test
In this project, we have implemented both mixed of end-to-end and unit tests. The test implementation can be access inside `PolyphenyDotNetDriver.Tests` directory. As the tests are partly of end-to-end tests, we need to setup the Polypheny-DB server first, as explained on the [Setup Polypheny-DB](#setup-polypheny-db) section.

To test the project, we can directly test by using this dotnet command inside the `PolyphenyDotNetDriver.Tests` directory
```
dotnet test
```

Alternatively, if we want to see the detailed report of the code coverage of the driver, we can use shell script `test.sh`. However, we need to install `reportgenerator` tool globally first

```
dotnet tool install -g dotnet-reportgenerator-globaltool
```

We can use this command to run the test shell script 
```
./test.sh
```

## Roadmap
See the [open issues](https://github.com/polypheny/Polypheny-DB/issues) for a list of proposed features (and known issues).


## Contributing
We highly welcome your contributions to the _Polypheny .NET Driver_. If you would like to contribute, please fork the repository and submit your changes as a pull request. Please consult our [Admin Repository](https://github.com/polypheny/Admin) and our [Website](https://polypheny.org) for guidelines and additional information.

Please note that we have a [code of conduct](https://github.com/polypheny/Admin/blob/master/CODE_OF_CONDUCT.md). Please follow it in all your interactions with the project. 


## License
The Apache 2.0 License
