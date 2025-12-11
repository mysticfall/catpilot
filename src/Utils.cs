using HandlebarsDotNet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EDPM37;

public static class Utils
{
    public static async Task<string> ReadPrompt(
        string path,
        IDictionary<string, object?> context,
        ILogger? logger = null)
    {
        var log = logger ?? NullLogger.Instance;

        log.LogDebug("Reading prompt from: {path}", path);

        var text = await File.ReadAllTextAsync(path);

        var template = Handlebars.Compile(text);
        var prompt = template(context);

        log.LogTrace("Using a prompt: {prompt}", prompt);

        return prompt;
    }

    public static string ExtractJson(this string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');

        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return text.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        return text;
    }
}