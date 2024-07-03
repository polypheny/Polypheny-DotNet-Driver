using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Polypheny.Prism;

namespace PolyphenyDotNetDriver;

public class PolyphenyTransaction: DbTransaction
{
    public PolyphenyTransaction(PolyphenyConnection connection, IsolationLevel isolationLevel)
    {
        PolyphenyConnection = connection;
        IsolationLevel = isolationLevel;
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

    public readonly PolyphenyConnection PolyphenyConnection;
    protected override DbConnection DbConnection => PolyphenyConnection;
    // TODO: use isolation level
    public override IsolationLevel IsolationLevel { get; }
}
