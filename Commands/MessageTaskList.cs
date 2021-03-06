using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;

namespace Nedordle.Commands;

public enum MessageTaskState
{
    Done,
    InProgress,
    Failed
}

public class MessageTask
{
    public string Log = "";
    public MessageTaskState State = MessageTaskState.InProgress;

    public MessageTask(string name)
    {
        Name = name;
    }

    public string Name { get; }
}

public class MessageTaskFailedException : Exception
{
    public MessageTaskFailedException(string message, string commandName, MessageTask task) : base(message)
    {
        Task = task;
        CommandName = commandName;
    }

    public MessageTask Task { get; }
    public string CommandName { get; }
}

public class MessageTaskList
{
    private readonly DiscordEmbedBuilder _builder;
    private readonly InteractionContext _context;

    private readonly Dictionary<MessageTaskState, char> _stateConverter = new()
    {
        {MessageTaskState.Done, '✓'},
        {MessageTaskState.Failed, '✗'},
        {MessageTaskState.InProgress, '◌'}
    };

    private readonly string _successDescription;
    private readonly Dictionary<string, MessageTask> _tasks;
    private bool _finished;
    private int _index;

    public MessageTaskList(DiscordEmbedBuilder builder, Dictionary<string, MessageTask> tasks, InteractionContext ctx,
        bool isPrivate,
        string successDescription = "")
    {
        _successDescription = successDescription;
        _builder = builder;
        _tasks = tasks;
        _context = ctx;
        Init(isPrivate).GetAwaiter().GetResult();
    }

    public void Log(string message)
    {
        _tasks.ElementAt(_index).Value.Log += message + "\n";
    }

    public void Log(string template, params object[] objs)
    {
        _tasks.ElementAt(_index).Value.Log += string.Format(template, objs) + "\n";
    }

    private async Task Init(bool isPrivate)
    {
        await _context.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .AsEphemeral(isPrivate));
        await UpdateContent();
    }

    public async Task FinishTask()
    {
        if (_finished) return;
        _tasks.ElementAt(_index).Value.State = MessageTaskState.Done;
        _index++;
        if (_index <= _tasks.Count - 1)
        {
            await UpdateContent();
        }
        else
        {
            _finished = true;
            await UpdateContent(true);
        }
    }

    public async Task FailTask(string reason)
    {
        _tasks.ElementAt(_index).Value.State = MessageTaskState.Failed;
        await UpdateContent();
        throw new MessageTaskFailedException(reason, _context.CommandName, _tasks.ElementAt(_index).Value);
    }

    public async Task UpdateContent(bool addDescription = false)
    {
        var str =
            $"{(addDescription && !string.IsNullOrEmpty(_successDescription) ? $"**{_successDescription}**\n" : "")}```\n";
        for (var i = 0; i < _tasks.Count; i++)
        {
            var task = _tasks.ElementAt(i).Value;
            str += $"[{_stateConverter[task.State]}] {task.Name}";
            if (!string.IsNullOrEmpty(task.Log))
                str += $"\n{task.Log}";
            else
                str += '\n';
        }

        str += "```";

        await _context.EditResponseAsync(new DiscordWebhookBuilder()
            .AddEmbed(_builder.WithDescription(str).Build()));
    }
}

public class MessageTaskListBuilder
{
    private readonly DiscordEmbedBuilder _builder = new();
    private readonly InteractionContext _context;
    private readonly Dictionary<string, MessageTask> _tasks = new();

    private bool _private;
    private string _successDescription = "";

    public MessageTaskListBuilder(InteractionContext context)
    {
        _context = context;
    }

    public MessageTaskListBuilder WithSuccessDescription(string description)
    {
        _successDescription = description;
        return this;
    }

    public MessageTaskListBuilder WithTitle(string title)
    {
        _builder.Title = title;
        return this;
    }

    public MessageTaskListBuilder WithColor(DiscordColor color)
    {
        _builder.Color = color;
        return this;
    }

    public MessageTaskListBuilder AddTask(string title)
    {
        _tasks[title] = new MessageTask(title);
        return this;
    }

    public MessageTaskListBuilder IsPrivate()
    {
        _private = true;
        return this;
    }

    public MessageTaskList Build()
    {
        return new MessageTaskList(_builder, _tasks, _context, _private, _successDescription);
    }
}