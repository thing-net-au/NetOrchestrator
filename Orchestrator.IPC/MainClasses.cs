using Microsoft.Extensions.Hosting;
using Orchestrator.Core.Models;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Orchestrator.IPC
{
    public static class SerializationExtensions
    {
        private static readonly JsonSerializerOptions DefaultOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static string ToJson<T>(this T obj)
        {
            if (obj is Envelope env)
            {
                if (env.Payload.ValueKind == JsonValueKind.Undefined || env.Payload.ValueKind == JsonValueKind.Null)
                {
                    return "{}";
                    throw new InvalidOperationException("Envelope.Payload is missing or null.");
                }
            }

            return JsonSerializer.Serialize(obj, DefaultOptions);
        }

        public static T FromJson<T>(this string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Input JSON string is null or empty.", nameof(json));

            try
            {
                return JsonSerializer.Deserialize<T>(json, DefaultOptions)
                       ?? throw new JsonException($"Deserialization of type {typeof(T).Name} returned null.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to deserialize JSON into {typeof(T).Name}: {ex.Message}", ex);
            }
        }
    }

    public class MessageQueue<T>
    {
        private readonly Channel<T> _channel;
        private readonly int _capacity;
        private readonly object _lock = new();
        private readonly Queue<T> _history;

        public MessageQueue(int capacity)
        {
            _capacity = capacity;
            _history = new Queue<T>(capacity);
            var opts = new BoundedChannelOptions(capacity)
            {
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest  // drop the oldest when full
            };
            _channel = Channel.CreateBounded<T>(opts);
        }

        /// <summary>Enqueue an item, dropping the oldest if we exceed capacity.</summary>
        public void Enqueue(T item)
        {
            if (item == null)
                return;
            lock (_lock)
            {
                if (_history.Count == _capacity)
                    _history.Dequeue();
                _history.Enqueue(item);
            }
            // This will never block (FullMode=DropOldest), and preserves FIFO
            _channel.Writer.TryWrite(item);
        }

        /// <summary>Get a snapshot of the last up to <paramref name="count"/> items, oldest first.</summary>
        public T[] GetHistory(int count)
        {
            lock (_lock)
            {
                return _history
                    .Take(Math.Min(count, _history.Count))
                    .ToArray();
            }
        }

        /// <summary>
        /// Returns an IAsyncEnumerable over all history _then_ live items in order.
        /// </summary>
        public async IAsyncEnumerable<T> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            // 1. Replay history
            var snapshot = GetHistory(_capacity);
            foreach (var item in snapshot)
                yield return item;

            // 2. Then tail the channel for live items
            await foreach (var item in _channel.Reader.ReadAllAsync(ct))
                yield return item;
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

        private readonly SemaphoreSlim _readSemaphore = new(1, 1);

        private async Task HandleClientAsync(int id, StreamReader reader)
        {
            try
            {
                while (_running)
                {
                    // begin a single read
                    var readTask = reader.ReadLineAsync();
                    var timeout = Task.Delay(TimeSpan.FromMinutes(5));
                    var completed = await Task.WhenAny(readTask, timeout);

                    if (completed == timeout)
                    {
                        // idle—stop reading and exit (or you can break and let outer code re-open)
                     //   continue;
                     //   break;
                    }

                    // now safely await the one outstanding read
                    var line = await readTask;
                    if (line == null) break;

                    var msg = line.FromJson<T>();
                    _history.Enqueue(msg);
                    
                    await BroadcastExceptAsync(msg, id);
                }
            }
            catch { }
            finally {
                // clean up on disconnect
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
                try
                {
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
            if (_client.Connected == false)
                return;
            var stream = _client.GetStream();
            _reader = new StreamReader(stream);
            _writer = new StreamWriter(stream) { AutoFlush = true };
            _senderCts?.Cancel();
            _senderCts = new CancellationTokenSource();
            _senderTask = Task.Run(() => SenderLoopAsync(_senderCts.Token));
            
            StartReceiving();
        }

        private readonly object _msgLock = new();
        private Task? _receiveTask;
        private readonly object _receiveLock = new();

        public void StartReceiving()
        {
            lock (_receiveLock)
            {
                if (_receiveTask == null || _receiveTask.IsCompleted)
                {
                    _receiveTask = Task.Run(ReceiveLoopAsync);
                }
            }
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
                    // only lock around the event invocation, not the read
                    lock (_msgLock)
                    {
                        MessageReceived?.Invoke(msg);
                    }
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
                if (!_client.Connected)
                    await ConnectAsync();
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
