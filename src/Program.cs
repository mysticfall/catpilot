using System.ClientModel;
using System.ClientModel.Primitives;
using EDPM37;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;

var workDir = Directory.GetCurrentDirectory();
var configPath = Path.Combine(workDir, "appsettings.json");

var configuration = new ConfigurationBuilder()
    .AddJsonFile(configPath, optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

var agentSettings = configuration.GetSection("Agent");

var endpoint = agentSettings.GetValue<string>("Endpoint") ?? "https://openrouter.ai/api/v1";
var model = agentSettings.GetValue<string>("Model") ?? "openai/gpt-5.1-codex-max";
var apiKey = agentSettings.GetValue<string>("ApiKey") ?? 
             throw new InvalidOperationException("Missing 'ApiKey' in the configuration file.");

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.ClearProviders();
    builder.AddConsole();
    builder.AddConfiguration(configuration.GetSection("Logging"));
});

var logger = loggerFactory.CreateLogger<Program>();

var client = new OpenAIClient(
        new ApiKeyCredential(apiKey),
        new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
            ClientLoggingOptions = new ClientLoggingOptions
            {
                EnableLogging = true,
                LoggerFactory = loggerFactory
            }
        })
    .GetChatClient(model)
    .AsIChatClient();

var launcher = new WorkflowLauncher(client, loggerFactory);

if (args.Length == 0)
{
    logger.LogError("Error: Please provide a project path as an argument.");
    Environment.Exit(1);
    return;
}

var projectDir = Path.GetFullPath(args[0]);

if (!Directory.Exists(projectDir))
{
    logger.LogError("Error: The project path '{projectDir}' does not exist.", projectDir);
    Environment.Exit(1);
    return;
}

var startTaskFrom = 0;
var startSubtaskFrom = 0;

if (args.Length > 1)
{
    var range = args[1].Split(":");

    int.TryParse(range[0], out startTaskFrom);

    if (range.Length > 1)
    {
        int.TryParse(range[1], out startSubtaskFrom);
    }
}

await launcher.RunAsync(projectDir, workDir, startTaskFrom, startSubtaskFrom);