using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EDPM37.Executors;

public readonly record struct StartTasks(
    string TasksDir,
    int StartTaskFrom = 0,
    int StartSubtaskFrom = 0
);

public readonly record struct NextTask;

public class ReadTasksExecutor(
    ILoggerFactory? loggerFactory = null
) : ReflectingExecutor<ReadTasksExecutor>("ReadTasksExecutor", CreateOptions()),
    IMessageHandler<StartTasks>,
    IMessageHandler<NextTask>
{
    private static ExecutorOptions CreateOptions()
    {
        var options = ExecutorOptions.Default;

        options.AutoSendMessageHandlerResultObject = false;
        options.AutoYieldOutputHandlerResultObject = false;

        return options;
    }

    private IList<string> _tasks = new List<string>();

    private int _currentIndex;

    private readonly ILogger _logger = (loggerFactory ?? NullLoggerFactory.Instance)
        .CreateLogger<ReadTasksExecutor>();

    public async ValueTask HandleAsync(
        StartTasks message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default
    )
    {
        var tasksDir = message.TasksDir;

        _logger.LogInformation("Reading tasks from: {taskDir}", tasksDir);

        if (!Directory.Exists(tasksDir))
        {
            throw new DirectoryNotFoundException($"Directory '{tasksDir}' does not exist.");
        }

        _tasks = Directory.GetDirectories(tasksDir).OrderBy(name => name).ToList();

        if (_tasks.Count == 0)
        {
            throw new InvalidOperationException($"No tasks are found in the directory '{tasksDir}'.");
        }

        _logger.LogInformation("{count} tasks found: ", _tasks.Count);

        foreach (var task in _tasks)
        {
            var title = Path.GetFileNameWithoutExtension(task);

            _logger.LogInformation(" * {title}", title);
        }

        _currentIndex = message.StartTaskFrom;

        var request = new StartSubtasks(_tasks[_currentIndex], message.StartSubtaskFrom);

        await context.SendMessageAsync(request, cancellationToken);
    }

    public async ValueTask HandleAsync(
        NextTask message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default
    )
    {
        var count = _tasks.Count;

        if (_currentIndex >= count - 1)
        {
            _logger.LogInformation("No more tasks to process.");

            await context.YieldOutputAsync($"{count} tasks completed successfully.", cancellationToken);

            _tasks = new List<string>();
            _currentIndex = 0;

            return;
        }

        _logger.LogInformation("Requesting to process the next task: {next}", _tasks[_currentIndex]);

        var request = new StartSubtasks(_tasks[++_currentIndex]);

        await context.SendMessageAsync(request, cancellationToken);
    }
}