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

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Polypheny.Prism;
using PolyphenyDotNetDriver.Interface;
using Version = Polypheny.Prism.Version;

namespace PolyphenyDotNetDriver
{
    public class PolyphenyConnection : DbConnection, IPolyphenyConnection
    {
        protected override PolyphenyTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            return new PolyphenyTransaction(this, isolationLevel);
        }

        public new PolyphenyTransaction BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.Unspecified)
        {
            return BeginDbTransaction(isolationLevel);
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

        public override ConnectionState State
        {
            get
            {
                return this._isConnected switch
                {
                    PolyphenyConnectionState.StatusDisconnected => ConnectionState.Closed,
                    PolyphenyConnectionState.StatusServerConnected => ConnectionState.Connecting,
                    PolyphenyConnectionState.StatusPolyphenyConnected => ConnectionState.Open,
                    _ => ConnectionState.Broken
                };
            }
        }

        public override string DataSource { get; }
        public override string ServerVersion => TransportVersion;

        private int _isConnected = PolyphenyConnectionState.StatusDisconnected;
        public int IsConnected => this._isConnected;

        private string TransportVersion { get; }
        private bool AutoCommit { get; }

        private string _hostname;
        private int _port;
        private string _username;
        private string _password;

        private TcpClient _client;
        private NetworkStream _stream;
        private readonly Queue<ulong> _pendingRequestIds = new();
        private long _sessionVersion;
        internal long SessionVersion => Volatile.Read(ref _sessionVersion);

        public PolyphenyConnection(string connectionString, PolyphenyConfig config)
        {
            Database = config.Database;
            DataSource = config.DataSource;
            TransportVersion = config.TransportVersion;
            AutoCommit = config.AutoCommit;
            
            this._connectionString = connectionString;
            UpdateConnectionString();
            Interlocked.Exchange(ref this._isConnected, PolyphenyConnectionState.StatusDisconnected);
        }

        public override void Close() => CloseAsync().GetAwaiter().GetResult();

        public override async Task CloseAsync()
        {
            if (State == ConnectionState.Closed)
            {
                return;
            }

            try
            {
                if (State == ConnectionState.Open && _pendingRequestIds.Count == 0)
                {
                    await SendRecv(new Request { DisconnectRequest = new DisconnectRequest() }).ConfigureAwait(false);
                }
            }
            catch (PolyphenyException)
            {
                // Disconnect ends the session even when the server replies with an error.
            }
            finally
            {
                DisposeTransport();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeTransport();
            }
            base.Dispose(disposing);
        }

        private void DisposeTransport()
        {
            this._client?.Dispose();
            this._client = null;
            this._stream = null;
            this._pendingRequestIds.Clear();
            Interlocked.Exchange(ref this._isConnected, PolyphenyConnectionState.StatusDisconnected);
        }

        public override void Open() => OpenAsync(CancellationToken.None).GetAwaiter().GetResult();

        public override async Task OpenAsync(CancellationToken cancellationToken)
        {
            if (State != ConnectionState.Closed)
            {
                throw new InvalidOperationException("The connection is already open or opening.");
            }

            this._client = new TcpClient();
            Interlocked.Increment(ref _sessionVersion);
            Interlocked.Exchange(ref this._isConnected, PolyphenyConnectionState.StatusServerConnected);

            try
            {
                await this._client.ConnectAsync(this._hostname, this._port, cancellationToken).ConfigureAwait(false);
                this._stream = this._client.GetStream();
                var recvVersion = await RawReceive(1, cancellationToken).ConfigureAwait(false);
                var sendVersion = System.Text.Encoding.ASCII.GetBytes(TransportVersion);
                if (!recvVersion.SequenceEqual(sendVersion))
                {
                    throw new InvalidOperationException("The transport version is incompatible with the server.");
                }
                await RawSend(sendVersion, 1, cancellationToken).ConfigureAwait(false);

                var request = new Request
                {
                    ConnectionRequest = new ConnectionRequest
                    {
                        MajorApiVersion = Convert.ToInt32(Version.Major),
                        MinorApiVersion = Convert.ToInt32(Version.Minor),
                        Username = this._username,
                        Password = this._password,
                        ConnectionProperties = new ConnectionProperties { IsAutoCommit = AutoCommit }
                    }
                };

                var response = await SendRecv(request, cancellationToken).ConfigureAwait(false);
                if (response.ConnectionResponse?.IsCompatible != true)
                {
                    throw new InvalidOperationException("The Prism API version is incompatible with the server.");
                }
                Interlocked.Exchange(ref this._isConnected, PolyphenyConnectionState.StatusPolyphenyConnected);
            }
            catch
            {
                DisposeTransport();
                throw;
            }
        }

        /// <summary>Searches for entities in the specified Polypheny namespace.</summary>
        /// <param name="namespaceName">The namespace to search.</param>
        /// <param name="entityPattern">An optional entity name pattern. Null returns all entities.</param>
        public IReadOnlyList<Entity> SearchEntities(string namespaceName, string entityPattern = null)
            => SearchEntitiesAsync(namespaceName, entityPattern).GetAwaiter().GetResult();

        /// <summary>Asynchronously searches for entities in the specified Polypheny namespace.</summary>
        /// <param name="namespaceName">The namespace to search.</param>
        /// <param name="entityPattern">An optional entity name pattern. Null returns all entities.</param>
        public async Task<IReadOnlyList<Entity>> SearchEntitiesAsync(string namespaceName, string entityPattern = null)
        {
            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                throw new ArgumentException("A namespace name is required.", nameof(namespaceName));
            }
            if (State != ConnectionState.Open)
            {
                throw new InvalidOperationException("Open the connection before searching for entities.");
            }

            var request = new EntitiesRequest { NamespaceName = namespaceName };
            if (entityPattern != null)
            {
                request.EntityPattern = entityPattern;
            }

            var response = await SendRecv(new Request { EntitiesRequest = request }).ConfigureAwait(false);
            return response.EntitiesResponse?.Entities
                ?? throw new InvalidOperationException("Expected a Prism entities response.");
        }

        public async Task Ping(CancellationToken cancellationToken = default)
        {
            var status = this._isConnected;
            switch (status)
            {
                case PolyphenyConnectionState.StatusDisconnected:
                    throw new Exception("Bad Connection");
                case PolyphenyConnectionState.StatusServerConnected:
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

        private async Task<byte[]> RawReceive(int lengthSize, CancellationToken cancellationToken = default)
        {
            if (lengthSize is < 1 or > 8)
            {
                throw new ArgumentOutOfRangeException(nameof(lengthSize));
            }

            var stream = _stream ?? throw new InvalidOperationException("The connection is closed.");
            var lengthBuf = new byte[8];
            await ReadFully(stream, lengthBuf.AsMemory(0, lengthSize), cancellationToken).ConfigureAwait(false);
            var frameLength = BinaryPrimitives.ReadUInt64LittleEndian(lengthBuf);
            if (frameLength > int.MaxValue)
            {
                if (ReferenceEquals(_stream, stream))
                {
                    DisposeTransport();
                }
                throw new IOException("The Prism frame is too large.");
            }

            var buf = new byte[(int)frameLength];
            await ReadFully(stream, buf, cancellationToken).ConfigureAwait(false);
            return buf;
        }

        private async Task ReadFully(NetworkStream stream, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            try
            {
                while (!buffer.IsEmpty)
                {
                    var bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        throw new EndOfStreamException("The server closed the connection in the middle of a Prism frame.");
                    }
                    buffer = buffer[bytesRead..];
                }
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            {
                if (ReferenceEquals(_stream, stream))
                {
                    DisposeTransport();
                }
                throw;
            }
        }

        private async Task RawSend(byte[] serialized, int lengthSize, CancellationToken cancellationToken = default)
        {
            if (lengthSize is < 1 or > 8)
            {
                throw new ArgumentOutOfRangeException(nameof(lengthSize));
            }

            if (serialized.Length >= Math.Pow(2, lengthSize * 8 - 1))
            {
                throw new Exception(
                    "The size of the serialized message is too large to be put in a byte array of size lengthSize");
            }

            var lengthBuf = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(lengthBuf, (ulong)serialized.Length);
            var stream = _stream ?? throw new InvalidOperationException("The connection is closed.");
            try
            {
                await stream.WriteAsync(lengthBuf.AsMemory(0, lengthSize), cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(serialized, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            {
                if (ReferenceEquals(_stream, stream))
                {
                    DisposeTransport();
                }
                throw;
            }
        }

        public Task Send(Request req, int lengthSize=8) => Send(req, lengthSize, CancellationToken.None);

        private async Task Send(Request request, int lengthSize, CancellationToken cancellationToken)
        {
            var stream = _stream;
            var requestId = request.Id;
            await RawSend(request.ToByteArray(), lengthSize, cancellationToken).ConfigureAwait(false);
            if (ReferenceEquals(_stream, stream))
            {
                _pendingRequestIds.Enqueue(requestId);
            }
        }
        
        public Task<Response> Receive(int lengthSize=8) => Receive(lengthSize, CancellationToken.None);

        private async Task<Response> Receive(int lengthSize, CancellationToken cancellationToken, bool statementClose = false)
        {
            var stream = _stream;
            ulong? requestId = _pendingRequestIds.TryPeek(out var pendingId) ? pendingId : null;
            var payload = await RawReceive(lengthSize, cancellationToken).ConfigureAwait(false);
            try
            {
                var response = Response.Parser.ParseFrom(payload);
                PrismResponse.ValidateRequestId(response, requestId);
                if (requestId.HasValue && response.Last && ReferenceEquals(_stream, stream))
                {
                    _pendingRequestIds.TryDequeue(out _);
                }
                if (statementClose)
                {
                    PrismResponse.ValidateStatementClose(response);
                }
                else
                {
                    PrismResponse.ThrowIfError(response);
                }
                return response;
            }
            catch (Exception error) when (error is InvalidDataException or InvalidProtocolBufferException)
            {
                // There may still be unread frames for the pending request.
                if (ReferenceEquals(_stream, stream))
                {
                    DisposeTransport();
                }
                throw;
            }
        }

        public Task<Response> SendRecv(Request m) => SendRecv(m, CancellationToken.None);

        private async Task<Response> SendRecv(Request request, CancellationToken cancellationToken)
        {
            if (_pendingRequestIds.Count != 0)
            {
                throw new InvalidOperationException("Read the pending Prism responses before starting another request.");
            }
            var statementClose = request.CloseStatementRequest != null;
            await Send(request, 8, cancellationToken).ConfigureAwait(false);
            return await Receive(8, cancellationToken, statementClose).ConfigureAwait(false);
        }
    }
}
