using SalesDataAnalyzer.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection(OpenAiOptions.SectionName));
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddSingleton<IDatasetStore, InMemoryDatasetStore>();
builder.Services.AddSingleton<IFileParsingService, FileParsingService>();
builder.Services.AddSingleton<IAnalyticsService, AnalyticsService>();
builder.Services.AddSingleton<ISalesDomainValidationService, SalesDomainValidationService>();
builder.Services.AddHttpClient<IOpenAiChatClient, OpenAiChatClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
});
builder.Services.AddScoped<IAgentInsightService, AgentInsightService>();

var app = builder.Build();

app.UseCors("frontend");
app.UseAuthorization();
app.MapControllers();

app.Run();
