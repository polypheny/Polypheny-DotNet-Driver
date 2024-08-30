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
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Polypheny.Prism;
using PolyphenyDotNetDriver.Interface;

namespace PolyphenyDotNetDriver;

public class PolyphenyTransaction: DbTransaction
{
    public PolyphenyTransaction(IPolyphenyConnection connection, IsolationLevel isolationLevel)
    {
        PolyphenyConnection = connection;
        IsolationLevel = isolationLevel;

        if (connection is DbConnection c)
        {
            this.DbConnection = c;
        }
        else
        {
            this.DbConnection = null;
        }
    }

    public override void Commit() => CommitAsync(CancellationToken.None).GetAwaiter().GetResult();

    public override async Task CommitAsync(CancellationToken cancellationToken)
    {
        var request = new Request()
        {
            CommitRequest = new CommitRequest()
        };
        
        await PolyphenyConnection.SendRecv(request);
    }

    public override void Rollback() => RollbackAsync(CancellationToken.None).GetAwaiter().GetResult();

    public override async Task RollbackAsync(CancellationToken cancellationToken)
    {
        var request = new Request()
        {
            RollbackRequest = new RollbackRequest()
        };

        await PolyphenyConnection.SendRecv(request);
    }

    public readonly IPolyphenyConnection PolyphenyConnection;
    // TODO: use isolation level
    protected override DbConnection DbConnection { get; }
    public override IsolationLevel IsolationLevel { get; }
}
