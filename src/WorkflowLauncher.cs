using CatPilot.Executors;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CatPilot;

public class WorkflowLauncher(
    IChatClient client,
    ILoggerFactory? loggerFactory
)
{
    private readonly ILogger _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<WorkflowLauncher>();

    public async Task RunAsync(
        string projectDir,
        string programDir,
        int startTaskFrom = 0,
        int startSubtaskFrom = 0
    )
    {
        Directory.SetCurrentDirectory(projectDir);

        var projectName = Path.GetFileNameWithoutExtension(projectDir);

        _logger.LogInformation("Starting migration of the project: {projectName}", projectName);
        _logger.LogInformation("Using the project path: {projectPath}", projectDir);
        _logger.LogInformation("Using the program path: {programPath}", programDir);
        _logger.LogInformation("Starting task index: {index}", startTaskFrom);
        _logger.LogInformation("Starting subtask index: {index}", startSubtaskFrom);

        var referencesDir = Path.Combine(programDir, "references");
        var promptsDir = Path.Combine(programDir, "prompts");

        var tasksDir = Path.Combine(promptsDir, "tasks");
        var instructionsDir = Path.Combine(promptsDir, "system");

        var workflow = await BuildWorkflow(projectName, projectDir, referencesDir, instructionsDir);

        await RunWorkflow(
            workflow,
            projectName,
            tasksDir,
            startTaskFrom,
            startSubtaskFrom
        );
    }

    private async Task<Workflow> BuildWorkflow(
        string projectName,
        string projectDir,
        string referencesDir,
        string promptsDir
    )
    {
        var promptContext = new Dictionary<string, object?>
        {
            ["projectname"] = projectName,
            ["projectdir"] = projectDir,
            ["referencesdir"] = referencesDir
        };

        var codingPrompt = await Utils.ReadPrompt(
            Path.Combine(promptsDir, "coding.md"),
            promptContext,
            _logger
        );

        var inputPort = RequestPort.Create<ConfirmRequest, ConfirmResult>("input-port");

        var readTasks = new ReadTasksExecutor(loggerFactory);
        var readSubtasks = new ReadSubtasksExecutor(loggerFactory);

        var code = new CodingExecutor(client, codingPrompt, promptContext, loggerFactory);

        WorkflowBuilder builder = new(readTasks);

        return builder
            .AddEdge(readTasks, readSubtasks)
            .AddEdge(readSubtasks, code)
            .AddEdge(readSubtasks, readTasks)
            .AddEdge(code, inputPort)
            .AddEdge(inputPort, code)
            .AddEdge(code, readSubtasks)
            .WithOutputFrom(readTasks)
            .Build();
    }

    private async Task RunWorkflow(
        Workflow workflow,
        string projectName,
        string tasksDir,
        int startTaskFrom = 0,
        int startSubtaskFrom = 0
    )
    {
        var run = await InProcessExecution
            .StreamAsync(workflow, new StartTasks(tasksDir, startTaskFrom, startSubtaskFrom))
            .ConfigureAwait(false);

        _logger.LogInformation("[{project}] Workflow started successfully.", projectName);

        await foreach (var evt in run.WatchStreamAsync().ConfigureAwait(false))
        {
            switch (evt)
            {
                case WorkflowOutputEvent e:
                    _logger.LogInformation(
                        "[{project}] Workflow completed successfully: {output}",
                        projectName,
                        e.Data);
                    break;
                case WorkflowErrorEvent e:
                    _logger.LogError(e.Data as Exception, "Failed to start the workflow.");
                    break;
                case ExecutorInvokedEvent e:
                    _logger.LogDebug("Executor invoked: {executor}.", e.ExecutorId);
                    break;
                case ExecutorCompletedEvent e:
                    _logger.LogDebug("Executor completed: {executor}.", e.ExecutorId);
                    break;
                case ExecutorFailedEvent e:
                    _logger.LogError(
                        e.Data,
                        "[{project}] Failed to execute the workflow.",
                        projectName
                    );
                    break;
                case RequestInfoEvent e:
                    var request = e.Request.Data.AsType(typeof(ConfirmRequest));

                    if (request is ConfirmRequest cr)
                    {
                        Console.WriteLine();
                        Console.Write("[Needs Confirmation][");
                        Console.Write(projectName);
                        Console.Write("]: ");
                        Console.WriteLine(cr.Text);
                        Console.WriteLine();
                        Console.WriteLine("Do you want to proceed? (Y/N)");

                        string? line = null;

                        while (string.IsNullOrWhiteSpace(line))
                        {
                            line = Console.ReadLine()?.Trim().ToUpper();

                            if (line is null || !new[] { "Y", "N" }.Contains(line))
                            {
                                Console.WriteLine("Please enter either 'Y' or 'N'.");
                                line = null;
                            }
                        }

                        var isConfirmed = line == "Y";

                        Console.WriteLine();
                        Console.WriteLine("Enter your message (optional):");

                        var message = Console.ReadLine()?.Trim();

                        if (string.IsNullOrWhiteSpace(message))
                        {
                            message = isConfirmed
                                ? "Request confirmed. Proceed with the task."
                                : "Request denied. Abort the task.";
                        }

                        var result = new ConfirmResult(message, isConfirmed);
                        var response = e.Request.CreateResponse(result);

                        await run.SendResponseAsync(response).ConfigureAwait(false);
                    }
                    else
                    {
                        throw new InvalidOperationException("Unexpected request type.");
                    }

                    break;
                default:
                    _logger.LogTrace("Unhandled event: {evt}.", evt);
                    break;
            }
        }
    }
}