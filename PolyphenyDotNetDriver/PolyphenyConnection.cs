using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Polypheny.Prism;

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
            throw new NotImplementedException();
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
        protected const int MajorApiVersion = 1;
        protected const int MinorApiVersion = 9;

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

        public override void Close()
        {
            var request = new Request()
            {
                DisconnectRequest = new DisconnectRequest()
            };
            HelperSendAndRecv(request);
            Interlocked.Exchange(ref this._IsConnected, StatusServerConnected);

            try
            {
                this.Stream.Flush();
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

        public override void Open()
        {
            this.Client = new TcpClient(this.Hostname, this.Port);
            this.Stream = this.Client.GetStream();
            Interlocked.Exchange(ref this._IsConnected, StatusServerConnected);

            var recvVersion = this.Receive(1);

            var sendVersion = System.Text.Encoding.ASCII.GetBytes(TransportVersion);
            Console.WriteLine(sendVersion);
            this.Send(sendVersion, 1);

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
                    MajorApiVersion = MajorApiVersion,
                    MinorApiVersion = MinorApiVersion,
                    Username = this.Username,
                    Password = this.Password,
                }
            };

            var response = HelperSendAndRecv(request);
            Console.WriteLine(response);
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
                        var task = Task.Run(() => HelperSendAndRecv(request), cancellationToken);
                        await task;
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

        protected byte[] Receive(int lengthSize)
        {
            if (lengthSize > 8)
            {
                throw new Exception("Invalid length size, expect the lengthSize parameter is not greater than 8");
            }

            var lengthBuf = new byte[8];
            var length = new byte[lengthSize];
            var bytesRead = this.Stream.Read(length, 0, lengthSize);
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
            bytesRead = this.Stream.Read(buf, 0, (int)recvLength);
            if ((ulong)bytesRead != recvLength)
            {
                throw new Exception("Failed to read the specified number of bytes");
            }

            return buf;
        }

        protected void Send(byte[] serialized, int lengthSize)
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

            this.Stream.Write(length, 0, length.Length);
            this.Stream.Write(serialized, 0, serialized.Length);
        }

        protected Response HelperSendAndRecv(Request m)
        {
            var buf = m.ToByteArray();
            this.Send(buf, 8);
            var recv = this.Receive(8);
            var parser = new MessageParser<Response>(() => new Response());
            var result = parser.ParseFrom(recv);
            return result;
        }
    }
}