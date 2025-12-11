using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using OpenAI.Responses;

namespace EDPM37.Executors;

public readonly record struct StartCoding(string PromptPath);

[method: JsonConstructor]
public readonly struct CodingAgentResponse(string result, string message)
{
    [JsonPropertyName("result")] public string Result { get; } = result;

    [JsonPropertyName("message")] public string Message { get; } = message;
}

public class CodingExecutor(
    IChatClient client,
    string instructions,
    IDictionary<string, object?> promptContext,
    ILoggerFactory? loggerFactory = null
) : ReflectingExecutor<CodingExecutor>("CodingExecutor", CreateOptions()),
    IMessageHandler<StartCoding>,
    IMessageHandler<ConfirmResult>
{
    private static ExecutorOptions CreateOptions()
    {
        var options = ExecutorOptions.Default;

        options.AutoSendMessageHandlerResultObject = false;
        options.AutoYieldOutputHandlerResultObject = false;

        return options;
    }

    private AgentThread? _thread;

    private string? _lastTask;

    private readonly ILogger _logger = (loggerFactory ?? NullLoggerFactory.Instance)
        .CreateLogger<CodingExecutor>();

    [Experimental("OPENAI001")]
    public async ValueTask HandleAsync(
        StartCoding message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default
    )
    {
        var currentTask = Path.GetFileNameWithoutExtension(message.PromptPath);
        var taskDetails = await Utils.ReadPrompt(message.PromptPath, promptContext, _logger);

        _logger.LogInformation("Starting task: {currentTask}", currentTask);

        _thread = await CodeAsync(
            currentTask,
            taskDetails,
            context,
            cancellationToken: cancellationToken
        );

        _lastTask = currentTask;
    }

    [Experimental("OPENAI001")]
    public async ValueTask HandleAsync(
        ConfirmResult message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (_lastTask is null)
        {
            throw new InvalidOperationException("No task is currently running.");
        }

        if (_thread is null)
        {
            throw new InvalidOperationException("No agent thread is available.");
        }

        if (!message.IsConfirmed)
        {
            _logger.LogInformation("User declined to proceed. Cancelling the task: {currentTask}", _lastTask);
            return;
        }

        _logger.LogInformation("Received a confirmation to proceed: {currentTask}", message.Text);

        _thread = await CodeAsync(
            _lastTask,
            message.Text,
            context,
            _thread,
            cancellationToken: cancellationToken
        );
    }

    [Experimental("OPENAI001")]
    private async ValueTask<AgentThread> CodeAsync(
        string currentTask,
        string taskDetails,
        IWorkflowContext context,
        AgentThread? thread = null,
        CancellationToken cancellationToken = default
    )
    {
        await using var mcpClient = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "desktop-commander",
                Command = "npx",
                Arguments =
                [
                    "-y",
                    "@wonderwhy-er/desktop-commander@latest",
                    "--no-onboarding"
                ]
            }),
            loggerFactory: loggerFactory,
            cancellationToken: cancellationToken
        );

        var mcpTools = await mcpClient
            .ListToolsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var agent = client
            .CreateAIAgent(
                new ChatClientAgentOptions
                {
                    ChatOptions = new ChatOptions
                    {
                        Instructions = instructions,
                        AllowMultipleToolCalls = true,
                        RawRepresentationFactory = _ => new ResponseCreationOptions
                        {
                            ReasoningOptions = new ResponseReasoningOptions
                            {
                                ReasoningEffortLevel = ResponseReasoningEffortLevel.Medium,
                                ReasoningSummaryVerbosity = ResponseReasoningSummaryVerbosity.Detailed
                            }
                        },
                        Tools =
                        [
                            AIFunctionFactory.Create(ReportProgress, "report_progress"),
                            .. mcpTools
                        ]
                    },
                    ChatMessageStoreFactory = ctx => new InMemoryChatMessageStore(
                        new ToolMessageReducer(),
                        ctx.SerializedState,
                        ctx.JsonSerializerOptions
                    )
                },
                loggerFactory
            );

        if (thread is null)
        {
            _logger.LogDebug("Creating a new thread for the agent.");
        }
        else
        {
            _logger.LogDebug("Using existing thread for the agent.");
        }

        var agentThread = thread ?? agent.GetNewThread();

        var completionUpdates =
            agent.RunStreamingAsync(
                taskDetails,
                agentThread,
                cancellationToken: cancellationToken
            );

        var sb = new StringBuilder();

        await foreach (var update in completionUpdates)
        {
            foreach (var item in update.Contents)
            {
                switch (item)
                {
                    case TextReasoningContent ctx:
                        _logger.LogInformation("\e[97m{text}\e[0m", ctx.Text);
                        break;
                    case FunctionCallContent ctx:
                        _logger.LogTrace("[FuncCall][{id}] {name}({args})", ctx.CallId, ctx.Name, ctx.Arguments);
                        break;
                    case FunctionResultContent ctx:
                        if (ctx.Exception is null)
                        {
                            _logger.LogTrace(
                                "[FuncResult][{id}] The function returned {result}",
                                ctx.CallId,
                                ctx.Result);
                        }
                        else
                        {
                            _logger.LogWarning(
                                ctx.Exception,
                                "[FuncResult][{id}] The function threw an exception.",
                                ctx.CallId
                            );
                        }

                        break;
                    case TextContent textContent:
                        sb.Append(textContent.Text);
                        break;
                }
            }
        }

        var rawOutput = sb.ToString().ExtractJson();

        _logger.LogTrace("Raw response: {rawOutput}", rawOutput);

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            var response = JsonSerializer.Deserialize<CodingAgentResponse>(rawOutput, options);

            switch (response.Result)
            {
                case "success":
                    _logger.LogInformation("Task \"{currentTask}\" completed successfully.", currentTask);

                    await context.SendMessageAsync(new NextSubtask(), cancellationToken);
                    break;
                case "failure":
                    throw new InvalidOperationException(
                        $"Task \"{currentTask}\" failed with message: {response.Message}");
                case "confirm":
                    _logger.LogDebug("Requesting user confirmation: {message}", response.Message);

                    await context.SendMessageAsync(new ConfirmRequest(response.Message), cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown result: \"{response.Result}\"");
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize the response: {rawOutput}", rawOutput);

            await context.SendMessageAsync(
                new ConfirmRequest(
                    "The task was stopped unexpectedly. The result may be incomplete." +
                    $"  - Current task: {currentTask}"
                ),
                cancellationToken
            );
        }

        return agentThread;
    }

    [Description("Report progress.")]
    private void ReportProgress([Description("Message to report.")] string message)
    {
        _logger.LogInformation("[Agent] {message}", message);
    }
}