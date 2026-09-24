using System.Text.Json.Serialization;
using JobOrchestrator.Execution;
using JobOrchestrator.Validation;
using JobOrchestrator.Worker;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services
    .AddOptions<WorkerOptions>()
    .Bind(builder.Configuration.GetSection(WorkerOptions.SectionName))
    .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _),
        "Worker:BaseUrl must be an absolute URL.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.AccessToken),
        "Worker:AccessToken is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.CandidateEmail),
        "Worker:CandidateEmail is required.")
    .Validate(options => options.MaxAttempts > 0,
        "Worker:MaxAttempts must be greater than zero.")
    .Validate(options => options.RequestTimeoutSeconds > 0,
        "Worker:RequestTimeoutSeconds must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddSingleton<IDefinitionValidator, DefinitionValidator>();
builder.Services.AddSingleton<ITemplateResolver, TemplateResolver>();
builder.Services.AddScoped<IExecutionCoordinator, ExecutionCoordinator>();

builder.Services.AddHttpClient<IWorkerClient, WorkerClient>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<WorkerOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();

app.Run();

public partial class Program;
