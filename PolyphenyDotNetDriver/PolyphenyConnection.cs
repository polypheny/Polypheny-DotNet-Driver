using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Polypheny.Prism;
using Version = Polypheny.Prism.Version;

namespace PolyphenyDotNetDriver
{
    public class PolyphenyConnection : DbConnection
    {
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotImplementedException();
        }

        public override void ChangeDatabase(string databaseName)
        {
            throw new NotImplementedException();
        }

        protected override DbCommand CreateDbCommand()
        {
            return new PolyphenyCommand().WithConnection(this);
        }

        private string _connectionString;
        public override string ConnectionString
        {
            get => this._connectionString;
            set
            {
                this._connectionString = value;
                UpdateConnectionString();
            }
        }

        public override string Database { get; }
        public override ConnectionState State { get; }
        public override string DataSource { get; }
        public override string ServerVersion { get; }

        protected int _IsConnected = StatusDisconnected;
        public int IsConnected => this._IsConnected;

        protected const string TransportVersion = "plain-v1@polypheny.com\n";

        protected const int StatusDisconnected = 0;
        protected const int StatusServerConnected = 1;
        protected const int StatusPolyphenyConnected = 2;

        protected string Hostname;
        protected int Port;
        protected string Username;
        protected string Password;

        protected TcpClient Client;
        protected NetworkStream Stream;

        public PolyphenyConnection(string connectionString)
        {
            this._connectionString = connectionString;
            UpdateConnectionString();
            Interlocked.Exchange(ref this._IsConnected, StatusDisconnected);
        }

        public override void Close() => CloseAsync(CancellationToken.None).GetAwaiter().GetResult();
        protected async Task CloseAsync(CancellationToken cancellationToken)
        {
            var request = new Request()
            {
                DisconnectRequest = new DisconnectRequest()
            };
            await SendRecv(request);
            Interlocked.Exchange(ref this._IsConnected, StatusServerConnected);

            try
            {
                await this.Stream.FlushAsync(cancellationToken);
                this.Stream.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                this.Client.Close();
                Interlocked.Exchange(ref this._IsConnected, StatusDisconnected);
            }
        }
        
        public override void Open() => OpenAsync(CancellationToken.None).GetAwaiter().GetResult();

        public override async Task OpenAsync(CancellationToken cancellationToken)
        {
            this.Client = new TcpClient(this.Hostname, this.Port);
            this.Stream = this.Client.GetStream();
            Interlocked.Exchange(ref this._IsConnected, StatusServerConnected);

            var recvVersion = await this.RawReceive(1);

            var sendVersion = System.Text.Encoding.ASCII.GetBytes(TransportVersion);
            Console.WriteLine(sendVersion);
            await this.RawSend(sendVersion, 1);

            if (!recvVersion.SequenceEqual(sendVersion))
            {
                throw new Exception("The transport version is incompatible with server");
            }

            Console.WriteLine("correct version");
            Console.WriteLine(recvVersion.ToString() + recvVersion.Length);
            Console.WriteLine(sendVersion.ToString() + sendVersion.Length);
            Console.WriteLine(TransportVersion);

            var request = new Request()
            {
                ConnectionRequest = new ConnectionRequest()
                {
                    MajorApiVersion = Convert.ToInt32(Version.Major),
                    MinorApiVersion = Convert.ToInt32(Version.Minor),
                    Username = this.Username,
                    Password = this.Password,
                }
            };

            await SendRecv(request);
            Interlocked.Exchange(ref this._IsConnected, StatusPolyphenyConnected);
        }

        public async Task Ping(CancellationToken cancellationToken = default)
        {
            var status = this._IsConnected;
            switch (status)
            {
                case StatusDisconnected:
                    throw new Exception("Bad Connection");
                case StatusServerConnected:
                    throw new Exception("Ping: invalid connection to Polypheny server");
                default:
                    {
                        var request = new Request()
                        {
                            ConnectionCheckRequest = new ConnectionCheckRequest()
                        };
                        // TODO: use cancellationToken
                        await SendRecv(request);
                        break;
                    }
            }
        }

        protected void UpdateConnectionString()
        {
            var splitted = ConnectionString.Split(',');
            if (splitted.Length != 2)
            {
                throw new Exception("Invalid connection string");
            }

            var hostnameAndPort = splitted[0].Split(':');
            if (hostnameAndPort.Length != 2)
            {
                throw new Exception("Invalid connection string");
            }

            var usernameAndPassword = splitted[1].Split(':');
            if (usernameAndPassword.Length != 2)
            {
                throw new Exception("Invalid connection string");
            }

            this.Hostname = hostnameAndPort[0];
            this.Port = int.Parse(hostnameAndPort[1]);
            this.Username = usernameAndPassword[0];
            this.Password = usernameAndPassword[1];
        }

        protected async Task<byte[]> RawReceive(int lengthSize)
        {
            if (lengthSize > 8)
            {
                throw new Exception("Invalid length size, expect the lengthSize parameter is not greater than 8");
            }

            var lengthBuf = new byte[8];
            var length = new byte[lengthSize];
            var bytesRead = await this.Stream.ReadAsync(length, 0, lengthSize);
            if (bytesRead != lengthSize)
            {
                Console.WriteLine(bytesRead + ":" + lengthSize);
                throw new Exception("Failed to read the specified number of bytes");
            }

            for (int i = 0; i < lengthBuf.Length; i++)
            {
                if (i < lengthSize)
                    lengthBuf[i] = length[i];
                else
                    lengthBuf[i] = 0;
            }

            var recvLength = BitConverter.ToUInt64(lengthBuf, 0);
            var buf = new byte[recvLength];
            bytesRead = await this.Stream.ReadAsync(buf, 0, (int)recvLength);
            if ((ulong)bytesRead != recvLength)
            {
                throw new Exception("Failed to read the specified number of bytes");
            }

            return buf;
        }

        protected async Task RawSend(byte[] serialized, int lengthSize)
        {
            if (lengthSize > 8)
            {
                throw new Exception("Invalid length size, expect the lengthSize parameter is not greater than 8");
            }

            var lengthBuf = new byte[8];
            var length = new byte[lengthSize];
            var lenSerialized = serialized.Length;

            if (lenSerialized >= Math.Pow(2, lengthSize * 8 - 1))
            {
                throw new Exception(
                    "The size of the serialized message is too large to be put in a byte array of size lengthSize");
            }

            BitConverter.GetBytes((ulong)lenSerialized).CopyTo(lengthBuf, 0);

            for (int i = 0; i < lengthSize; i++)
            {
                length[i] = lengthBuf[i];
            }

            await this.Stream.WriteAsync(length, 0, length.Length);
            await this.Stream.WriteAsync(serialized, 0, serialized.Length);
        }

        public async Task Send(Request req, int lengthSize=8)
        {
            var buf = req.ToByteArray();
            await this.RawSend(buf, lengthSize);
        }
        
        // add default lengthSize
        public async Task<Response> Receive(int lengthSize=8)
        {
            var recv = await this.RawReceive(lengthSize);
            var parser = new MessageParser<Response>(() => new Response());
            var result = parser.ParseFrom(recv);
            return result;
        }

        public async Task<Response> SendRecv(Request m)
        {
            await this.Send(m);
            return await this.Receive();
        }
    }
}