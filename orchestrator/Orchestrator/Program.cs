using StackExchange.Redis;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    return ConnectionMultiplexer.Connect("redis:6379");
});

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

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

app.Run();
record StartTestRequest(string Url, int Requests, int Duration);
record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
