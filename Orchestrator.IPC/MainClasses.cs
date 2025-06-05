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
        /// <summary>
        /// Broadcast to *all* connected clients except the one with this ID.
        /// </summary>
        private async Task BroadcastExceptAsync(T message, int excludeClientId)
        {
            var json = message.ToJson();
            foreach (var kv in _clients.ToArray())
            {
                var clientId = kv.Key;
                var (client, writer) = kv.Value;
                if (clientId == excludeClientId || !client.Connected)
                    continue;

                try
                {
                    await writer.WriteLineAsync(json);
                }
                catch
                {
                    writer.Dispose();
                    client.Close();
                    _clients.TryRemove(clientId, out _);
                }
            }
        }

        private async Task HandleClientAsync(int id, StreamReader reader)
        {
            try
            {
                while (_running)
                {
                    var readTask = reader.ReadLineAsync();
                    var timeout = Task.Delay(TimeSpan.FromMinutes(5));
                    var completed = await Task.WhenAny(readTask, timeout);

                    if (completed == timeout)
                    {
                        // idle period; keep the connection open
                        continue;
                    }

                    var line = await readTask;
                    if (line == null)
                        break;    // client has truly disconnected

                    // 1) raise the raw‐JSON event
                   // RawMessageReceived?.Invoke(line);

                    // 2) deserialize into your T
                    T msg;
                    try
                    {
                        msg = line.FromJson<T>();
                    }
                    catch (JsonException)
                    {
                        // invalid JSON—skip and continue listening
                        continue;
                    }

                    // 3) enqueue into history & fire typed event
                    _history.Enqueue(msg);
                   // MessageReceived?.Invoke(msg);

                    // 4) broadcast to everyone *but* the sender
                    await BroadcastExceptAsync(msg, id);
                }
            }
            catch( Exception e)
            {
                var i = e;

                // swallow any unexpected IO errors
            }
            finally
            {
                // clean up on true disconnect
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
                try { 
                    await writer.WriteLineAsync(json); 
                    await writer.FlushAsync();
                    await Task.Delay(10); 
                }
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
        private Task? _senderTask;
        private CancellationTokenSource? _senderCts;
        private bool _disposed;

        public event Action<Exception?>? Disconnected;

        public bool IsConnected => _client?.Connected == true && !_disposed;

        public event Action<T> MessageReceived;

        public TcpJsonClient(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public async Task ConnectAsync(int timeoutMs = 5000)
        {
            _disposed = false;
            _client = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await _client.ConnectAsync(_host, _port);
            var stream = _client.GetStream();
            _reader = new StreamReader(stream);
            _writer = new StreamWriter(stream) { AutoFlush = true };
            _senderCts?.Cancel();
            _senderCts = new CancellationTokenSource();
            _senderTask = Task.Run(() => SenderLoopAsync(_senderCts.Token));
            _ = Task.Run(ReceiveLoopAsync);
      }

        private async Task ReceiveLoopAsync()
        {
            Exception? ex = null;
            try
            {
                string? line;
                while ((line = await _reader.ReadLineAsync()) != null)
                {
                    var msg = line.FromJson<T>();
                    MessageReceived?.Invoke(msg);
                }
            }
            catch (Exception e)
            {
                ex = e;
            }
            finally
            {
                if (!_disposed)
                {
                    Dispose();
                    Disconnected?.Invoke(ex);
                }
            }
        }

        public Task SendAsync(T message)
        {
            return _outgoing.Writer.WriteAsync(message).AsTask();

            if (_writer == null)
                throw new InvalidOperationException("Not connected");
//            await _writer.WriteLineAsync(message.ToJson());
            return _outgoing.Writer.WriteAsync(message).AsTask();

        }
 private async Task SenderLoopAsync(CancellationToken ct)
{
    await foreach (var msg in _outgoing.Reader.ReadAllAsync(ct))
    {
        string json;
        try
        {
            json = msg.ToJson();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SenderLoop] Serialization failed: {ex.Message}");
            continue; // skip bad message, keep going
        }

        try
        {
            await _writer.WriteLineAsync(json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SenderLoop] Write failed: {ex.Message}");
            break;
        }
    }
}

        public void Dispose()
        {
            _disposed = true;
            _senderCts?.Cancel();
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
 
public static class TcpJsonClientExtensions
    {
        public static IAsyncEnumerable<T> StreamAsync<T>(this TcpJsonClient<T> client, CancellationToken ct = default)
        {
            var chan = Channel.CreateUnbounded<T>();
            void OnMsg(T msg) => _ = chan.Writer.WriteAsync(msg, ct);
            client.MessageReceived += OnMsg;

            return ReadAllAsync();

            async IAsyncEnumerable<T> ReadAllAsync()
            {
                try
                {
                    await foreach (var msg in chan.Reader.ReadAllAsync(ct))
                        yield return msg;
                }
                finally
                {
                    client.MessageReceived -= OnMsg;
                }
            }
        }
    }

}
