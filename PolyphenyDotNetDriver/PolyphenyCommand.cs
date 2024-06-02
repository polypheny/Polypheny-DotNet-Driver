using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Polypheny.Prism;

namespace PolyphenyDotNetDriver
{
    public class PolyphenyCommand : DbCommand
    {
        public PolyphenyCommand WithConnection(PolyphenyConnection connection)
        {
            this.PolyphenyConnection = connection;
            return this;
        }
        
        public PolyphenyCommand WithCommandText(string commandText)
        {
            this.CommandText = commandText;
            return this;
        }
        
        // TODO: with Polypheny Transaction
        
        public override void Cancel()
        {
            throw new System.NotImplementedException();
        }

        public override int ExecuteNonQuery() => ExecuteNonQueryAsync(CancellationToken.None).GetAwaiter().GetResult();

        public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            if (PolyphenyConnection == null)
            {
                throw new InvalidOperationException("Connection property must be non-null");
            }

            var request = new Request()
            {
                ExecuteUnparameterizedStatementRequest = new ExecuteUnparameterizedStatementRequest()
                {
                    LanguageName = "sql",
                    Statement = CommandText,
                }
            };

            var response = await this.PolyphenyConnection.SendRecv(request);
            var tmpId = response?.StatementResponse?.StatementId;

            var newResponse = await this.PolyphenyConnection.Receive();
            if (newResponse?.StatementResponse?.StatementId != tmpId)
            {
                return -1;
            }

            var stmtResult = newResponse?.StatementResponse?.Result;
            var result = ProtoHelper.FormatResult(stmtResult);
            
            return (int)result.RowsAffected;
        }

        public override object ExecuteScalar()
        {
            throw new System.NotImplementedException();
        }

        public override void Prepare()
        {
            
        } //=> PrepareAsync(CancellationToken.None).GetAwaiter().GetResult();
        
        // protected async Task PrepareAsync(CancellationToken cancellationToken)
        // {
        //     if (PolyphenyConnection == null)
        //     {
        //         throw new InvalidOperationException("Connection property must be non-null");
        //     }
        //     
        //     // TODO
        //     
        // }

        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; } = 30;
        public override CommandType CommandType { get; set; } = CommandType.Text;
        public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.Both;

        protected PolyphenyConnection PolyphenyConnection;
        protected override DbConnection DbConnection
        {
            get => this.PolyphenyConnection;
            set
            {
                if (value is PolyphenyConnection polyphenyConnection)
                {
                    this.PolyphenyConnection = polyphenyConnection;
                }
                else
                {
                    throw new InvalidOperationException("Connection must be a PolyphenyConnection");
                }
            }
        }

        protected override DbParameterCollection DbParameterCollection { get; }
        protected override DbTransaction DbTransaction { get; set; }
        public override bool DesignTimeVisible { get; set; }

        protected override DbParameter CreateDbParameter()
        {
            throw new System.NotImplementedException();
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            throw new System.NotImplementedException();
        }
    }
}