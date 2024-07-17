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
