using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Gwendoline.Core;

namespace Gwendoline;

/// <summary>
/// The bot token, the linked chat and the open session, kept in %LOCALAPPDATA%\Gwendoline\telegram.json.
/// The token is encrypted with Windows (DPAPI) for this user on this PC.
/// </summary>
internal sealed class TelegramConfig
{
    [JsonIgnore]
    public string Token { get; set; } = "";
    public string BotUsername { get; set; } = "";
    /// <summary>Set once the user taps Start in the chat with the bot.</summary>
    public long? ChatId { get; set; }
    /// <summary>Secret carried by the Start link, so only that tap can link a chat.</summary>
    public string? LinkCode { get; set; }
    /// <summary>The session whose opening message was sent (or tried) and whose closing message has not gone out yet.</summary>
    public TelegramSession? Session { get; set; }

    [JsonPropertyName("token")]
    public string ProtectedToken
    {
        get => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(Token), null, DataProtectionScope.CurrentUser));
        set => Token = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
    }

    private static string File => Path.Combine(Paths.Data, "telegram.json");

    public static TelegramConfig? Load()
    {
        try
        {
            return JsonSerializer.Deserialize<TelegramConfig>(System.IO.File.ReadAllText(File));
        }
        catch (Exception error) when (error is IOException or JsonException or CryptographicException or FormatException)
        {
            return null;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Paths.Data);
        var temporary = File + ".tmp";
        System.IO.File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        System.IO.File.Move(temporary, File, overwrite: true);
    }

    public static void Delete()
    {
        try
        {
            System.IO.File.Delete(File);
        }
        catch (IOException)
        {
            // Already gone or in use: the next connect overwrites it.
        }
    }
}

internal sealed class TelegramSession
{
    /// <summary>When the session started, in Unix milliseconds. Also ties the off button to it.</summary>
    public long Start { get; set; }
    /// <summary>
    /// The closing message, written the moment the session ends, so a crash or a restart
    /// before it goes out still closes the session with the right words.
    /// </summary>
    public string? ClosingText { get; set; }
    /// <summary>Why it will end, when Gwendoline quit while it could not end it.</summary>
    public CloseReason? EndReason { get; set; }
}

/// <summary>A Bot API error, with Telegram's error code when there is one.</summary>
internal sealed class TelegramFailure(int? code, string message, int? retryAfter = null) : Exception(message)
{
    public int? Code { get; } = code;
    public int? RetryAfter { get; } = retryAfter;
}

/// <summary>A thin client for the few Bot API methods Gwendoline uses.</summary>
internal static class TelegramApi
{
    private static readonly HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly JsonSerializerOptions options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static async Task<JsonElement> Call(string token, string method, object body, int timeoutSeconds = 15, CancellationToken cancel = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        HttpResponseMessage response;
        string text;
        try
        {
            response = await http.PostAsync($"https://api.telegram.org/bot{token}/{method}", JsonContent.Create(body, options: options), deadline.Token);
            text = await response.Content.ReadAsStringAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new TelegramFailure(null, "Telegram did not answer in time.");
        }
        catch (HttpRequestException)
        {
            // Never the exception text: it could carry the URL, and the token with it.
            throw new TelegramFailure(null, "Could not reach Telegram. Check the internet connection.");
        }

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(text).RootElement;
        }
        catch (JsonException)
        {
            // Not a Bot API answer (a proxy, an outage page): no Telegram error code, so it is retried like a network error.
            throw new TelegramFailure(null, $"Telegram sent an unexpected answer (HTTP {(int)response.StatusCode}).");
        }
        if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean() && root.TryGetProperty("result", out var result))
        {
            return result;
        }
        int? code = root.TryGetProperty("error_code", out var c) ? c.GetInt32() : null;
        var description = root.TryGetProperty("description", out var d) ? d.GetString() : null;
        int? retryAfter = root.TryGetProperty("parameters", out var p) && p.TryGetProperty("retry_after", out var r) ? r.GetInt32() : null;
        throw new TelegramFailure(code, description ?? "Telegram refused the request.", retryAfter);
    }
}

/// <summary>
/// Event-driven, cancellable waits. A revision prevents lost wakeups when a producer signals
/// between inspecting state and starting to wait. Used from the UI thread only.
/// </summary>
internal sealed class WakeSignal
{
    private TaskCompletionSource next = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Revision { get; private set; }

    public void Signal()
    {
        Revision++;
        var current = next;
        next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        current.TrySetResult();
    }

    /// <summary>Returns true when signalled after <paramref name="revision"/>, false on timeout or cancellation.</summary>
    public async Task<bool> Wait(int revision, TimeSpan timeout, CancellationToken cancel = default)
    {
        if (cancel.IsCancellationRequested || timeout <= TimeSpan.Zero)
        {
            return false;
        }
        if (revision != Revision)
        {
            return true;
        }
        var signalled = next.Task;
        var finished = await Task.WhenAny(signalled, Task.Delay(timeout, cancel));
        return finished == signalled;
    }
}

/// <summary>
/// Talks to the user's own bot. No server involved: Gwendoline asks Telegram for new messages with long polling.
///
/// Every session, from Gwendoline on to Gwendoline off, gets an opening message and, whatever ends it,
/// a closing message that stays the last one of the session. Messages leave one at a time, in order,
/// from an outbox. A session is written to disk before its opening is sent and erased once its closing
/// went out, so a crash, a power loss or a network outage still ends with a closing message, on the
/// next launch at the latest. Everything runs on the UI thread.
/// </summary>
internal sealed partial class TelegramBot
{
    public enum Command { Status, Off }

    /// <summary>Called for each command from the linked chat.</summary>
    public Action<Command>? OnCommand { get; set; }

    private TelegramConfig? config = TelegramConfig.Load();
    private bool persistencePending;
    private string? storageProblem;
    /// <summary>Lasting problems for the menu. Sending and polling keep their own, so one success cannot hide the other.</summary>
    private string? sendProblem;
    private string? pollProblem;
    private readonly WakeSignal retrySignal = new();
    private readonly WakeSignal outboxSignal = new();

    private readonly List<Outgoing> outbox = [];
    private Task? worker;
    /// <summary>The message whose request is in flight: it may already be delivered, so it is never pulled out.</summary>
    private Guid? sending;
    /// <summary>Set when a session message is queued or a drain starts, to cut a retry wait short.</summary>
    private bool retryNow;
    /// <summary>Bumped by Stop(), so a worker stopped by a reconnect cannot touch the new outbox.</summary>
    private int generation;
    /// <summary>Disconnecting: the last message is on its way, nothing new may be queued behind it.</summary>
    private bool leaving;
    /// <summary>Drains waiting right now (quit, held sleep, disconnect): retries come every second, not after a long backoff.</summary>
    private int drains;
    /// <summary>Why the session will end, when Gwendoline quit while it could not end it, for an opening not yet on disk.</summary>
    private CloseReason? quitEndReason;
    private CancellationTokenSource? polling;
    private long? offset;

    /// <summary>Reminders and replies older than this are dropped rather than sent late.</summary>
    private static readonly TimeSpan staleAfter = TimeSpan.FromMinutes(2);
    /// <summary>Retries start after <c>firstRetry</c> seconds and double up to <c>longRetry</c>, the wait for errors that last.</summary>
    private const int firstRetry = 5;
    private const int longRetry = 60;
    private const int transientLimit = 100;

    private enum Kind
    {
        /// <summary>Opens the session that started at <c>Start</c>.</summary>
        Opening,
        /// <summary>A reminder, the charging message or a warning, dropped once the session closes or after 2 minutes.</summary>
        Notice,
        /// <summary>An answer to a command, dropped after 2 minutes.</summary>
        Reply,
        /// <summary>Closes the session that started at <c>Start</c>. Never dropped, it waits for the network.</summary>
        Closing,
        /// <summary>The last message of a session still running when Telegram gets disconnected.</summary>
        Farewell,
    }

    /// <summary><paramref name="Permission"/>: adds Allow and Deny buttons for that Claude Code permission request.</summary>
    private sealed record Outgoing(Kind Kind, string Text, long? Start = null, long? OffButton = null, string? Permission = null)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Created { get; set; } = DateTime.UtcNow;

        /// <summary>The session this message opens or ends.</summary>
        public long? Session => Kind is Kind.Opening or Kind.Closing or Kind.Farewell ? Start : null;

        /// <summary>A closing or a farewell: the last message of its session.</summary>
        public bool EndsSession => Kind is Kind.Closing or Kind.Farewell;
    }

    public string? Problem => storageProblem ?? sendProblem ?? pollProblem;
    public bool IsLinked => config?.ChatId != null;
    public bool IsWaitingForStart => config != null && config.ChatId == null;
    public string? BotUsername => config?.BotUsername;
    public bool HasPendingMessages => outbox.Count > 0;

    private bool RetryNow
    {
        get => retryNow;
        set
        {
            retryNow = value;
            if (value)
            {
                retrySignal.Signal();
            }
        }
    }

    private void OutboxChanged() => outboxSignal.Signal();

    /// <summary>
    /// The start of the session the chat sees as running: persisted without a closing text,
    /// or with its opening queued, and with no closing or farewell queued after it.
    /// </summary>
    private long? ActiveStart
    {
        get
        {
            long? active = config?.Session is { ClosingText: null } session ? session.Start : null;
            foreach (var item in outbox)
            {
                switch (item.Kind)
                {
                    case Kind.Opening: active = item.Start; break;
                    case Kind.Closing or Kind.Farewell: active = null; break;
                }
            }
            return active;
        }
    }

    /// <summary>The links that open the bot chat with the secret code, while waiting for Start.</summary>
    public (string App, string Web)? StartLinks
    {
        get
        {
            if (config?.LinkCode is not string code)
            {
                return null;
            }
            return ($"tg://resolve?domain={config.BotUsername}&start={code}", $"https://t.me/{config.BotUsername}?start={code}");
        }
    }

    private bool Persist()
    {
        if (config == null)
        {
            return true;
        }
        try
        {
            config.Save();
            persistencePending = false;
            storageProblem = null;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            persistencePending = true;
            storageProblem = "Telegram: settings could not be saved; check disk space and permissions";
            return false;
        }
    }

    // Lifecycle

    /// <summary>
    /// Starts listening. A session left open by a quit while offline, a crash or a restart gets its closing now,
    /// unless Gwendoline could not give sleep back at launch: then it is still running and stays open.
    /// </summary>
    public void Start(DateTimeOffset bootedAt, PowerState state)
    {
        if (config == null)
        {
            return;
        }
        if (config.Session is TelegramSession session && (session.ClosingText != null || !state.SleepDisabled))
        {
            var rebooted = bootedAt.ToUnixTimeMilliseconds() > session.Start;
            var reason = session.EndReason ?? (rebooted ? CloseReason.UnexpectedRestart : CloseReason.Crash);
            var text = session.ClosingText ?? TelegramText.Closed(reason, state);
            if (session.ClosingText == null)
            {
                session.ClosingText = text;
                Persist();
            }
            Enqueue(new Outgoing(Kind.Closing, text, session.Start));
        }
        else if (config.Session?.EndReason != null)
        {
            // Still running after all: the reason of an earlier quit no longer applies.
            config.Session.EndReason = null;
            Persist();
        }
        if (IsLinked)
        {
            SetCommands();
        }
        StartPolling();
    }

    /// <summary>Checks the token with Telegram and saves it. <see cref="StartLinks"/> then open the chat to tap Start.</summary>
    public async Task Connect(string raw)
    {
        var token = raw.Trim();
        if (!TokenPattern().IsMatch(token))
        {
            throw new TelegramFailure(null, "This does not look like a bot token. Copy the whole token BotFather sent, it looks like 123456789:AAH...");
        }
        JsonElement bot, webhook;
        try
        {
            bot = await TelegramApi.Call(token, "getMe", new { });
            webhook = await TelegramApi.Call(token, "getWebhookInfo", new { });
        }
        catch (TelegramFailure failure) when (failure.Code is 401 or 404)
        {
            throw new TelegramFailure(failure.Code, "Telegram does not know this token. Copy it again from BotFather.");
        }
        if (!bot.TryGetProperty("username", out var username) || username.GetString() is not string name)
        {
            throw new TelegramFailure(null, "Telegram did not return the bot name.");
        }
        // Another service receives this bot's messages: Gwendoline would never see Start.
        if (webhook.TryGetProperty("url", out var url) && !string.IsNullOrEmpty(url.GetString()))
        {
            throw new TelegramFailure(null, "This bot is already used by another service. Create a new bot with /newbot in BotFather.");
        }

        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        var code = new string(Enumerable.Range(0, 16).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
        await Disconnect();
        config = new TelegramConfig { Token = token, BotUsername = name, LinkCode = code };
        if (!Persist())
        {
            var problem = storageProblem;
            config = null;
            throw new TelegramFailure(null, problem ?? "Telegram settings could not be saved.");
        }
        StartPolling();
    }

    /// <summary>
    /// Leaves the chat in a clean state, then forgets the bot. A session still running gets a last message
    /// saying the chat stops here, a closing already queued gets a few seconds to go out.
    /// Returns why that last message could not leave, null when it did.
    /// </summary>
    public async Task<string?> Disconnect()
    {
        if (leaving)
        {
            return null;
        }
        if (IsLinked && ActiveStart is long start && !DropUnannouncedOpening(start))
        {
            Enqueue(new Outgoing(Kind.Farewell, TelegramText.Disconnected, start));
        }
        // From here on nothing may follow the last message: no polling, no new opening, notice or reply.
        leaving = true;
        polling?.Cancel();
        polling = null;
        await Drain(TimeSpan.FromSeconds(5));
        var stuck = outbox.Any(item => item.EndsSession);
        var problem = stuck ? (sendProblem ?? "Telegram could not be reached in time, the chat keeps its previous message.") : null;
        Stop();
        TelegramConfig.Delete();
        config = null;
        return problem;
    }

    /// <summary>Gwendoline quits while it stays on: the next launch closes the session with this reason.</summary>
    public void KeepOpenAfterQuit(CloseReason reason)
    {
        quitEndReason = reason;
        if (!IsLinked || ActiveStart is not long start || config?.Session?.Start != start)
        {
            return;
        }
        config.Session.EndReason = reason;
        Persist();
    }

    private void Stop()
    {
        ReleasePermissions();
        generation++;
        leaving = false;
        polling?.Cancel();
        polling = null;
        worker = null;
        sending = null;
        outbox.Clear();
        OutboxChanged();
        offset = null;
        sendProblem = null;
        pollProblem = null;
        storageProblem = null;
        persistencePending = false;
        retrySignal.Signal();
    }

    // Session

    /// <summary>
    /// Lines the chat up with the real state: an opening message once Gwendoline is on,
    /// a closing one when it went off without Gwendoline closing it (from Windows settings, for example).
    /// </summary>
    public void Sync(PowerState state)
    {
        if (!IsLinked || leaving)
        {
            return;
        }
        if (state.SleepDisabled)
        {
            if (ActiveStart == null)
            {
                var start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Enqueue(new Outgoing(Kind.Opening, TelegramText.On(state), start, OffButton: start));
            }
        }
        else if (ActiveStart != null)
        {
            Close(CloseReason.Elsewhere, state);
        }
    }

    /// <summary>Ends the session the chat sees as running, if any, with this reason.</summary>
    public void Close(CloseReason reason, PowerState state)
    {
        // Back at the PC: Claude Code asks in the terminal again.
        ReleasePermissions();
        if (!IsLinked || leaving || ActiveStart is not long start)
        {
            return;
        }
        // Reminders still waiting are no longer true.
        RemoveNotices();
        // Turned on and off before the opening was ever tried: nothing was announced, nothing to close.
        if (DropUnannouncedOpening(start))
        {
            return;
        }
        var text = TelegramText.Closed(reason, state);
        if (config?.Session?.Start == start)
        {
            config.Session.ClosingText = text;
            Persist();
        }
        Enqueue(new Outgoing(Kind.Closing, text, start));
    }

    /// <summary>A reminder, the charging message or a warning, with a Turn Gwendoline off button when asked.</summary>
    public void Notify(string text, bool offButton)
    {
        if (!IsLinked || leaving || ActiveStart is not long start)
        {
            return;
        }
        Enqueue(new Outgoing(Kind.Notice, text, OffButton: offButton ? start : null));
    }

    public void Reply(string text)
    {
        if (!IsLinked || leaving)
        {
            return;
        }
        Enqueue(new Outgoing(Kind.Reply, text));
    }

    /// <summary>Waits until every queued message went out, or the timeout.</summary>
    public async Task Drain(TimeSpan timeout)
    {
        RetryNow = true;
        drains++;
        try
        {
            var deadline = DateTime.UtcNow + timeout;
            while (outbox.Count > 0 && DateTime.UtcNow < deadline)
            {
                await outboxSignal.Wait(outboxSignal.Revision, deadline - DateTime.UtcNow);
            }
        }
        finally
        {
            drains--;
        }
    }

    private void RemoveNotices()
    {
        outbox.RemoveAll(item => item.Kind == Kind.Notice && item.Id != sending);
        OutboxChanged();
    }

    /// <summary>
    /// Removes the queued opening of this session, and its reminders, if it was never tried
    /// (the session is not on disk yet). Returns true when it did.
    /// </summary>
    private bool DropUnannouncedOpening(long start)
    {
        if (config?.Session?.Start == start)
        {
            return false;
        }
        var index = outbox.FindIndex(item => item.Kind == Kind.Opening && item.Start == start);
        if (index < 0 || outbox[index].Id == sending)
        {
            return false;
        }
        outbox.RemoveAt(index);
        RemoveNotices();
        return true;
    }

    // Outbox

    private void Prune()
    {
        var now = DateTime.UtcNow;
        if (outbox.RemoveAll(item => item.Session == null && item.Id != sending && now - item.Created > staleAfter) > 0)
        {
            OutboxChanged();
        }
    }

    private void Enqueue(Outgoing item)
    {
        Prune();
        if (item.Session == null && outbox.Count(o => o.Session == null) >= transientLimit)
        {
            var index = outbox.FindIndex(o => o.Session == null && o.Id != sending);
            if (index >= 0)
            {
                outbox.RemoveAt(index);
            }
        }
        item.Created = DateTime.UtcNow;
        outbox.Add(item);
        OutboxChanged();
        if (item.Session != null)
        {
            RetryNow = true;
        }
        // A worker that found nothing to send finishes before it is even stored: check IsCompleted too.
        if (worker == null || worker.IsCompleted)
        {
            worker = RunOutbox(generation);
        }
    }

    private async Task RunOutbox(int generation)
    {
        var delay = firstRetry;
        while (generation == this.generation && outbox.Count > 0 && config is { ChatId: long chatId } current)
        {
            var item = outbox[0];
            if (item.Session == null && DateTime.UtcNow - item.Created > staleAfter)
            {
                outbox.RemoveAt(0);
                OutboxChanged();
                continue;
            }
            if (item.Kind == Kind.Opening && current.Session?.Start != item.Start)
            {
                // Written before sending: if the answer is lost and Gwendoline stops, the next launch still closes it.
                current.Session = new TelegramSession
                {
                    Start = item.Start!.Value,
                    ClosingText = QueuedClosingText(item.Start.Value),
                    EndReason = quitEndReason,
                };
                persistencePending = true;
            }
            // Never announce a session before its recovery record is durable.
            if (persistencePending && !Persist())
            {
                await Backoff(firstRetry, interruptible: true);
                continue;
            }

            object? keyboard = item.OffButton is long session
                ? new { inline_keyboard = new[] { new[] { new { text = TelegramText.OffButton, callback_data = OffCallback(session) } } } }
                : item.Permission is string permission
                    ? new { inline_keyboard = new[] { new[] {
                        new { text = ClaudeText.AllowButton, callback_data = PermissionCallback(permission, true) },
                        new { text = ClaudeText.DenyButton, callback_data = PermissionCallback(permission, false) } } } }
                    : null;
            sending = item.Id;
            // Anything queued from now on, while the request is in flight, cuts the next wait short.
            RetryNow = false;
            TelegramFailure? failure = null;
            try
            {
                await TelegramApi.Call(current.Token, "sendMessage", new { chat_id = chatId, text = item.Text, reply_markup = keyboard });
            }
            catch (TelegramFailure error)
            {
                failure = error;
            }
            if (generation != this.generation || outbox.Count == 0 || outbox[0].Id != item.Id)
            {
                continue;
            }
            sending = null;

            if (failure == null)
            {
                sendProblem = null;
                delay = firstRetry;
                outbox.RemoveAt(0);
                OutboxChanged();
                Sent(item);
                continue;
            }
            sendProblem = LastingProblem(failure) ?? sendProblem;
            // Close() spared this reminder only because it was in flight. It failed, and its session is over.
            if (item.Kind == Kind.Notice && outbox.Any(o => o.EndsSession))
            {
                outbox.RemoveAt(0);
                OutboxChanged();
                continue;
            }
            var lasting = failure.Code is 401 or 403 or 404;
            if (failure.Code == 400)
            {
                // A malformed request never succeeds: give up on it rather than block the queue.
                outbox.RemoveAt(0);
                OutboxChanged();
                Sent(item);
                continue;
            }
            if (lasting && item.Session == null)
            {
                outbox.RemoveAt(0);
                OutboxChanged();
                continue;
            }
            var wait = failure.RetryAfter ?? (lasting ? longRetry : (drains > 0 ? 1 : delay));
            if (await Backoff(wait, interruptible: failure.RetryAfter == null))
            {
                delay = firstRetry;
            }
            else if (failure.RetryAfter == null)
            {
                delay = Math.Min(delay * 2, longRetry);
            }
        }
        if (generation == this.generation)
        {
            worker = null;
        }
    }

    /// <summary>
    /// Waits before a retry. A session message queued or a drain cuts the wait short, except a 429 retry_after.
    /// Returns true when cut short.
    /// </summary>
    private async Task<bool> Backoff(int seconds, bool interruptible)
    {
        Prune();
        if (!interruptible)
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            return false;
        }
        if (RetryNow)
        {
            retryNow = false;
            return true;
        }
        var changed = await retrySignal.Wait(retrySignal.Revision, TimeSpan.FromSeconds(seconds));
        if (changed)
        {
            retryNow = false;
        }
        return changed;
    }

    /// <summary>The closing text already queued for a session whose opening is about to be sent.</summary>
    private string? QueuedClosingText(long start)
    {
        return outbox.FirstOrDefault(item => item.Kind == Kind.Closing && item.Start == start)?.Text;
    }

    /// <summary>Erases the session once its closing or farewell went out.</summary>
    private void Sent(Outgoing item)
    {
        if (item.EndsSession && config is { Session: { } session } && session.Start == item.Start)
        {
            config.Session = null;
            Persist();
        }
    }

    /// <summary>The data of an off button, tied to the session it was sent in.</summary>
    private static string OffCallback(long session) => $"off:{session}";

    private static string? LastingProblem(TelegramFailure failure)
    {
        return failure.Code switch
        {
            401 or 404 => "Telegram: the bot token no longer works, connect again",
            403 => "Telegram: the bot is blocked, open its chat and tap Restart",
            409 => "Telegram: another app or PC reads this bot",
            _ => null,
        };
    }

    // Polling

    private void StartPolling()
    {
        polling?.Cancel();
        polling = new CancellationTokenSource();
        _ = Poll(polling.Token);
    }

    private async Task Poll(CancellationToken cancel)
    {
        var delay = firstRetry;
        while (!cancel.IsCancellationRequested && config is TelegramConfig current)
        {
            try
            {
                var updates = await TelegramApi.Call(current.Token, "getUpdates",
                    new { offset, timeout = 50, allowed_updates = new[] { "message", "callback_query" } },
                    timeoutSeconds: 65, cancel: cancel);
                pollProblem = null;
                delay = firstRetry;
                foreach (var update in updates.EnumerateArray())
                {
                    if (cancel.IsCancellationRequested)
                    {
                        break;
                    }
                    offset = update.GetProperty("update_id").GetInt64() + 1;
                    Handle(update);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (TelegramFailure failure)
            {
                pollProblem = LastingProblem(failure) ?? pollProblem;
                // A dead token or a conflict will not fix itself on the first retry: ask less often.
                int wait;
                switch (failure.Code)
                {
                    case 401 or 404: wait = longRetry; break;
                    case 409: wait = 30; break;
                    default:
                        wait = failure.RetryAfter ?? delay;
                        delay = Math.Min(delay * 2, longRetry);
                        break;
                }
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(wait), cancel);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void Handle(JsonElement update)
    {
        if (config == null)
        {
            return;
        }

        if (update.TryGetProperty("callback_query", out var query))
        {
            var queryId = query.GetProperty("id").GetString()!;
            long? fromChat = query.TryGetProperty("message", out var queryMessage) ? queryMessage.GetProperty("chat").GetProperty("id").GetInt64() : null;
            if (config.ChatId is not long linked || fromChat != linked)
            {
                Answer(queryId, null);
                return;
            }
            var data = query.TryGetProperty("data", out var d) ? d.GetString() : null;
            if (data?.StartsWith("perm:") == true)
            {
                var parts = data.Split(':');
                var answered = parts.Length == 3 && Resolve(parts[1], parts[2] == "y");
                Answer(queryId, answered ? null : ClaudeText.TooLate);
                return;
            }
            if (ActiveStart is long active && data == OffCallback(active))
            {
                Answer(queryId, null);
                OnCommand?.Invoke(Command.Off);
            }
            else
            {
                Answer(queryId, ActiveStart == null ? TelegramText.AlreadyOff : TelegramText.StaleButton);
            }
            return;
        }

        if (!update.TryGetProperty("message", out var message)
            || message.GetProperty("chat").GetProperty("type").GetString() != "private"
            || !message.TryGetProperty("text", out var textElement)
            || textElement.GetString() is not string text)
        {
            return;
        }
        var chat = message.GetProperty("chat").GetProperty("id").GetInt64();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // "/status@gwendoline_bot" is how commands look when picked from the menu in some clients.
        var command = words.Length > 0 ? words[0].Split('@')[0].ToLowerInvariant() : "";

        if (config.ChatId is not long chatId)
        {
            if (command == "/start" && words.Length == 2 && words[1] == config.LinkCode)
            {
                Link(chat);
            }
            else
            {
                FireAndForget("sendMessage", new { chat_id = chat, text = TelegramText.UseTheLink });
            }
            return;
        }
        if (chat != chatId)
        {
            return;
        }

        var sentAt = DateTimeOffset.FromUnixTimeSeconds(message.GetProperty("date").GetInt64());
        switch (command)
        {
            case "/status":
                // Typed while the PC slept, it arrives on wake: past a few minutes the answer is not wanted.
                if (DateTimeOffset.UtcNow - sentAt < TimeSpan.FromMinutes(5))
                {
                    OnCommand?.Invoke(Command.Status);
                }
                break;
            case "/off":
                if (ActiveStart is not long active)
                {
                    Reply(TelegramText.AlreadyOff);
                }
                // Typed before Gwendoline was turned on again: it was meant for the previous session.
                // 10 s of slack for the gap between Telegram's clock and the PC's.
                else if (sentAt.AddSeconds(10).ToUnixTimeMilliseconds() < active)
                {
                    Reply(TelegramText.LateOff);
                }
                else
                {
                    OnCommand?.Invoke(Command.Off);
                }
                break;
            default:
                // "oui", "ok", "non"... answer the Claude Code request waiting, when there is exactly one.
                if (permissions.Count > 0 && ClaudeText.ParseAnswer(text) is bool allowed)
                {
                    if (permissions.Count == 1)
                    {
                        Resolve(permissions[0].Id, allowed);
                    }
                    else
                    {
                        Reply(ClaudeText.Several);
                    }
                }
                else
                {
                    Reply(TelegramText.Help);
                }
                break;
        }
    }

    // Claude Code permissions

    /// <summary>Requests from Claude Code waiting for an answer in the chat, oldest first.</summary>
    private readonly List<(string Id, TaskCompletionSource<bool?> Answer)> permissions = [];
    private static readonly TimeSpan permissionWait = TimeSpan.FromMinutes(55);

    /// <summary>
    /// Asks in the chat whether Claude Code may go on. Returns true or false with the answer, or null when
    /// there is nobody to ask (Gwendoline off, Telegram not linked), nobody answered in time, or Gwendoline
    /// turned off meanwhile: Claude Code then asks in the terminal as usual.
    /// </summary>
    public async Task<bool?> AskPermission(string text)
    {
        if (!IsLinked || leaving || ActiveStart is not long start)
        {
            return null;
        }
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var answer = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        permissions.Add((id, answer));
        Enqueue(new Outgoing(Kind.Notice, text, Permission: id));
        if (await Task.WhenAny(answer.Task, Task.Delay(permissionWait)) != answer.Task && Resolve(id, null))
        {
            Notify(ClaudeText.Expired, offButton: false);
        }
        return await answer.Task;
    }

    /// <summary>Settles a waiting request. Returns false when it no longer waits.</summary>
    private bool Resolve(string id, bool? allowed)
    {
        var index = permissions.FindIndex(p => p.Id == id);
        if (index < 0)
        {
            return false;
        }
        var answer = permissions[index].Answer;
        permissions.RemoveAt(index);
        answer.TrySetResult(allowed);
        if (allowed is bool given)
        {
            Reply(ClaudeText.Answered(given, Random.Shared));
        }
        return true;
    }

    /// <summary>Gwendoline turned off or Telegram went away: every waiting request goes back to the terminal.</summary>
    private void ReleasePermissions()
    {
        foreach (var (id, _) in permissions.ToList())
        {
            Resolve(id, null);
        }
    }

    private static string PermissionCallback(string id, bool allow) => $"perm:{id}:{(allow ? "y" : "n")}";

    private void Link(long chatId)
    {
        config!.ChatId = chatId;
        config.LinkCode = null;
        Persist();
        SetCommands();
        Reply(TelegramText.Connected);
    }

    private void Answer(string queryId, string? text)
    {
        FireAndForget("answerCallbackQuery", new { callback_query_id = queryId, text });
    }

    private void SetCommands()
    {
        FireAndForget("setMyCommands", new { commands = TelegramText.Commands.Select(c => new { command = c.Command, description = c.Description }) });
    }

    /// <summary>A request outside the outbox, for calls that do not matter if they are lost.</summary>
    private async void FireAndForget(string method, object body)
    {
        if (config?.Token is not string token)
        {
            return;
        }
        try
        {
            await TelegramApi.Call(token, method, body);
        }
        catch (TelegramFailure)
        {
            // Lost: nothing depends on it.
        }
    }

    [GeneratedRegex("^[0-9]+:[A-Za-z0-9_-]{30,}$")]
    private static partial Regex TokenPattern();
}
