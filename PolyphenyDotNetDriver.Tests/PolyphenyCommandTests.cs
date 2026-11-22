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

    [TestCase(true)]
    [TestCase(false)]
    public void ExecuteNonQueryAsync_WithStubErrorReportsPrismDetails(bool beforeAcknowledgment)
    {
        var response = new Response
        {
            Last = true,
            ErrorResponse = new ErrorResponse
            {
                Message = "Statement rejected.",
                Details = new ErrorDetails { State = "42000", ErrorCode = 73 }
            }
        };
        var acknowledgment = new Response { StatementResponse = new StatementResponse { StatementId = 1 } };
        _mockConnection.Setup(x => x.SendRecv(It.IsAny<Request>())).ReturnsAsync(beforeAcknowledgment ? response : acknowledgment);
        _mockConnection.Setup(x => x.Receive(8)).ReturnsAsync(response);
        using var command = new PolyphenyCommand().WithConnection(_mockConnection.Object).WithCommandText("bad statement");

        var error = Assert.ThrowsAsync<PolyphenyException>(() => command.ExecuteNonQueryAsync(CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Is.EqualTo("Statement rejected."));
            Assert.That(error.SqlState, Is.EqualTo("42000"));
            Assert.That(error.ServerErrorCode, Is.EqualTo(73));
        });
        _mockConnection.Verify(x => x.Receive(8), beforeAcknowledgment ? Times.Never() : Times.Once());
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
    public void ExecuteQueryMongo_DecodesPrismDocumentMap()
    {
        var nested = new ProtoDocument
        {
            Entries = { { "severity", new ProtoValue { Integer = new ProtoInteger { Integer = 4 } } } }
        };
        var document = new ProtoDocument
        {
            Entries =
            {
                { "patient_id", new ProtoValue { String = new ProtoString { String = "p1" } } },
                { "nested", new ProtoValue { Document = nested } },
                {
                    "tags", new ProtoValue
                    {
                        List = new ProtoList
                        {
                            Values =
                            {
                                new ProtoValue { String = new ProtoString { String = "fever" } },
                                new ProtoValue { Null = new ProtoNull() }
                            }
                        }
                    }
                }
            }
        };

        _mockConnection.Setup(x => x.SendRecv(It.Is<Request>(request =>
            request.ExecuteUnparameterizedStatementRequest.NamespaceName == "patients"
            && request.ExecuteUnparameterizedStatementRequest.Statement == "db.symptoms.find()"))).ReturnsAsync(new Response
        {
            StatementResponse = new StatementResponse { StatementId = 1 }
        });
        _mockConnection.Setup(x => x.Receive(8)).ReturnsAsync(new Response
        {
            StatementResponse = new StatementResponse
            {
                StatementId = 1,
                Result = new StatementResult
                {
                    Frame = new Frame
                    {
                        IsLast = true,
                        DocumentFrame = new DocumentFrame { Documents = { document } }
                    }
                }
            }
        });

        var command = new PolyphenyCommand().WithConnection(_mockConnection.Object).WithCommandText("db.symptoms.find()");
        var results = command.ExecuteQueryMongo("patients");

        Assert.That(results, Has.Length.EqualTo(1));
        Assert.That(results[0]["patient_id"], Is.EqualTo("p1"));
        Assert.That(((Dictionary<object, object>)results[0]["nested"])["severity"], Is.EqualTo(4));
        Assert.That(results[0]["tags"], Is.EqualTo(new object?[] { "fever", null }));
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
