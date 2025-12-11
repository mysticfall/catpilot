using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace EDPM37.Executors;

[Experimental("MEAI001")]
public class ToolMessageReducer(int head = 2, int tail = 10) : IChatReducer
{
    public Task<IEnumerable<ChatMessage>> ReduceAsync(IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var list = messages.ToList();

        if (list.Count < head + tail)
        {
            return Task.FromResult(list.AsEnumerable());
        }

        var reduced = list.Take(head).Concat(list.TakeLast(tail));

        return Task.FromResult(reduced);
    }
}