using Docker.DotNet;
using Docker.DotNet.Models;
using StackExchange.Redis;

public class ScalingBackgroundService : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDockerClient _docker;

    const int threshold = 5;
    const int minWorkers = 1;
    const int maxWorkers = 5;

    public ScalingBackgroundService(IConnectionMultiplexer redis, IDockerClient docker)
    {
        _redis = redis;
        _docker = docker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckAndScale();
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task CheckAndScale()
    {
        try
        {
            var db = _redis.GetDatabase();

            var groupInfoList = await db.StreamGroupInfoAsync("loadtest-stream");
            var group = groupInfoList.FirstOrDefault(g => g.Name == "loadtest-workers");
            var lastDeliveredId = group.LastDeliveredId ?? "0-0";
            var undelivered = await db.StreamRangeAsync("loadtest-stream", lastDeliveredId, "+");
            var pendingCount = undelivered.Length;

            var containers = await _docker.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = false,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool> { ["loadtest-worker"] = true }
                }
            });

            var currentWorkerCount = containers.Count;
            int targetWorkerCount = currentWorkerCount;

            if (pendingCount > threshold && currentWorkerCount < maxWorkers)
                targetWorkerCount = Math.Min(currentWorkerCount + 1, maxWorkers);
            else if (pendingCount == 0 && currentWorkerCount > minWorkers)
                targetWorkerCount = Math.Max(currentWorkerCount - 1, minWorkers);

            if (targetWorkerCount > currentWorkerCount)
            {
                await _docker.Containers.StartContainerAsync("loadtest-worker", new ContainerStartParameters());
                Console.WriteLine($"[AutoScale] Worker started. Pending: {pendingCount}, Workers: {targetWorkerCount}");
            }
            else if (targetWorkerCount < currentWorkerCount)
            {
                await _docker.Containers.StopContainerAsync("loadtest-worker", new ContainerStopParameters());
                Console.WriteLine($"[AutoScale] Worker stopped. Pending: {pendingCount}, Workers: {targetWorkerCount}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AutoScale] Error: {ex.Message}");
        }
    }
}