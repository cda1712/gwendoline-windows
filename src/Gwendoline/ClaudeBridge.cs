using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gwendoline;

/// <summary>
/// Claude Code hooks reach the running app through a named pipe, open to this user only.
/// Only the app may read the bot's messages (Telegram allows one reader per bot), so a hook
/// that waits for an answer has to wait through it.
///
/// <c>Gwendoline.exe --claude</c> is the hook: it reads the hook input on stdin, sends one line
/// to the app, and for a permission request prints the decision Claude Code reads on stdout.
/// It never blocks Claude Code when Gwendoline is not running: it just exits.
/// </summary>
internal static class ClaudeBridge
{
    private const string PipeName = "Gwendoline.Claude";

    /// <summary>One hook call: what Claude Code asks, in the app's words.</summary>
    public sealed record Request(string Event, string Project, string? Tool = null, string? Detail = null);

    // App side

    /// <summary>Answers hook calls until the app exits. <paramref name="handle"/> runs on the UI thread.</summary>
    public static async void Serve(SynchronizationContext ui, Func<Request, Task<bool?>> handle)
    {
        while (true)
        {
            var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.WaitForConnectionAsync();
            }
            catch (IOException)
            {
                pipe.Dispose();
                continue;
            }
            _ = Answer(pipe, ui, handle);
        }
    }

    private static async Task Answer(NamedPipeServerStream pipe, SynchronizationContext ui, Func<Request, Task<bool?>> handle)
    {
        await using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                var line = await reader.ReadLineAsync();
                if (line == null || JsonSerializer.Deserialize<Request>(line) is not Request request)
                {
                    return;
                }
                var decision = new TaskCompletionSource<bool?>();
                ui.Post(async _ =>
                {
                    try
                    {
                        decision.SetResult(await handle(request));
                    }
                    catch (Exception error)
                    {
                        decision.SetException(error);
                    }
                }, null);
                var allowed = await decision.Task;
                var answer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { allowed }) + "\n");
                await pipe.WriteAsync(answer);
                await pipe.FlushAsync();
            }
            catch (Exception error) when (error is IOException or JsonException)
            {
                // The hook went away (Claude Code stopped waiting): nothing to answer.
            }
        }
    }

    // Hook side

    /// <summary>Runs as a Claude Code hook. Always exits 0: a failed hook must never get in Claude's way.</summary>
    public static int RunHook()
    {
        try
        {
            var input = JsonNode.Parse(Console.In.ReadToEnd());
            if (input == null || ToRequest(input) is not Request request)
            {
                return 0;
            }
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(1000);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true);
            writer.WriteLine(JsonSerializer.Serialize(request));
            writer.Flush();
            using var reader = new StreamReader(pipe, Encoding.UTF8);
            var answer = reader.ReadLine();
            if (request.Event != "permission" || answer == null)
            {
                return 0;
            }
            if (JsonNode.Parse(answer)?["allowed"]?.GetValue<bool?>() is bool allowed)
            {
                var decision = allowed
                    ? new JsonObject { ["behavior"] = "allow" }
                    : new JsonObject { ["behavior"] = "deny", ["message"] = "Refused from Telegram." };
                Console.Out.Write(new JsonObject
                {
                    ["hookSpecificOutput"] = new JsonObject { ["hookEventName"] = "PermissionRequest", ["decision"] = decision },
                }.ToJsonString());
                Console.Out.Flush();
            }
        }
        catch (Exception error) when (error is TimeoutException or IOException or JsonException or InvalidOperationException)
        {
            // Gwendoline is not running or went away: Claude Code carries on as if there were no hook.
        }
        return 0;
    }

    private static Request? ToRequest(JsonNode input)
    {
        var cwd = input["cwd"]?.GetValue<string>() ?? "";
        var project = Path.GetFileName(cwd.TrimEnd('/', '\\'));
        project = string.IsNullOrEmpty(project) ? "?" : project;
        switch (input["hook_event_name"]?.GetValue<string>())
        {
            case "Stop":
                return new Request("done", project);
            case "PermissionRequest":
                var tool = input["tool_name"]?.GetValue<string>() ?? "?";
                return new Request("permission", project, tool, Detail(input["tool_input"]));
            default:
                return null;
        }
    }

    /// <summary>The part of a tool call worth reading on a phone: the command, the file, the address.</summary>
    private static string? Detail(JsonNode? toolInput)
    {
        if (toolInput is not JsonObject fields)
        {
            return null;
        }
        foreach (var key in new[] { "command", "file_path", "url", "query", "pattern", "path", "description" })
        {
            if (fields[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }
        return fields.ToJsonString();
    }
}
