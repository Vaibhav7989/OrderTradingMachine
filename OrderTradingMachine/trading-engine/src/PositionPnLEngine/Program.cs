using PositionPnLEngine.Infrastructure;
using PositionPnLEngine.Services;
using Shared.Infrastructure;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Enable annotations for better docs
    c.EnableAnnotations();
});

builder.Services.AddSingleton<IEventBus, InMemoryEventBus>();
builder.Services.AddSingleton<IPositionService, PositionService>();

// Producer-consumer background worker
builder.Services.AddHostedService<TradeEventConsumer>();

builder.Services.AddHealthChecks();

// Enable CORS for local testing
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c => 
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Position & PnL Engine API v1");
    c.RoutePrefix = "swagger"; // Access at /swagger
    c.DocumentTitle = "Position & PnL Engine API";
    c.DefaultModelsExpandDepth(2);
    c.DefaultModelExpandDepth(2);
    c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
    c.DisplayRequestDuration();
});

app.UseCors("AllowAll");
app.MapControllers();
app.MapHealthChecks("/health");

// Redirect root to Swagger UI
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

public partial class Program { }
