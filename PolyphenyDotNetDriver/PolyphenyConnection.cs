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

        public override string Database => "PolyphenyDatabase";

        public override ConnectionState State
        {
            get
            {
                return this._isConnected switch
                {
                    StatusDisconnected => ConnectionState.Closed,
                    StatusServerConnected => ConnectionState.Connecting,
                    StatusPolyphenyConnected => ConnectionState.Open,
                    _ => ConnectionState.Broken
                };
            }
        }

        public override string DataSource => "PolyphenyDataSource";
        public override string ServerVersion => TransportVersion;

        private int _isConnected = StatusDisconnected;
        public int IsConnected => this._isConnected;

        private const string TransportVersion = "plain-v1@polypheny.com\n";

        private const int StatusDisconnected = 0;
        private const int StatusServerConnected = 1;
        private const int StatusPolyphenyConnected = 2;

        private string _hostname;
        private int _port;
        private string _username;
        private string _password;

        private TcpClient _client;
        private NetworkStream _stream;

        public PolyphenyConnection(string connectionString)
        {
            this._connectionString = connectionString;
            UpdateConnectionString();
            Interlocked.Exchange(ref this._isConnected, StatusDisconnected);
        }

        public override void Close() => CloseAsync(CancellationToken.None).GetAwaiter().GetResult();

        private async Task CloseAsync(CancellationToken cancellationToken)
        {
            var request = new Request()
            {
                DisconnectRequest = new DisconnectRequest()
            };
            await SendRecv(request);
            Interlocked.Exchange(ref this._isConnected, StatusServerConnected);

            try
            {
                await this._stream.FlushAsync(cancellationToken);
                this._stream.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                this._client.Close();
                Interlocked.Exchange(ref this._isConnected, StatusDisconnected);
            }
        }
        
        public override void Open() => OpenAsync(CancellationToken.None).GetAwaiter().GetResult();

        public override async Task OpenAsync(CancellationToken cancellationToken)
        {
            this._client = new TcpClient(this._hostname, this._port);
            this._stream = this._client.GetStream();
            Interlocked.Exchange(ref this._isConnected, StatusServerConnected);

            var recvVersion = await this.RawReceive(1);

            var sendVersion = System.Text.Encoding.ASCII.GetBytes(TransportVersion);
            await this.RawSend(sendVersion, 1);

            if (!recvVersion.SequenceEqual(sendVersion))
            {
                throw new Exception("The transport version is incompatible with server");
            }

            var request = new Request()
            {
                ConnectionRequest = new ConnectionRequest()
                {
                    MajorApiVersion = Convert.ToInt32(Version.Major),
                    MinorApiVersion = Convert.ToInt32(Version.Minor),
                    Username = this._username,
                    Password = this._password,
                }
            };

            await SendRecv(request);
            Interlocked.Exchange(ref this._isConnected, StatusPolyphenyConnected);
        }

        public async Task Ping(CancellationToken cancellationToken = default)
        {
            var status = this._isConnected;
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

        private void UpdateConnectionString()
        {
            var split = ConnectionString.Split(',');
            if (split.Length != 2)
            {
                throw new Exception("Invalid connection string");
            }

            var hostnameAndPort = split[0].Split(':');
            if (hostnameAndPort.Length != 2)
            {
                throw new Exception("Invalid connection string");
            }

            var usernameAndPassword = split[1].Split(':');
            if (usernameAndPassword.Length != 2)
            {
                throw new Exception("Invalid connection string");
            }

            this._hostname = hostnameAndPort[0];
            this._port = int.Parse(hostnameAndPort[1]);
            this._username = usernameAndPassword[0];
            this._password = usernameAndPassword[1];
        }

        private async Task<byte[]> RawReceive(int lengthSize)
        {
            if (lengthSize > 8)
            {
                throw new Exception("Invalid length size, expect the lengthSize parameter is not greater than 8");
            }

            var lengthBuf = new byte[8];
            var length = new byte[lengthSize];
            var bytesRead = await _stream.ReadAsync(length.AsMemory(0, lengthSize));
            if (bytesRead != lengthSize)
            {
                throw new Exception("Failed to read the specified number of bytes");
            }

            for (var i = 0; i < lengthBuf.Length; i++)
            {
                if (i < lengthSize)
                    lengthBuf[i] = length[i];
                else
                    lengthBuf[i] = 0;
            }

            var recvLength = BitConverter.ToUInt64(lengthBuf, 0);
            var buf = new byte[recvLength];
            bytesRead = await _stream.ReadAsync(buf.AsMemory(0, (int)recvLength));
            if ((ulong)bytesRead != recvLength)
            {
                throw new Exception("Failed to read the specified number of bytes");
            }

            return buf;
        }

        private async Task RawSend(byte[] serialized, int lengthSize)
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

            for (var i = 0; i < lengthSize; i++)
            {
                length[i] = lengthBuf[i];
            }

            await _stream.WriteAsync(length);
            await _stream.WriteAsync(serialized);
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