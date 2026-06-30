using StackExchange.Redis;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using var httpClient = new HttpClient();

var influxUrl = Environment.GetEnvironmentVariable("INFLUX_URL")!;
var influxToken = Environment.GetEnvironmentVariable("INFLUX_TOKEN")!;
var influxOrg = Environment.GetEnvironmentVariable("INFLUX_ORG")!;
var influxBucket = Environment.GetEnvironmentVariable("INFLUX_BUCKET")!;

using var influxClient = new InfluxDBClient(influxUrl, influxToken);
var writeApi = influxClient.GetWriteApiAsync();

var redis = ConnectionMultiplexer.Connect("redis:6379");
var db = redis.GetDatabase();

string streamName = "loadtest-stream";
string groupName = "loadtest-workers";
string consumerName = Environment.MachineName;

try
{
    await db.StreamCreateConsumerGroupAsync(streamName, groupName, "0-0", createStream: true);
}
catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
{
    Console.WriteLine($"Consumer group '{groupName}' already exists.");
}



Console.WriteLine("Worker started. Connected to Redis.");

while (true)
{
    var results = await db.StreamReadGroupAsync(
        streamName, 
        groupName,
        consumerName,
        ">", 
        count: 10
        );

        if (results.Length > 0)
        {
            foreach (var entry in results)
            {
                Console.WriteLine($"Received message: {entry.Id}");
                
                var url = entry.Values.FirstOrDefault(v => v.Name == "url").Value.ToString();
                var requests = int.Parse(entry.Values.FirstOrDefault(v => v.Name == "requests").Value!);
                var duration = int.Parse(entry.Values.FirstOrDefault(v => v.Name == "duration").Value!);

                Console.WriteLine($"Load test started. URL: {url}, Requests: {requests}, Duration: {duration} seconds");
                
                var tasks = Enumerable.Range(0, requests).Select(async i =>
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                    try
                    {
                        var response = await httpClient.GetAsync(url);
                        stopwatch.Stop();
                        Console.WriteLine($"[{i}] {(int)response.StatusCode} - {stopwatch.ElapsedMilliseconds}ms");
                    }
                    catch (Exception ex)
                    {
                        stopwatch.Stop();
                        Console.WriteLine($"  [{i}] ERROR - {stopwatch.ElapsedMilliseconds}ms - {ex.Message}");
                    }
                });

                await Task.WhenAll(tasks);

                var point = PointData.Measurement("load_test_results")
                    .Tag("url", url)
                    .Field("requests", requests)
                    .Field("duration", duration)
                    .Timestamp(DateTime.UtcNow, WritePrecision.Ms);

                await writeApi.WritePointAsync(point, influxBucket, influxOrg);
                Console.WriteLine($"Results written to InfluxDB.");    

                await db.StreamAcknowledgeAsync(streamName, groupName, entry.Id);
                Console.WriteLine($"ACK sent: {entry.Id} ");
            }
        }
    
    else
    {
        Console.WriteLine("No new messages. Waiting...");
        await Task.Delay(1000);
    }
}