using Microsoft.OpenApi.Models;
using Nest;
using BibleFTS.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var esUrl = builder.Configuration["ELASTICSEARCH_URL"] ?? "http://localhost:9200";
var settings = new ConnectionSettings(new Uri(esUrl)).DefaultIndex("bible");
builder.Services.AddSingleton<IElasticClient>(new ElasticClient(settings));

builder.Services.AddScoped<BibleSeeder>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "BibleFTS API", Version = "v1" });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "BibleFTS API v1");
});

app.MapGet("/health/es", async (IElasticClient es) =>
{
    var ping = await es.PingAsync();
    return Results.Ok(new { ping.IsValid, ping.ServerError });
});

app.MapControllers();

app.Run();

public partial class Program { }
