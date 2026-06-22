using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

class Program
{
    static async Task Main(string[] args)
    {
        var urls = new[] {
            "http://v3.presence.eu-beta.hp2k.co.uk/Presence",
            "https://v3.presence.eu-beta.hp2k.co.uk/Presence",
            "http://v1.softsignalling.eu-beta.hp2k.co.uk/Signals"
        };

        foreach (var url in urls)
        {
            Console.WriteLine($"\nTesting {url}...");
            try
            {
                var connection = new HubConnectionBuilder()
                    .WithUrl(url)
                    .Build();

                var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                await connection.StartAsync(cts.Token);
                Console.WriteLine($"SUCCESS: Connected to {url}");
                await connection.StopAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner: {ex.InnerException.Message}");
                }
            }
        }
    }
}
