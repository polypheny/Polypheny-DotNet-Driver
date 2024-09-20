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

using System.Data;
using Moq;
using PolyphenyDotNetDriver.Interface;

namespace PolyphenyDotNetDriver.Tests;

public class PolyphenyConnectionTests
{
    [SetUp]
    public void SetUp()
    {
        
    }

    [Test]
    public void CreateDbCommand_ReturnPolyphenyCommand()
    {
        var connection = new PolyphenyConnection("localhost:20590,pa:", new PolyphenyConfig());
        var cmd = connection.CreateCommand();
        Assert.That(cmd.Connection, Is.EqualTo(connection));
    }
    
    [Test]
    public void InvalidConnectionString_ThrowsException()
    {
        Assert.Throws<Exception>(() =>
        {
            var connection = new PolyphenyConnection("localhost:20590", new PolyphenyConfig());
        });
    }
    
    [Test]
    public void InvalidConnectionString2_ThrowsException()
    {
        Assert.Throws<Exception>(() =>
        {
            var connection = new PolyphenyConnection("localhost,", new PolyphenyConfig());
        });
    }
    
    [Test]
    public void InvalidConnectionString3_ThrowsException()
    {
        Assert.Throws<Exception>(() =>
        {
            var connection = new PolyphenyConnection("localhost:20590,pa", new PolyphenyConfig());
        });
    }
    
    [Test]
    public void SetConnectionString()
    {
        var connection = new PolyphenyConnection("localhost:20590,pa:", new PolyphenyConfig());
        connection.ConnectionString = "localhost:20591,pa:";
    }
    
    [Test]
    public void ServerVersion()
    {
        var connection = new PolyphenyConnection("localhost:20590,pa:", new PolyphenyConfig()
        {
            TransportVersion = "transport-version"
        });
        Assert.That(connection.ServerVersion, Is.EqualTo("transport-version"));
    }
    
    [Test]
    public void IsConnected_ReturnDisconnected()
    {
        var connection = new PolyphenyConnection("localhost:20590,pa:", new PolyphenyConfig());
        Assert.That(connection.IsConnected, Is.EqualTo(PolyphenyConnectionState.StatusDisconnected));
    }
    
    [Test]
    public void ConnectionState_ReturnClosed()
    {
        var connection = new PolyphenyConnection("localhost:20590,pa:", new PolyphenyConfig());
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
    }

    [Test]
    public void Ping_WhenClosedConnection_ThrowsException()
    {
        var connection = new PolyphenyConnection("localhost:20590,pa:", new PolyphenyConfig());
        Assert.ThrowsAsync<Exception>(async () =>
        {
            await connection.Ping();
        });
    }
}
