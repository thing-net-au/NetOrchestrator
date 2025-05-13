using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Orchestrator.IPC
{
    public static class SerializationExtensions
    {
        public static string ToJson<T>(this T obj)
            => JsonSerializer.Serialize(obj);

        public static T FromJson<T>(this string json)
            => JsonSerializer.Deserialize<T>(json);
    }

    public class MessageQueue<T>
    {
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
        private int _max;

        public MessageQueue(int maxHistory) => _max = maxHistory;

        public void Enqueue(T item)
        {
            _queue.Enqueue(item);
            while (_queue.Count > _max && _queue.TryDequeue(out _)) { }
        }

        public T[] GetHistory(int count)
        {
            var arr = _queue.ToArray();
            int take = Math.Min(count, arr.Length);
            var res = new T[take];
            Array.Copy(arr, arr.Length - take, res, 0, take);
            return res;
        }

        /// <summary>
        /// Change the maximum number of messages kept in history.
        /// </summary>
        public void SetMaxHistory(int maxHistory)
        {
            _max = maxHistory;
            // Optionally trim if current count > new max:
            while (_queue.Count > _max && _queue.TryDequeue(out _)) { }
        }
    }

    public class TcpJsonServer<T>
    {
        private readonly IPAddress _address;
        private readonly int _port;
        private readonly TcpListener _listener;
        private MessageQueue<T> _history;
        private int _replayCount;
        private readonly ConcurrentDictionary<int, (TcpClient Client, StreamWriter Writer)> _clients = new();
        private int _nextId;
        private bool _running;

        public event Action<T> MessageReceived;
        public event Action<string> RawMessageReceived;


        public TcpJsonServer(string ipAddress, int port, int replayCount = 5, int historySize = 5)
        {
            _address = IPAddress.Parse(ipAddress);
            _port = port;
            _listener = new TcpListener(_address, _port);
            _replayCount = replayCount;
            _history = new MessageQueue<T>(historySize);
        }

        public void Start()
        {
            _running = true;
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }

        public void Stop()
        {
            _running = false;
            _listener.Stop();
            foreach (var kv in _clients.Values)
            {
                kv.Writer.Dispose();
                kv.Client.Close();
            }
            _clients.Clear();
        }

        private async Task AcceptLoopAsync()
        {
            while (_running)
            {
                var client = await _listener.AcceptTcpClientAsync();
                int id = Interlocked.Increment(ref _nextId);

                var stream = client.GetStream();
                var reader = new StreamReader(stream);
                var writer = new StreamWriter(stream) { AutoFlush = true };
                _clients[id] = (client, writer);

                // Send history
                foreach (var msg in _history.GetHistory(_replayCount))
                {
                    var rawJson = JsonSerializer.Serialize(msg);
                    await writer.WriteLineAsync(rawJson);
                }
                _ = Task.Run(() => HandleClientAsync(id, reader));
            }
        }

        private async Task HandleClientAsync(int id, StreamReader reader)
        {
            try
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    RawMessageReceived?.Invoke(line);
                    var msg = line.FromJson<T>();
                    _history.Enqueue(msg);
                    MessageReceived?.Invoke(msg);
                    await BroadcastAsync(msg);
                }
            }
            catch
            {
                // swallow
            }
            finally
            {
                if (_clients.TryRemove(id, out var kv))
                {
                    kv.Writer.Dispose();
                    kv.Client.Close();
                }
            }
        }

        public async Task BroadcastAsync(T message)
        {
            var json = message.ToJson();
            foreach (var kv in _clients.ToArray())
            {
                var (client, writer) = kv.Value;
                if (!client.Connected) continue;
                try { await writer.WriteLineAsync(json); }
                catch
                {
                    writer.Dispose();
                    client.Close();
                    _clients.TryRemove(kv.Key, out _);
                }
            }
        }

        /// <summary>
        /// Adjust how many messages to replay on new connections.
        /// </summary>
        public void SetReplayCount(int replayCount)
        {
            _replayCount = replayCount;
        }

        /// <summary>
        /// Adjust how many messages are kept in history.
        /// </summary>
        public void SetHistorySize(int historySize)
        {
            _history.SetMaxHistory(historySize);
        }
    }

    public class TcpJsonClient<T> : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private TcpClient _client;
        private StreamReader _reader;
        private StreamWriter _writer;
        private readonly Channel<T> _outgoing = Channel.CreateUnbounded<T>();

        public event Action<T> MessageReceived;

        public TcpJsonClient(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public async Task ConnectAsync(int timeoutMs = 5000)
        {
            _client = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await _client.ConnectAsync(_host, _port);
            var stream = _client.GetStream();
            _reader = new StreamReader(stream);
            _writer = new StreamWriter(stream) { AutoFlush = true };
            _ = Task.Run(ReceiveLoopAsync);
          _ = Task.Run(SenderLoopAsync);
       }

        private async Task ReceiveLoopAsync()
        {
            try
            {
                string line;
                while ((line = await _reader.ReadLineAsync()) != null)
                {
                    var msg = line.FromJson<T>();
                    MessageReceived?.Invoke(msg);
                }
            }
            catch
            {
                // swallow
            }
        }

        public Task SendAsync(T message)
        {
            if (_writer == null)
                throw new InvalidOperationException("Not connected");
//            await _writer.WriteLineAsync(message.ToJson());
            return _outgoing.Writer.WriteAsync(message).AsTask();

        }
        private async Task SenderLoopAsync()
        {
            await foreach (var msg in _outgoing.Reader.ReadAllAsync())
            {
                var json = msg.ToJson();
                await _writer.WriteLineAsync(json);  // _writer only ever used here
            }
        }

        public void Dispose()
        {
            _writer?.Dispose();
            _reader?.Dispose();
            _client?.Close();
        }
    }
    /// <summary>
    /// Wraps a TcpJsonServer<T> so that it is started/stopped as an IHostedService.
    /// </summary>
    public class TcpJsonServerHost<T> : IHostedService
    {
        private readonly TcpJsonServer<T> _server;

        public TcpJsonServerHost(TcpJsonServer<T> server)
        {
            _server = server;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // ensure the server is started
            _server.Start();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // gracefully stop
            _server.Stop();
            return Task.CompletedTask;
        }
    }
}
