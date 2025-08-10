using System.Text;
using System.Text.Json;
using System.Net.Http;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StockBotWorker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger) => _logger = logger;

    private record StockCommand(string StockCode, string? RoomId);
    private record BotReply(string RoomId, string Message);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            IConnection? conn = null;
            IModel? channel = null;

            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost",
                    UserName = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "guest",
                    Password = Environment.GetEnvironmentVariable("RABBITMQ_PASS") ?? "guest",
                    DispatchConsumersAsync = true,
                    AutomaticRecoveryEnabled = true,
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                    TopologyRecoveryEnabled = true
                };

                conn = factory.CreateConnection();
                channel = conn.CreateModel();

                channel.QueueDeclare("stockQueue", durable: false, exclusive: false, autoDelete: false);
                channel.QueueDeclare("chatQueue",  durable: false, exclusive: false, autoDelete: false);
                channel.BasicQos(0, prefetchCount: 1, global: false);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.Received += async (_, ea) =>
                {
                    string roomId = "general";
                    string stockCode;

                    try
                    {
                        // Parse payload: prefer JSON {stockCode, roomId}; fallback to plain "aapl.us"
                        var payload = Encoding.UTF8.GetString(ea.Body.ToArray());
                        if (payload.StartsWith("{"))
                        {
                            var cmd = JsonSerializer.Deserialize<StockCommand>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            stockCode = cmd?.StockCode?.Trim() ?? "";
                            roomId    = string.IsNullOrWhiteSpace(cmd?.RoomId) ? "general" : cmd!.RoomId!.Trim();
                        }
                        else
                        {
                            stockCode = payload.Trim();
                        }

                        if (string.IsNullOrWhiteSpace(stockCode))
                            throw new InvalidOperationException("Missing stock code");

                        // fetch CSV (HttpClient without factory)
                        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                        var url = $"https://stooq.com/q/l/?s={stockCode}&f=sd2t2ohlcv&h&e=csv";
                        var csv = await http.GetStringAsync(url, stoppingToken);

                        // parse CSV
                        var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length < 2) throw new InvalidOperationException("CSV response missing data");

                        var cols   = lines[1].Split(',');
                        var symbol = cols[0].ToUpperInvariant();
                        var close  = cols[6];

                        string message = close.Equals("N/D", StringComparison.OrdinalIgnoreCase)
                            ? $"{symbol} quote is not available right now"
                            : $"{symbol} quote is ${close} per share";

                        var reply = new BotReply(roomId, message);
                        var outJson = JsonSerializer.Serialize(reply);
                        channel.BasicPublish("", "chatQueue", null, Encoding.UTF8.GetBytes(outJson));

                        channel.BasicAck(ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Bot error while processing message");
                        // Send a friendly error back to the same room (if we know it)
                        var reply = new BotReply(roomId, "bot error: could not fetch quote for requested symbol");
                        var outJson = JsonSerializer.Serialize(reply);
                        channel.BasicPublish("", "chatQueue", null, Encoding.UTF8.GetBytes(outJson));

                        channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                    }
                };

                channel.BasicConsume("stockQueue", autoAck: false, consumer: consumer);
                _logger.LogInformation("StockBot connected and listening on stockQueue…");

                try { await Task.Delay(Timeout.Infinite, stoppingToken); }
                catch (TaskCanceledException) { /* normal shutdown */ }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RabbitMQ loop crashed. Retrying in 3s…");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
            finally
            {
                try { channel?.Close(); channel?.Dispose(); } catch { }
                try { conn?.Close(); conn?.Dispose(); } catch { }
            }
        }
    }
}