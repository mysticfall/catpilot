using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EDPM37.Executors;

public readonly record struct StartSubtasks(
    string SubTasksDir,
    int StartSubtaskFrom = 0
);

public readonly record struct NextSubtask;

public class ReadSubtasksExecutor(
    ILoggerFactory? loggerFactory = null
) : ReflectingExecutor<ReadSubtasksExecutor>("ReadSubtasksExecutor", CreateOptions()),
    IMessageHandler<StartSubtasks>,
    IMessageHandler<NextSubtask>
{
    private static ExecutorOptions CreateOptions()
    {
        var options = ExecutorOptions.Default;

        options.AutoSendMessageHandlerResultObject = false;
        options.AutoYieldOutputHandlerResultObject = false;

        return options;
    }

    private IList<string> _subtasks = new List<string>();

    private int _currentIndex;

    private readonly ILogger _logger = (loggerFactory ?? NullLoggerFactory.Instance)
        .CreateLogger<ReadSubtasksExecutor>();

    public async ValueTask HandleAsync(
        StartSubtasks message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default
    )
    {
        var subtasksDir = message.SubTasksDir;

        _logger.LogInformation("Reading subtasks from: {subtasksDir}", subtasksDir);

        if (!Directory.Exists(subtasksDir))
        {
            throw new DirectoryNotFoundException($"Directory '{subtasksDir}' does not exist.");
        }

        _subtasks = Directory
            .GetFiles(subtasksDir, "*.md")
            .OrderBy(name => name)
            .ToList();

        if (_subtasks.Count == 0)
        {
            throw new InvalidOperationException($"No subtasks are found in the directory '{subtasksDir}'.");
        }

        _logger.LogInformation("{count} subtasks found: ", _subtasks.Count);

        _currentIndex = message.StartSubtaskFrom;

        foreach (var subtask in _subtasks)
        {
            var title = Path.GetFileNameWithoutExtension(subtask);

            _logger.LogInformation(" * {title}", title);
        }

        var request = new StartCoding(_subtasks[_currentIndex]);

        await context.SendMessageAsync(request, cancellationToken);
    }

    public async ValueTask HandleAsync(
        NextSubtask message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default
    )
    {
        var count = _subtasks.Count;

        if (_currentIndex >= count - 1)
        {
            _logger.LogInformation("All {count} subtasks have been completed successfully.", count);

            _subtasks = new List<string>();
            _currentIndex = 0;

            await context.SendMessageAsync(new NextTask(), cancellationToken);

            return;
        }

        var task = _subtasks[++_currentIndex];

        _logger.LogInformation("Requesting to process the next subtask: {next}", task);

        var request = new StartCoding(task);

        await context.SendMessageAsync(request, cancellationToken);
    }
}