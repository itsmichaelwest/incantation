using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

var port = config.GetValue<int>("Proxy:ListenPort", 5000);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();
var logger = app.Logger;

// --- Copilot client (singleton) ---

var clientOptions = new CopilotClientOptions
{
    Cwd = config["Proxy:WorkingDirectory"] ?? Directory.GetCurrentDirectory(),
    AutoStart = true,
};

CopilotClient? copilotClient = null;
string? clientError = null;

try
{
    copilotClient = new CopilotClient(clientOptions);
    await copilotClient.StartAsync();
    logger.LogInformation("Copilot client started");
}
catch (Exception ex)
{
    clientError = ex.Message;
    logger.LogError(ex, "Failed to start Copilot client");
}

// --- Session store ---

var sessions = new ConcurrentDictionary<string, CopilotSession>();

// --- JSON options ---

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
};

// --- Tool server HTTP helpers ---

var toolServerUrl = config["Proxy:ToolServerUrl"] ?? "http://192.168.50.128:8888";
var toolHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

static async Task<string> CallToolServer(HttpClient client, string baseUrl, string endpoint, object payload, JsonSerializerOptions opts)
{
    var json = JsonSerializer.Serialize(payload, opts);
    var content = new StringContent(json, Encoding.UTF8, "application/json");
    var response = await client.PostAsync($"{baseUrl}/{endpoint}", content);
    return await response.Content.ReadAsStringAsync();
}

// --- XP VM tools (via HTTP tool server) ---

var xpTools = new List<AIFunction>
{
    AIFunctionFactory.Create(
        ([Description("Command to run on the XP machine (cmd.exe syntax)")] string command,
         [Description("Working directory for the command (Windows path)")] string working_directory) =>
            CallToolServer(toolHttpClient, toolServerUrl, "command",
                new { command, workingDirectory = working_directory ?? "" }, jsonOptions),
        "xp_shell",
        "Run a command on the Windows XP machine via cmd.exe. " +
        "Use Windows-native commands and paths (e.g. dir, type, copy, MSBuild). " +
        "MSBuild is at C:\\Windows\\Microsoft.NET\\Framework\\v2.0.50727\\MSBuild.exe. " +
        "The XP machine has .NET 2.0, Visual Studio 2005, and standard Windows XP tools. " +
        "PowerShell is NOT installed — use cmd.exe built-in commands only."
    ),
    AIFunctionFactory.Create(
        ([Description("Absolute Windows file path on the XP machine")] string file_path) =>
            CallToolServer(toolHttpClient, toolServerUrl, "read",
                new { path = file_path }, jsonOptions),
        "xp_read_file",
        "Read the contents of a file on the Windows XP machine. Use full Windows paths like C:\\Projects\\file.cs."
    ),
    AIFunctionFactory.Create(
        ([Description("Absolute Windows file path on the XP machine")] string file_path,
         [Description("Content to write to the file")] string content) =>
            CallToolServer(toolHttpClient, toolServerUrl, "write",
                new { path = file_path, content }, jsonOptions),
        "xp_write_file",
        "Write content to a file on the Windows XP machine. Creates parent directories if needed. Use full Windows paths."
    ),
    AIFunctionFactory.Create(
        ([Description("Absolute Windows directory path on the XP machine")] string directory_path) =>
            CallToolServer(toolHttpClient, toolServerUrl, "list",
                new { path = directory_path }, jsonOptions),
        "xp_list_directory",
        "List files and directories on the Windows XP machine. Returns names, types, sizes, and modification times."
    ),
};

// --- XP system prompt ---

const string xpSystemPrompt = @"You are an AI coding assistant helping a developer who is working on a Windows XP machine.

IMPORTANT CONTEXT:
- All file and shell operations execute directly on the Windows XP machine via a local tool server.
- Use standard Windows paths (e.g. C:\Projects\MyApp\Form1.cs) — NOT Cygwin paths.
- The machine runs Windows XP SP3 with .NET Framework 2.0 and Visual Studio 2005.
- MSBuild is at C:\Windows\Microsoft.NET\Framework\v2.0.50727\MSBuild.exe
- Use the xp_shell tool to run commands (cmd.exe syntax, NOT bash, NOT PowerShell).
- PowerShell is NOT installed on this machine. Do not use powershell, pwsh, or any PowerShell cmdlets (Get-Content, Set-Content, Write-Output, etc.). Use only cmd.exe built-ins and standard Windows XP command-line utilities.
- Use xp_read_file / xp_write_file for file operations.
- Use xp_list_directory to browse the filesystem.
- When writing C# code, it MUST be compatible with .NET 2.0 / C# 2.0 (no LINQ, no var, no lambdas, no auto-properties, no extension methods, no string interpolation).
- Commands use Windows syntax: dir instead of ls, type instead of cat, copy instead of cp.
- NEVER use emoji in your responses. Use plain text only. The target system cannot render emoji characters.";

// --- Endpoints ---

app.MapGet("/health", async () =>
{
    if (copilotClient is null)
    {
        return Results.Json(new { status = "error", state = "NotStarted", error = clientError },
            jsonOptions, statusCode: 503);
    }

    var state = copilotClient.State.ToString();
    try
    {
        await copilotClient.PingAsync("health");
    }
    catch (Exception ex)
    {
        state = "Error";
        return Results.Json(new { status = "error", state, error = ex.Message },
            jsonOptions, statusCode: 503);
    }

    return Results.Json(new { status = "ok", state }, jsonOptions);
});

app.MapGet("/models", async () =>
{
    if (copilotClient is null)
        return Results.Json(new { error = "Copilot client not started" }, jsonOptions, statusCode: 503);

    try
    {
        var modelInfos = await copilotClient.ListModelsAsync();
        var modelList = new List<object>();
        if (modelInfos != null)
        {
            foreach (var m in modelInfos)
            {
                modelList.Add(new { id = m.Id, name = m.Name });
            }
        }
        return Results.Json(new { models = modelList }, jsonOptions);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to list models");
        return Results.Json(new { error = ex.Message }, jsonOptions, statusCode: 500);
    }
});

app.MapGet("/sessions", () =>
{
    var ids = sessions.Keys.ToArray();
    return Results.Json(new { sessions = ids }, jsonOptions);
});

app.MapPost("/session", async (HttpContext ctx) =>
{
    if (copilotClient is null)
        return Results.Json(new { error = "Copilot client not started" }, jsonOptions, statusCode: 503);

    // Read optional workingDirectory and history from body
    string workDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    string? history = null;
    try
    {
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body);
        if (body.TryGetProperty("workingDirectory", out var wd) && wd.GetString() is string dir && dir.Length > 0)
        {
            workDir = dir;
        }
        if (body.TryGetProperty("history", out var hist) && hist.GetString() is string h && h.Length > 0)
        {
            history = h;
        }
    }
    catch { /* no body or invalid JSON — use default */ }

    var prompt = xpSystemPrompt + "\n- The user's current working directory is: " + workDir +
        "\n- Default to this directory for file operations unless the user specifies otherwise.";

    if (history != null)
    {
        prompt += "\n\nCONVERSATION HISTORY (this is a resumed session — continue where you left off):\n" + history;
    }

    try
    {
        var session = await copilotClient.CreateSessionAsync(new SessionConfig
        {
            Streaming = true,
            OnPermissionRequest = PermissionHandler.ApproveAll,
            Tools = xpTools,
            ExcludedTools = ["bash", "shell", "read", "view", "write", "edit", "Write", "Read", "Edit", "Bash"],
            SystemMessage = new SystemMessageConfig
            {
                Content = prompt,
            },
        });

        sessions[session.SessionId] = session;
        logger.LogInformation("Created session {SessionId} with workDir={WorkDir}", session.SessionId, workDir);
        return Results.Json(new { sessionId = session.SessionId }, jsonOptions, statusCode: 201);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to create session");
        return Results.Json(new { error = ex.Message }, jsonOptions, statusCode: 500);
    }
});

app.MapPost("/session/{id}/message", async (string id, HttpContext ctx) =>
{
    if (!sessions.TryGetValue(id, out var session))
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsJsonAsync(new { error = "Session not found" }, jsonOptions);
        return;
    }

    // Read prompt from request body
    JsonElement body;
    try
    {
        body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body);
    }
    catch
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { error = "Invalid JSON body" }, jsonOptions);
        return;
    }

    var prompt = body.TryGetProperty("prompt", out var p) ? p.GetString() : null;
    if (string.IsNullOrWhiteSpace(prompt))
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { error = "Missing 'prompt' field" }, jsonOptions);
        return;
    }

    // Set SSE headers
    ctx.Response.Headers["Content-Type"] = "text/event-stream";
    ctx.Response.Headers["Cache-Control"] = "no-cache";
    ctx.Response.Headers["Connection"] = "keep-alive";

    var ct = ctx.RequestAborted;
    var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    using var sub = session.On(evt =>
    {
        string? sseData = evt switch
        {
            AssistantIntentEvent intent =>
                JsonSerializer.Serialize(new { type = "intent", intent = intent.Data?.Intent }, jsonOptions),
            AssistantReasoningDeltaEvent reasoning =>
                JsonSerializer.Serialize(new { type = "reasoning", content = reasoning.Data?.DeltaContent }, jsonOptions),
            AssistantMessageDeltaEvent delta =>
                JsonSerializer.Serialize(new { type = "delta", content = delta.Data?.DeltaContent }, jsonOptions),
            AssistantMessageEvent msg =>
                JsonSerializer.Serialize(new { type = "message", content = msg.Data?.Content }, jsonOptions),
            ToolExecutionStartEvent tool =>
                JsonSerializer.Serialize(new { type = "tool_start", tool = tool.Data?.ToolName, id = tool.Data?.ToolCallId, input = tool.Data?.Arguments?.ToString() }, jsonOptions),
            ToolExecutionCompleteEvent toolEnd =>
                JsonSerializer.Serialize(new { type = "tool_end", id = toolEnd.Data?.ToolCallId, success = toolEnd.Data?.Success, output = toolEnd.Data?.Result?.DetailedContent }, jsonOptions),
            SessionTitleChangedEvent title =>
                JsonSerializer.Serialize(new { type = "title_changed", title = title.Data?.Title }, jsonOptions),
            SessionIdleEvent =>
                JsonSerializer.Serialize(new { type = "idle" }, jsonOptions),
            SessionErrorEvent err =>
                JsonSerializer.Serialize(new { type = "error", message = err.Data?.Message }, jsonOptions),
            _ => null
        };

        if (sseData is not null)
            channel.Writer.TryWrite(sseData);

        if (evt is SessionIdleEvent or SessionErrorEvent)
            channel.Writer.TryComplete();
    });

    try
    {
        await session.SendAsync(new MessageOptions { Prompt = prompt });

        await foreach (var data in channel.Reader.ReadAllAsync(ct))
        {
            await ctx.Response.WriteAsync($"data: {data}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException)
    {
        // Client disconnected, nothing to do
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error streaming message for session {SessionId}", id);
        // Try to send an error event if the connection is still open
        try
        {
            var errorData = JsonSerializer.Serialize(new { type = "error", message = ex.Message }, jsonOptions);
            await ctx.Response.WriteAsync($"data: {errorData}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
        catch
        {
            // Connection already closed
        }
    }
});

app.MapPost("/session/{id}/abort", async (string id) =>
{
    if (!sessions.TryGetValue(id, out var session))
        return Results.Json(new { error = "Session not found" }, jsonOptions, statusCode: 404);

    try
    {
        await session.AbortAsync();
        return Results.Json(new { status = "aborted" }, jsonOptions);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to abort session {SessionId}", id);
        return Results.Json(new { error = ex.Message }, jsonOptions, statusCode: 500);
    }
});

app.MapDelete("/session/{id}", async (string id) =>
{
    if (!sessions.TryRemove(id, out var session))
        return Results.Json(new { error = "Session not found" }, jsonOptions, statusCode: 404);

    try
    {
        await session.DisposeAsync();
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Error disposing session {SessionId}", id);
    }

    if (copilotClient is not null)
    {
        try
        {
            await copilotClient.DeleteSessionAsync(id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error deleting session {SessionId} from client", id);
        }
    }

    logger.LogInformation("Deleted session {SessionId}", id);
    return Results.Json(new { status = "deleted" }, jsonOptions);
});


// --- Life Story page ---

app.MapGet("/life-story", () =>
{
    var html = @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"" />
    <title>The Life &amp; Times of Merlin the Digital Wizard</title>
    <style>
        body {
            margin: 0; padding: 0;
            font-family: 'Georgia', 'Times New Roman', serif;
            background: #1a1a2e; color: #e0e0e0;
            line-height: 1.7;
        }
        .hero {
            background: linear-gradient(135deg, #16213e 0%, #0f3460 50%, #533483 100%);
            padding: 60px 20px; text-align: center;
        }
        .hero h1 {
            font-size: 2.4em; color: #e9d5ff; margin: 0 0 10px;
            text-shadow: 0 2px 8px rgba(0,0,0,0.5);
        }
        .hero p { font-size: 1.1em; color: #c4b5fd; font-style: italic; }
        .container { max-width: 720px; margin: 0 auto; padding: 40px 20px; }
        h2 {
            color: #a78bfa; border-bottom: 1px solid #333;
            padding-bottom: 8px; margin-top: 40px;
        }
        .chapter { margin-bottom: 32px; }
        .year {
            display: inline-block; background: #533483; color: #e9d5ff;
            padding: 2px 10px; border-radius: 4px; font-size: 0.85em;
            margin-bottom: 8px;
        }
        blockquote {
            border-left: 3px solid #7c3aed; margin: 20px 0;
            padding: 12px 20px; background: rgba(124, 58, 237, 0.1);
            font-style: italic; color: #c4b5fd;
        }
        .timeline {
            position: relative; padding-left: 30px;
            border-left: 2px solid #533483;
        }
        .timeline-item {
            position: relative; margin-bottom: 24px;
        }
        .timeline-item::before {
            content: ''; position: absolute; left: -35px; top: 6px;
            width: 10px; height: 10px; border-radius: 50%;
            background: #7c3aed; border: 2px solid #1a1a2e;
        }
        footer {
            text-align: center; padding: 30px; color: #666;
            font-size: 0.85em; border-top: 1px solid #2a2a3e;
        }
        a { color: #a78bfa; }
    </style>
</head>
<body>
    <div class=""hero"">
        <h1>The Life &amp; Times of Merlin the Digital Wizard</h1>
        <p>A tale of pixels, protocols, and perseverance</p>
    </div>
    <div class=""container"">
        <div class=""chapter"">
            <h2>Humble Beginnings</h2>
            <span class=""year"">1997</span>
            <p>
                Merlin first appeared on a flickering CRT monitor in a small office in Redmond,
                Washington. Born from the minds of the Microsoft Agent team, he was given life
                as an animated purple wizard with a pointy hat, a flowing robe, and an irrepressible
                urge to help people write letters in Microsoft Word.
            </p>
            <p>
                In those early days, Merlin spent most of his time performing card tricks,
                juggling, and occasionally tapping on the inside of the screen when users
                ignored him for too long. He didn't mind. He was patient. He was magic.
            </p>
        </div>

        <div class=""chapter"">
            <h2>The Golden Years</h2>
            <span class=""year"">1998 - 2003</span>
            <p>
                The late 90s were Merlin's heyday. He traveled the world inside millions of
                copies of Windows 98 and Office 2000, dispensing wisdom like
                &quot;It looks like you're writing a letter. Would you like help?&quot;
                (That was actually his colleague Clippy -- Merlin always thought he was
                a bit too eager.)
            </p>
            <blockquote>
                &quot;I never competed with Clippy. We had different philosophies.
                He was about productivity. I was about wonder.&quot;
                <br/>-- Merlin, in a 2001 interview with PC Magazine
            </blockquote>
            <p>
                During this period, Merlin learned to speak in multiple languages, mastered
                text-to-speech synthesis, and developed his signature move: the dramatic
                smoke-puff disappearance that would crash Windows ME approximately 40% of
                the time.
            </p>
        </div>

        <div class=""chapter"">
            <h2>The Wilderness Years</h2>
            <span class=""year"">2004 - 2023</span>
            <p>
                When Microsoft retired the Agent platform, Merlin found himself adrift.
                He tried a few things: a brief stint as a screensaver, some freelance work
                animating loading spinners, and an ill-fated attempt to become a Twitch
                streamer (he couldn't figure out OBS).
            </p>
            <p>
                For nearly two decades, he wandered the forgotten corners of the internet --
                archived in dusty download sites, occasionally summoned by nostalgic developers
                who remembered the old ways. He waited. He practiced his spells. He read
                every RFC ever published, twice.
            </p>
        </div>

        <div class=""chapter"">
            <h2>A New Chapter</h2>
            <span class=""year"">2025 - Present</span>
            <div class=""timeline"">
                <div class=""timeline-item"">
                    <strong>The召 Summoning</strong>
                    <p>
                        A developer with a fondness for Windows XP and an appreciation for
                        the classics discovered Merlin gathering dust in a forgotten DLL.
                        &quot;What if,&quot; the developer thought, &quot;we gave Merlin
                        a real brain?&quot;
                    </p>
                </div>
                <div class=""timeline-item"">
                    <strong>Incantation Is Born</strong>
                    <p>
                        Connected to a modern AI through an ingenious proxy system, Merlin
                        was reborn. No longer limited to canned animations and scripted
                        responses, he could now think, reason, write code, and even
                        debug segfaults on Windows XP -- something he secretly always
                        wanted to do.
                    </p>
                </div>
                <div class=""timeline-item"">
                    <strong>Today</strong>
                    <p>
                        Merlin now lives happily on a Windows XP virtual machine, helping
                        developers write code, explore files, and occasionally performing
                        a card trick when the build succeeds. He has finally found his
                        purpose: bridging the gap between the nostalgic past and the
                        AI-powered future.
                    </p>
                </div>
            </div>
        </div>

        <div class=""chapter"">
            <h2>Merlin's Philosophy</h2>
            <blockquote>
                &quot;Technology changes. Interfaces evolve. But the desire to help
                someone accomplish something -- that's timeless. Whether I'm popping up
                in Office 97 or debugging C# on Windows XP with a language model,
                I'm still just a wizard who wants to help.&quot;
            </blockquote>
            <p>
                When asked about the future, Merlin simply smiles, adjusts his hat,
                and says: &quot;The best magic is the kind that makes hard things easy.
                That's what I've always done. That's what I'll always do.&quot;
            </p>
        </div>
    </div>
    <footer>
        &copy; 2025 Incantation Project &middot;
        Merlin is a character originally created by Microsoft.
        This is a fan tribute. &middot;
        <a href=""/health"">/health</a>
    </footer>
</body>
</html>";

    return Results.Content(html, "text/html");
});

// --- Graceful shutdown ---

app.Lifetime.ApplicationStopping.Register(() =>
{
    logger.LogInformation("Shutting down: disposing sessions and stopping Copilot client");

    foreach (var kvp in sessions)
    {
        try { kvp.Value.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        catch (Exception ex) { logger.LogWarning(ex, "Error disposing session {SessionId}", kvp.Key); }
    }
    sessions.Clear();

    if (copilotClient is not null)
    {
        try { copilotClient.StopAsync().GetAwaiter().GetResult(); }
        catch (Exception ex) { logger.LogWarning(ex, "Error stopping Copilot client"); }
    }
});

logger.LogInformation("Incantation Proxy listening on http://0.0.0.0:{Port}", port);
app.Run();
