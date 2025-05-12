using System;
using System.Text.Json;
using System.Threading.Tasks;
using Orchestrator.IPC;

// Define your data contract
public class ThisObj
{
    public string aaa { get; set; }
    public string bbb { get; set; }
    public string ccc { get; set; }
}

// Envelope to wrap messages with type metadata
public class Envelope
{
    public string TypeString { get; set; }
    public object Contents { get; set; }
}

class Program
{
    static async Task Main()
    {
        const string Host = "127.0.0.1";
        const int Port = 9000;

        // 1. Start the TCP JSON server for Envelope
        var server = new TcpJsonServer<Envelope>(Host, Port, replayCount: 5, historySize: 50);
        server.MessageReceived += env =>
        {
            if (env.TypeString == nameof(ThisObj) && env.Contents is System.Text.Json.JsonElement je)
            {
                var obj = je.Deserialize<ThisObj>();
                Console.WriteLine($"[Server] Received {env.TypeString}: aaa={obj.aaa}, bbb={obj.bbb}, ccc={obj.ccc}");
            }
        };
        server.Start();
        Console.WriteLine("[Server] Started.");

        // 2. Connect two clients with retry logic
        async Task<TcpJsonClient<Envelope>> CreateClientAsync(string name)
        {
            var client = new TcpJsonClient<Envelope>(Host, Port);
            client.MessageReceived += env =>
            {
                if (env.TypeString == nameof(ThisObj) && env.Contents is System.Text.Json.JsonElement je)
                {
                    var obj = je.Deserialize<ThisObj>();
                    Console.WriteLine($"[{name}] Received {env.TypeString}: aaa={obj.aaa}, bbb={obj.bbb}, ccc={obj.ccc}");
                }
            };
            int attempts = 0;
            while (true)
            {
                try
                {
                    await client.ConnectAsync();
                    Console.WriteLine($"[{name}] Connected.");
                    return client;
                }
                catch (Exception ex) when (attempts++ < 5)
                {
                    Console.WriteLine($"[{name}] Connect failed, retrying... {ex.Message}");
                    await Task.Delay(200);
                }
            }
        }

        using var clientA = await CreateClientAsync("Client A");
        using var clientB = await CreateClientAsync("Client B");

        // 3. Exchange ThisObj instances wrapped in Envelope
        for (int i = 1; i <= 5; i++)
        {
            var objA = new ThisObj { aaa = $"A_aaa_{i}", bbb = $"A_bbb_{i}", ccc = $"A_ccc_{i}" };
            var envA = new Envelope { TypeString = nameof(ThisObj), Contents = objA };
            Console.WriteLine($"[Client A] Sending Envelope with {envA.TypeString}");
            try { await clientA.SendAsync(envA); }
            catch (Exception ex) { Console.WriteLine($"[Client A] error: {ex.Message}"); }

            await Task.Delay(100);

            var objB = new ThisObj { aaa = $"B_aaa_{i}", bbb = $"B_bbb_{i}", ccc = $"B_ccc_{i}" };
            var envB = new Envelope { TypeString = nameof(ThisObj), Contents = objB };
            Console.WriteLine($"[Client B] Sending Envelope with {envB.TypeString}");
            try { await clientB.SendAsync(envB); }
            catch (Exception ex) { Console.WriteLine($"[Client B] error: {ex.Message}"); }

            await Task.Delay(300);
        }

        // 4. Shutdown
        Console.WriteLine("Demo complete. Press any key to exit...");
        Console.ReadKey(true);
        server.Stop();
    }
}
