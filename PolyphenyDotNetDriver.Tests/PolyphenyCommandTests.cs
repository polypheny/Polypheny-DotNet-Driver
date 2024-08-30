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
using Polypheny.Prism;
using PolyphenyDotNetDriver.Interface;

namespace PolyphenyDotNetDriver.Tests;

public class PolyphenyCommandTests
{
    private Mock<IPolyphenyConnection> _mockConnection;
    
    [SetUp]
    public void SetUp()
    {
        _mockConnection = new Mock<IPolyphenyConnection>();
    }
    
    [Test]
    public void WithConnection_WithNonDbConnection_MakeConnectionNull()
    {
        var connection = _mockConnection.Object;
        var cmd = new PolyphenyCommand().WithConnection(connection);
        Assert.That(cmd.Connection, Is.EqualTo(null));
    }
    
    [Test]
    public void WithTransaction_WithMismatchConnection_ThrowsException()
    {
        var connection = _mockConnection.Object;
        var connection2 = new Mock<IPolyphenyConnection>().Object;
            
        var trx = new PolyphenyTransaction(connection2, IsolationLevel.Serializable);

        Assert.Throws<Exception>(() =>
        {
            var cmd = new PolyphenyCommand().WithConnection(connection).WithTransaction(trx);
        });
    }

    [Test]
    public void ExecuteNonQuery_WithNullConnection_ThrowsInvalidOperationException()
    {
        var cmd = new PolyphenyCommand();
        Assert.Throws<InvalidOperationException>(() =>
        {
            cmd.ExecuteNonQuery();
        });        
    }

    [Test]
    public void ExecuteQueryMongo_WithNullConnection_ThrowsInvalidOperationException()
    {
        var cmd = new PolyphenyCommand();
        Assert.Throws<InvalidOperationException>(() =>
        {
            cmd.ExecuteQueryMongo();
        });     
    }
    
    [Test]
    public void ExecuteQueryMongo_WithMismatchStatementId_ThrowsException()
    {
        var response = new Response()
        {
            StatementResponse = new StatementResponse()
            {
                StatementId = 1,
            }
        };
        _mockConnection.Setup(x => x.SendRecv(It.IsAny<Request>())).ReturnsAsync(response);

        var response2 = new Response()
        {
            StatementResponse = new StatementResponse()
            {
                StatementId = 2,
            }
        };
        _mockConnection.Setup(x => x.Receive(8)).ReturnsAsync(response2);

        var reader = new PolyphenyCommand().WithConnection(_mockConnection.Object);

        Assert.Throws<Exception>( () =>
        {
            reader.ExecuteQueryMongo();
        });
    }
    
    [Test]
    public void ExecuteQueryMongo_WithNullDocumentFrame_ThrowsException()
    {
        var response = new Response()
        {
            StatementResponse = new StatementResponse()
            {
                StatementId = 1,
            }
        };
        _mockConnection.Setup(x => x.SendRecv(It.IsAny<Request>())).ReturnsAsync(response);

        var response2 = new Response()
        {
            StatementResponse = new StatementResponse()
            {
                StatementId = 1,
                Result = new StatementResult()
                {
                    Frame = new Frame()
                    {
                        DocumentFrame = null,
                    }
                }
            }
        };
        _mockConnection.Setup(x => x.Receive(8)).ReturnsAsync(response2);

        var reader = new PolyphenyCommand().WithConnection(_mockConnection.Object);

        Assert.Throws<Exception>( () =>
        {
            reader.ExecuteQueryMongo();
        });
    }
    
    [Test]
    public void ExecuteQueryMongo_WithNullFrame_ThrowsException()
    {
        var response = new Response()
        {
            StatementResponse = new StatementResponse()
            {
                StatementId = 1,
            }
        };
        _mockConnection.Setup(x => x.SendRecv(It.IsAny<Request>())).ReturnsAsync(response);

        var response2 = new Response()
        {
            StatementResponse = new StatementResponse()
            {
                StatementId = 1,
                Result = new StatementResult()
                {
                    Frame = null
                }
            }
        };
        _mockConnection.Setup(x => x.Receive(8)).ReturnsAsync(response2);

        var reader = new PolyphenyCommand().WithConnection(_mockConnection.Object);

        Assert.Throws<Exception>( () =>
        {
            reader.ExecuteQueryMongo();
        });
    }
    
    [Test]
    public void ExecuteQueryMongo_WithResult_ThrowsException()
    {
        var response = new Response()
        {
            StatementResponse = new StatementResponse()
            {
                StatementId = 1,
            }
        };
        _mockConnection.Setup(x => x.SendRecv(It.IsAny<Request>())).ReturnsAsync(response);

        var response2 = new Response()
        {
            StatementResponse = new StatementResponse()
            {
                StatementId = 1,
                Result = null,
            }
        };
        _mockConnection.Setup(x => x.Receive(8)).ReturnsAsync(response2);

        var reader = new PolyphenyCommand().WithConnection(_mockConnection.Object);

        Assert.Throws<Exception>( () =>
        {
            reader.ExecuteQueryMongo();
        });
    }
    
    [Test]
    public void ExecuteNonQuery_WithMismatchStatementId_ThrowsException()
    {
        var response = new Response()
        {
            StatementResponse = new StatementResponse()
            {
                StatementId = 1,
            }
        };
        _mockConnection.Setup(x => x.SendRecv(It.IsAny<Request>())).ReturnsAsync(response);

        var response2 = new Response()
        {
            StatementResponse = new StatementResponse()
            {
                StatementId = 2,
            }
        };
        _mockConnection.Setup(x => x.Receive(8)).ReturnsAsync(response2);

        var reader = new PolyphenyCommand().WithConnection(_mockConnection.Object);

        Assert.Throws<Exception>( () =>
        {
            reader.ExecuteNonQuery();
        });
    }
    
    [Test]
    public void Prepare_WithNullConnection_ThrowsInvalidOperationException()
    {
        var cmd = new PolyphenyCommand();
        Assert.Throws<InvalidOperationException>(() =>
        {
            cmd.Prepare();
        });        
    }
    
    [Test]
    public void ExecuteNonQueryPrepared_WithNullParameterValue_ThrowsInvalidOperationException()
    {
        var response = new Response()
        {
            PreparedStatementSignature = new PreparedStatementSignature()
            {
                ParameterMetas = {  },
                StatementId = 1,
            }
        };
        _mockConnection.Setup(x => x.SendRecv(It.IsAny<Request>())).ReturnsAsync(response);
        
        var cmd = new PolyphenyCommand().WithConnection(_mockConnection.Object);
        cmd.Prepare();
        Assert.Throws<InvalidOperationException>(() =>
        {
            cmd.ExecuteNonQuery();
        });        
    }
}
