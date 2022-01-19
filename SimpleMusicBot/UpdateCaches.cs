using DSharpPlus.Lavalink;
using DSharpPlus.SlashCommands;

namespace SimpleMusicBot;

public class UpdateCaches : SlashCheckBaseAttribute
{
    public override Task<bool> ExecuteChecksAsync(InteractionContext ctx)
    {
        if (ctx.Guild != null)
            Update(ctx.Client.GetLavalink(), ctx.Guild.Id);

        return Task.FromResult(true);
    }

    public static void Update(LavalinkExtension lava, ulong guildId)
    {
        var conn = lava.ConnectedNodes.First().Value.GetGuildConnection(lava.Client.Guilds[guildId]);

        if ((conn?.IsConnected).GetValueOrDefault())
        {
            if (!MusicCommands.queue.ContainsKey(guildId))
                MusicCommands.queue.Add(guildId, new Queue<LavalinkTrack>());
            if (!MusicCommands.loop.ContainsKey(guildId))
                MusicCommands.loop.Add(guildId, false);
            if (!MusicCommands.afkTimers.ContainsKey(guildId))
                MusicCommands.afkTimers.Add(guildId, MusicCommands.afkTimeout);
        }
        else
        {
            if (MusicCommands.queue.ContainsKey(guildId))
                MusicCommands.queue.Remove(guildId);
            if (MusicCommands.loop.ContainsKey(guildId))
                MusicCommands.loop.Remove(guildId);
            if (MusicCommands.afkTimers.ContainsKey(guildId))
                MusicCommands.afkTimers.Remove(guildId);
        }
    }
}