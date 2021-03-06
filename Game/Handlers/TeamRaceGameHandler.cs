using System.Threading.Channels;
using DSharpPlus.Entities;
using Nedordle.Database;
using Nedordle.Drawer;
using Nedordle.Helpers;
using Nedordle.Helpers.Game;
using Nedordle.Helpers.Types;

namespace Nedordle.Game.Handlers;

public class TeamRaceGameHandler: MultiplayerGameHandler
{
    public int UserLimit { get; init; }
    public int Length { get; init; }
    private string _answer = "";
    private ulong _winner;

    public override Task OnCreate(DiscordUser creator, Locale locale)
    {
        UpdateInfo();
        Update();
        return Task.CompletedTask;
    }

    private protected override void UpdateInfo()
    {
        Info["Channels"] = Channels.ToDictionary(x => x.Key, x => x.Value.Id);
        Info["ResponseMessages"] = ResponseMessages.ToDictionary(x => x.Key, x => x.Value.Id);
        Info["Playing"] = Playing;
        Info["Answer"] = _answer;
        Info["Winner"] = _winner;
    } 

    public override async Task OnJoined(DiscordChannel callerChannel, DiscordUser user, Locale locale)
    {
        if (Players.Count == UserLimit)
        {
            await callerChannel.SendMessageAsync(SimpleDiscordEmbed.Error(locale.GameMultiplayerFull));
            return;
        }

        if (Playing)
        {
            await callerChannel.SendMessageAsync(SimpleDiscordEmbed.Error(locale.AlreadyStarted));
            return;
        }

        Players[user.Id] = new Player(user, locale);
        var channel =  await MultiplayerHelper.GetPrivateChannel(user, callerChannel);
        await channel.SendMessageAsync(string.Format(locale.GameMultiplayerInfo, UserLimit));
        Channels[user.Id] = channel;
        UpdateInfo(); 
        Update();
        
        await ChannelBroadcaster.Broadcast(this, ChannelBroadcaster.BroadcastLevel.Success, "GameMultiplayerJoined", user.Username, Players.Count, UserLimit);
        if (Players.Count == UserLimit)
            await OnStart();
    }

    public override async Task OnLeft(DiscordChannel callerChannel, DiscordUser user, Locale locale)
    {
        if (Playing)
        {
            await callerChannel.SendMessageAsync(locale.CannotLeaveWhilePlaying);
            return;
        }

        Channels.Remove(user.Id);
        Players.Remove(user.Id);

        UpdateInfo();
        Update();

        await ChannelBroadcaster.Broadcast(this, ChannelBroadcaster.BroadcastLevel.Error, "GameMultiplayerLeft", user.Username, Players.Count, UserLimit);

        if (Players.Count == 0)
            await OnCleanup();
    }

    public override async Task OnStart()
    {
        Playing = true;
        
        _answer = DictionaryDatabaseHelper.GetWord(Language, Length);
        UpdateInfo();
        Update();
        await ChannelBroadcaster.Broadcast(this, ChannelBroadcaster.BroadcastLevel.Success, "GameStart");
    }

    public override async Task OnEnd()
    {
        Ended = true;
        
        var winner = Players.First(x => x.Key == _winner);
        await ChannelBroadcaster.Broadcast(this, ChannelBroadcaster.BroadcastLevel.Success, "GameMultiplayerWinner", winner.Value.User.Username);
        foreach (var (_, value) in Players)
            await Channels[value.User.Id].SendMessageAsync(BuildResult(value));

        await OnCleanup();
    }

    public override Task OnCleanup()
    {
        GameDatabaseHelper.RemoveGame(Id);
        return Task.CompletedTask;
    }

    public override async Task OnInput(DiscordChannel caller, DiscordUser user, string input)
    {
        if (Ended) return;

        if (caller.Id != Channels[user.Id].Id) return;
        
        if (input.Length != Length) return;

        if (!DictionaryDatabaseHelper.Exists(Language, input))
        {
            var error = await Channels[user.Id].SendMessageAsync(
                SimpleDiscordEmbed.Error(Players[user.Id].Locale.GameInvalidWord));
            await Task.Delay(5000);
            await error.DeleteAsync();
            return;
        }

        if (Players[user.Id].Guesses.Any(x => x.Input == input))
        {
            var error = await Channels[user.Id].SendMessageAsync(
                SimpleDiscordEmbed.Error(Players[user.Id].Locale.GameAlreadyUsed));
            await Task.Delay(5000);
            await error.DeleteAsync();
            return;
        }

        if (ResponseMessages.ContainsKey(user.Id) && ResponseMessages[user.Id] != null) await ResponseMessages[user.Id]!.DeleteAsync();
        Players[user.Id].AddGuess(input, _answer);

        var stream = WordleDrawer.Generate(Players[user.Id].GuessString, Players[user.Id].Theme);
        stream.Seek(0, SeekOrigin.Begin);
        ResponseMessages[user.Id] = await Channels[user.Id].SendMessageAsync(new DiscordMessageBuilder()
            .WithFile($"nedordle_teamrace_{Id}.png", stream));
        UpdateInfo();
        Update();
        
        if (input == _answer && _winner == 0)
        {
            _winner = user.Id;
            await Channels[user.Id].SendMessageAsync(SimpleDiscordEmbed.Colored(SimpleDiscordEmbed.PastelGreen,
                Players[user.Id].Locale.GameWin));
            await OnEnd();
        }
    }

    public override string BuildResult(Player player) => EmojiResultBuilder.BuildTeamRace(player, _winner, player.Locale.GameTypes[GameType].Name);
}