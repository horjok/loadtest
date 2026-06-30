using StackExchange.Redis;
using Docker.DotNet;
using Docker.DotNet.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    return ConnectionMultiplexer.Connect("redis:6379");
});
builder.Services.AddSingleton<IDockerClient>(sp =>
    new DockerClientConfiguration(
        new Uri("unix:///var/run/docker.sock"))
        .CreateClient());


// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddHostedService<ScalingBackgroundService>();
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/redis", async (IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();
    await db.StringSetAsync("test-key", "hello-docker");
    var value = await db.StringGetAsync("test-key");
    return Results.Ok(new { message = value.ToString() });
});


app.MapPost("/start-test", async (StartTestRequest request, IConnectionMultiplexer redis) =>
{
    var db = redis.GetDatabase();
    
    var messageId = await db.StreamAddAsync(
        "loadtest-stream", 
        new NameValueEntry[]
    {
        new NameValueEntry("url", request.Url),
        new NameValueEntry("requests", request.Requests.ToString()),
        new NameValueEntry("duration", request.Duration.ToString())
    });

    return Results.Ok(new { message = $"{request.Requests} requests added to the stream." });
});

app.MapGet("/scale", async (IConnectionMultiplexer redis, IDockerClient docker) =>
{
    var db = redis.GetDatabase();

    var streamInfo = await db.StreamInfoAsync("loadtest-stream");
    var groupInfo = await db.StreamGroupInfoAsync("loadtest-stream");
    var group = groupInfo.FirstOrDefault(g => g.Name == "loadtest-workers");

    var lastDeliveredId = group.LastDeliveredId ?? "0-0";
    var undelivered = await db.StreamRangeAsync("loadtest-stream", lastDeliveredId, "+");
    var pendingCount = undelivered.Length;

    var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters
    {
        All = false,
        Filters = new Dictionary<string, IDictionary<string, bool>>
        {
            ["name"] = new Dictionary<string, bool> { ["loadtest-worker"] = true }
        }
    });

    var currentWorkerCount = containers.Count;

    const int threshold = 5;
    const int minWorkers = 1;
    const int maxWorkers = 5;

    int targetWorkerCount = currentWorkerCount;

    if (pendingCount > threshold && currentWorkerCount < maxWorkers)
        targetWorkerCount = Math.Min(currentWorkerCount + 1, maxWorkers);
    else if (pendingCount == 0 && currentWorkerCount > minWorkers)
        targetWorkerCount = Math.Max(currentWorkerCount - 1, minWorkers);

    if (targetWorkerCount > currentWorkerCount)
    {
        await docker.Containers.StartContainerAsync("loadtest-worker", new ContainerStartParameters());
        Console.WriteLine($"Worker başlatıldı. Yeni sayı: {targetWorkerCount}");
    }
    else if (targetWorkerCount < currentWorkerCount)
    {
        await docker.Containers.StopContainerAsync("loadtest-worker", new ContainerStopParameters());
        Console.WriteLine($"Worker durduruldu. Yeni sayı: {targetWorkerCount}");
    }

    return Results.Ok(new
    {
        pendingMessages = pendingCount,
        currentWorkers = currentWorkerCount,
        targetWorkers = targetWorkerCount
    });
});


app.Run();
record StartTestRequest(string Url, int Requests, int Duration);

