using System.Timers;
using DSharpPlus.Entities;
using DSharpPlus.Lavalink;
using DSharpPlus.Lavalink.EventArgs;
using DSharpPlus.SlashCommands;
using MoreLinq;
using Timer = System.Timers.Timer;

namespace SimpleMusicBot;

public class MusicCommands : ApplicationCommandModule
{
    public const int afkTimeout = 10; // In Minutes
    public static readonly DiscordColor responseColor = new(0x5865F2);
    public static readonly DiscordColor errorColor = new(0xED4245);

    private static readonly Timer afkCheckTimer = new(60000) { AutoReset = true };

    private static string currentPlayingRequestor = "Wenn du das hier siehst, melde dich bei Maxi#2608";
    public static readonly Dictionary<ulong, Queue<(LavalinkTrack, string)>> queue = new();
    public static readonly Dictionary<ulong, bool> loop = new();
    public static readonly Dictionary<ulong, int> afkTimers = new();

    public static void Initialize()
    {
        afkCheckTimer.Elapsed += CheckInactivity;
        afkCheckTimer.Start();
    }

    private static void CheckInactivity(object source, ElapsedEventArgs args)
    {
        var client = Program.Client;
        var lava = client.GetLavalink();

        client.Guilds.ForEach(g => UpdateCaches.Update(lava, g.Key));

        foreach (var guildId in queue.Keys)
        {
            var conn = lava.GetGuildConnection(client.Guilds[guildId]);

            if (conn.CurrentState.CurrentTrack == null)
                afkTimers[guildId]--;
            else
                afkTimers[guildId] = afkTimeout;

            if (afkTimers[guildId] > 0) continue;
            conn.DisconnectAsync().Wait();
            UpdateCaches.Update(lava, guildId);
        }
    }

    private static Task PlaybackFinished(LavalinkGuildConnection sender, TrackFinishEventArgs e)
    {
        e.Handled = true;

        if (e.Reason is TrackEndReason.Finished or TrackEndReason.LoadFailed)
        {
            if (loop[sender.Guild.Id])
                e.Player.PlayAsync(e.Track);
            else if (queue[sender.Guild.Id].Any())
            {
                var (lavalinkTrack, requestor) = queue[sender.Guild.Id].Dequeue();
                currentPlayingRequestor = requestor;
                e.Player.PlayAsync(lavalinkTrack);
            }
        }

        return Task.CompletedTask;
    }

    [CustomRequireGuild]
    [SlashCommand("help", "Tipps & Tricks")]
    public async Task Help(InteractionContext ctx)
    {
        var embedBuilder = new DiscordEmbedBuilder { Title = "Help", Color = responseColor };

        embedBuilder.AddField("`/join`", "Verbindet sich mit dem Sprachkanal, in dem du gerade bist");
        embedBuilder.AddField("`/leave`", "Verlässt den Sprachkanal, in dem der Bot gerade ist und leert die Warteliste");
        embedBuilder.AddField("`/play`",
            "Spielt ein Lied im momentanen Channel ab (vorher mit /join den Bot herholen). Als Quelle geht ein Suchbegriff für Youtube oder ein direkter Link zu YouTube, SoundCloud, Bandcamp, Vimeo, Twitch streams oder einer Videodatei. Standartmäßig wird das Lied hinten in die Warteschlange eingereiht. Mit playNow true (TAB drücken nach Commandeingabe) wird es vorne eingereiht und sofort abgespielt (überspringt das momentane Lied)");
        embedBuilder.AddField("`/nowplaying`", "Zeigt Informationen über das momentane Lied an (Titel, Autor, Quelle, Abspielposition)");
        embedBuilder.AddField("`/queue`", "Zeigt die momentane Warteschlange an (ohne dem jetztigen Lied)");
        embedBuilder.AddField("`/skip`", "Überspringt das aktuelle Lied (kein Voting)");
        embedBuilder.AddField("`/skipto`", "Springt zu einer bestimmten Stelle im Video, angegeben in Minuten und Sekunden");
        embedBuilder.AddField("`/pause`", "Pausiert die Wiedergabe");
        embedBuilder.AddField("`/resume`", "Setzt die Wiedergabe fort");
        embedBuilder.AddField("`/stop`", "Bricht die Wiedergabe ab und leert die Warteschlange");
        embedBuilder.AddField("`/loop`", "Wiederholt das aktuelle Lied");

        embedBuilder.AddField("`Weitere Infos`",
            "Die Warteschlange wird auch gelöscht, wenn der Bot manuell aus einem Sprachkanal entfernt wird oder den Sprachkanal wechselt\nAltersbeschränkte Videos können nicht abgespielt werden und werden automatisch übersprungen");
        embedBuilder.AddField("`Schnellstart`",
            "1. Geh in den Sprachkanal, in dem du etwas abspielen willst\n2. Rufe den Bot mit /join\n3. Spiele etwas mit /play ab");
        await ctx.CreateResponseAsync(embedBuilder.Build(), true);
    }

    [UpdateCaches]
    [CustomRequireGuild]
    [SlashCommand("join", "Verbindet sich mit einem Channel")]
    public async Task Join(InteractionContext ctx)
    {
        var embedBuilder = new DiscordEmbedBuilder();

        var channel = ctx.Member.VoiceState?.Channel;

        if (channel == null)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Du bist nicht in einem Sprachkanal");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        var lava = ctx.Client.GetLavalink();
        var node = lava.ConnectedNodes.Values.First();

        if (node.GetGuildConnection(ctx.Guild) != null)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Der Bot wird bereits in einem anderen Sprachkanal. Verwende /leave vorher");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        embedBuilder.Title = "Joined";
        embedBuilder.Color = responseColor;
        embedBuilder.AddField("Details", $"Joined {channel.Mention}");
        await ctx.CreateResponseAsync(embedBuilder.Build());

        (await node.ConnectAsync(channel)).PlaybackFinished += PlaybackFinished;
    }

    [UpdateCaches]
    [CustomRequireGuild]
    [SlashCommand("leave", "Verlässt einen Channel")]
    public async Task Leave(InteractionContext ctx)
    {
        var embedBuilder = new DiscordEmbedBuilder();
        var lava = ctx.Client.GetLavalink();
        var node = lava.ConnectedNodes.Values.First();
        var conn = node.GetGuildConnection(ctx.Guild);

        if (conn == null)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Der Bot ist in keinem Sprachkanal");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        if (ctx.Member.VoiceState?.Channel == null ||
            ctx.Member.VoiceState?.Channel != lava.ConnectedNodes.First().Value.GetGuildConnection(ctx.Guild)?.Channel)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Du bist nicht in einem Sprachkanal mit dem Bot");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        embedBuilder.Title = "Left";
        embedBuilder.Color = responseColor;
        embedBuilder.AddField("Details", $"{conn.Channel.Mention} verlassen");
        await ctx.CreateResponseAsync(embedBuilder.Build());

        await conn.DisconnectAsync();
    }

    [UpdateCaches]
    [CustomRequireGuild]
    [SlashCommand("play", "Spielt ein Lied im momentanen Channel ab")]
    public async Task Play(InteractionContext ctx,
        [Option("source", "Youtube-Suche oder Direktlink zu YouTube, SoundCloud, Bandcamp, Vimeo, Twitch oder einer Videodatei")]
        string source,
        [Option("playNow", "Ob die Warteschlange übersprungen werden soll")]
        bool playNow = false)
    {
        var embedBuilder = new DiscordEmbedBuilder();
        var lava = ctx.Client.GetLavalink();
        var node = lava.ConnectedNodes.Values.First();
        var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);

        var isLink = Uri.IsWellFormedUriString(source, UriKind.Absolute);

        if (conn == null)
        {
            var channel = ctx.Member.VoiceState?.Channel;

            if (channel == null)
            {
                embedBuilder.Title = "Fehler";
                embedBuilder.Color = errorColor;
                embedBuilder.AddField("Details", "Du bist nicht in einem Sprachkanal");
                await ctx.CreateResponseAsync(embedBuilder.Build(), true);
                return;
            }

            conn = await node.ConnectAsync(channel);

            conn.PlaybackFinished += PlaybackFinished;

            UpdateCaches.Update(lava, ctx.Guild.Id);
        }

        if (ctx.Member.VoiceState?.Channel == null ||
            ctx.Member.VoiceState?.Channel != lava.ConnectedNodes.First().Value.GetGuildConnection(ctx.Guild)?.Channel)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Der Bot wird bereits in einem anderen Sprachkanal verwendet oder konnte deinem Sprachkanal nicht beitreten");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        await ctx.DeferAsync();

        var loadResult = isLink
            ? await node.Rest.GetTracksAsync(new Uri(source))
            : await node.Rest.GetTracksAsync(source);

        if (loadResult.LoadResultType == LavalinkLoadResultType.LoadFailed)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", $"{(isLink ? "Abrufen" : "Suche")} fehlgeschlagen für {source}");
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbeds(new[] { embedBuilder.Build() }));
            return;
        }

        if (loadResult.LoadResultType == LavalinkLoadResultType.NoMatches)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", isLink ? $"Kein Video gefunden in {source}" : $"Keine Ergebnise gefunden für `{source}`");
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbeds(new[] { embedBuilder.Build() }));
            return;
        }

        var track = loadResult.Tracks.First();
        embedBuilder.Title = "Track found";
        embedBuilder.Color = responseColor;
        
        string requesterName = ctx.Member.Nickname ?? ctx.Member.Username;

        if (playNow)
        {
            queue[ctx.Guild.Id] = new Queue<(LavalinkTrack, string)>(Enumerable.Prepend(queue[ctx.Guild.Id], (track, requesterName)));
            embedBuilder.AddField("Details", $"`{track.Title}` wird jetzt in {conn.Channel.Mention} abgespielt");
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbeds(new[] { embedBuilder.Build() }));
            loop[ctx.Guild.Id] = false;
        }
        else
        {
            if (queue[ctx.Guild.Id].Count >= 50)
            {
                embedBuilder.Title = "Fehler";
                embedBuilder.Color = errorColor;
                embedBuilder.AddField("Details", $"`{track.Title}` konnte nicht zur Warteschlange hinzugefügt werden, weil sie schon 50 Tracks enthält");
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbeds(new[] { embedBuilder.Build() }));
                return;
            }

            queue[ctx.Guild.Id].Enqueue((track, requesterName));
            embedBuilder.AddField("Details", $"`{track.Title}` zur Wartschlange für {conn.Channel.Mention} hinzugefügt");
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbeds(new[] { embedBuilder.Build() }));
        }

        if (conn.CurrentState.CurrentTrack == null || playNow)
        {
            var (lavalinkTrack, requestor) = queue[ctx.Guild.Id].Dequeue();
            currentPlayingRequestor = requestor;
            await conn.PlayAsync(lavalinkTrack);
        }
    }

    [UpdateCaches]
    [CustomRequireGuild]
    [SlashCommand("nowplaying", "Zeigt informationen über den aktuellen Track")]
    public Task NowPlaying(InteractionContext ctx)
    {
        var embedBuilder = new DiscordEmbedBuilder();

        var conn = ctx.Client.GetLavalink().ConnectedNodes.First().Value.GetGuildConnection(ctx.Guild);

        if (conn == null)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Der Bot ist in keinem Sprachkanal");
            return ctx.CreateResponseAsync(embedBuilder.Build(), true);
        }

        if (conn.CurrentState.CurrentTrack == null)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Momentan wird nichts abgespielt");
            return ctx.CreateResponseAsync(embedBuilder.Build(), true);
        }

        var track = conn.CurrentState.CurrentTrack;

        embedBuilder.Title = "Now playing";
        embedBuilder.Color = responseColor;
        embedBuilder.AddField("Details",
            $"`Titel`: {track.Title}\n`Autor`: {track.Author}\n`Quelle`: {track.Uri}\n`Angefordert von`: {currentPlayingRequestor}\n`Position`: {conn.CurrentState.PlaybackPosition:hh\\:mm\\:ss}/{track.Length:hh\\:mm\\:ss}\n`Loop`: {(loop[ctx.Guild.Id] ? "aktiviert" : "deaktiviert")}");

        return ctx.CreateResponseAsync(embedBuilder.Build(), true);
    }

    [UpdateCaches]
    [CustomRequireGuild]
    [SlashCommand("queue", "Zeigt die aktuelle Wartschlange")]
    public Task Queue(InteractionContext ctx)
    {
        var embedBuilder = new DiscordEmbedBuilder();
        var lava = ctx.Client.GetLavalink();
        var conn = lava.ConnectedNodes.First().Value.GetGuildConnection(ctx.Guild);

        var trackList = queue.ContainsKey(ctx.Guild.Id) ? queue[ctx.Guild.Id].ToArray() : Array.Empty<(LavalinkTrack, string)>();

        if (!trackList.Any())
        {
            if (loop[ctx.Guild.Id])
            {
                embedBuilder.Title = "Wartschlange";
                embedBuilder.Color = responseColor;
                embedBuilder.AddField("Details", "**Loop ist aktiviert**");
                return ctx.CreateResponseAsync(embedBuilder.Build(), true);
            }

            embedBuilder.Title = "Wartschlange";
            embedBuilder.Color = responseColor;
            embedBuilder.AddField("Details", "Die Warteschlange ist leer");
            return ctx.CreateResponseAsync(embedBuilder.Build(), true);
        }

        var trackListString = loop[ctx.Guild.Id] ? "**Loop ist aktiviert**\n\n" : "";
        for (var i = 0; i < trackList.Length; i++)
            trackListString += $"`{i + 1}` [{trackList[i].Item1.Title}]({trackList[i].Item1.Uri})\n";
        trackListString = trackListString[..^1];

        embedBuilder.Title = "Warteschlange";
        embedBuilder.Color = responseColor;
        embedBuilder.AddField("Details", trackListString);

        return ctx.CreateResponseAsync(embedBuilder.Build(), true);
    }

    [UpdateCaches]
    [CustomRequireGuild]
    [SlashCommand("skip", "Überspringt den aktive Wiedergabe")]
    public async Task Skip(InteractionContext ctx)
    {
        var embedBuilder = new DiscordEmbedBuilder();
        var lava = ctx.Client.GetLavalink();
        var node = lava.ConnectedNodes.Values.First();
        var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);

        if (conn == null)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Der Bot ist in keinem Sprachkanal");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        if (conn.CurrentState.CurrentTrack == null)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Momentan wird nichts abgespielt");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        if (ctx.Member.VoiceState?.Channel == null ||
            ctx.Member.VoiceState?.Channel != lava.ConnectedNodes.First().Value.GetGuildConnection(ctx.Guild)?.Channel)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Du bist nicht in einem Sprachkanal mit dem Bot");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        embedBuilder.Title = "Skipped";
        embedBuilder.Color = responseColor;
        embedBuilder.AddField("Details",
            $"`{conn.CurrentState.CurrentTrack.Title}` in Kanal {conn.Channel.Mention} übersprungen" +
            (queue[ctx.Guild.Id].Any() ? $".\n`{queue[ctx.Guild.Id].Peek().Item1.Title}` wird jetzt abgespielt" : ""));
        await ctx.CreateResponseAsync(embedBuilder.Build());

        loop[ctx.Guild.Id] = false;

        if (queue[ctx.Guild.Id].Any())
        {
            var (lavalinkTrack, requestor) = queue[ctx.Guild.Id].Dequeue();
            currentPlayingRequestor = requestor;
            await conn.PlayAsync(lavalinkTrack);
        }
        else
            await conn.StopAsync();
    }

    [UpdateCaches]
    [CustomRequireGuild]
    [SlashCommand("skipto", "Springt zu einer bestimmten Stelle im Video")]
    public async Task SkipTo(InteractionContext ctx, [Option("minutes", "Zeitpunkt in Minuten vom Start")] [Minimum(0)] long minutes,
        [Option("seconds", "Zeitpunkt in Sekunden vom Start")] [Minimum(0)]
        long seconds)
    {
        var embedBuilder = new DiscordEmbedBuilder();
        var lava = ctx.Client.GetLavalink();
        var node = lava.ConnectedNodes.Values.First();
        var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);

        var pos = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);

        if (conn == null)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Der Bot ist in keinem Sprachkanal");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        if (conn.CurrentState.CurrentTrack == null)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Momentan wird nichts abgespielt");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        if (ctx.Member.VoiceState?.Channel == null ||
            ctx.Member.VoiceState?.Channel != lava.ConnectedNodes.First().Value.GetGuildConnection(ctx.Guild)?.Channel)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Du bist nicht in einem Sprachkanal mit dem Bot");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        if (pos >= conn.CurrentState.CurrentTrack.Length)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", $"Der Track ist nur {conn.CurrentState.CurrentTrack.Length.TotalSeconds} Sekunden lang");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        embedBuilder.Title = "Set playback position";
        embedBuilder.Color = responseColor;
        embedBuilder.AddField("Details",
            $"Zu `{pos:hh\\:mm\\:ss}` bei Track`{conn.CurrentState.CurrentTrack.Title}` in Kanal {conn.Channel.Mention} gesprungen");
        await ctx.CreateResponseAsync(embedBuilder.Build());

        await conn.SeekAsync(pos);
    }

    [UpdateCaches]
    [CustomRequireGuild]
    [SlashCommand("pause", "Pausiert die aktive Wiedergabe")]
    public async Task Pause(InteractionContext ctx)
    {
        var embedBuilder = new DiscordEmbedBuilder();
        var lava = ctx.Client.GetLavalink();
        var conn = lava.ConnectedNodes.Values.First().GetGuildConnection(ctx.Member.VoiceState.Guild);

        if (conn == null)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Der Bot ist in keinem Sprachkanal");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        if (conn.CurrentState.CurrentTrack == null)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Momentan wird nichts abgespielt");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        if (ctx.Member.VoiceState?.Channel == null ||
            ctx.Member.VoiceState?.Channel != lava.ConnectedNodes.First().Value.GetGuildConnection(ctx.Guild)?.Channel)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Du bist nicht in einem Sprachkanal mit dem Bot");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        embedBuilder.Title = "Paused";
        embedBuilder.Color = responseColor;
        embedBuilder.AddField("Details", $"`{conn.CurrentState.CurrentTrack.Title}` in Kanal {conn.Channel.Mention} pausiert");

        await conn.PauseAsync();
    }

    [UpdateCaches]
    [CustomRequireGuild]
    [SlashCommand("resume", "Setzt die aktive Wiedergabe fort")]
    public async Task Resume(InteractionContext ctx)
    {
        var embedBuilder = new DiscordEmbedBuilder();
        var lava = ctx.Client.GetLavalink();
        var node = lava.ConnectedNodes.Values.First();
        var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);

        if (conn == null)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Der Bot ist in keinem Sprachkanal");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        if (conn.CurrentState.CurrentTrack == null)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Momentan wird nichts abgespielt");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        if (ctx.Member.VoiceState?.Channel == null ||
            ctx.Member.VoiceState?.Channel != lava.ConnectedNodes.First().Value.GetGuildConnection(ctx.Guild)?.Channel)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Du bist nicht in einem Sprachkanal mit dem Bot");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        embedBuilder.Title = "Resumed";
        embedBuilder.Color = responseColor;
        embedBuilder.AddField("Details", $"`{conn.CurrentState.CurrentTrack.Title}` in Kanal {conn.Channel.Mention} fortgesetzt");

        await conn.ResumeAsync();
    }

    [UpdateCaches]
    [CustomRequireGuild]
    [SlashCommand("stop", "Stoppt die aktive Wiedergabe und leert die Warteschlange")]
    public async Task Stop(InteractionContext ctx)
    {
        var embedBuilder = new DiscordEmbedBuilder();
        var lava = ctx.Client.GetLavalink();
        var node = lava.ConnectedNodes.Values.First();
        var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);

        if (conn == null)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Der Bot ist in keinem Sprachkanal");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        if (conn.CurrentState.CurrentTrack == null)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Momentan wird nichts abgespielt");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        if (ctx.Member.VoiceState?.Channel == null ||
            ctx.Member.VoiceState?.Channel != lava.ConnectedNodes.First().Value.GetGuildConnection(ctx.Guild)?.Channel)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Du bist nicht in einem Sprachkanal mit dem Bot");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        embedBuilder.Title = "Stopped";
        embedBuilder.Color = responseColor;
        embedBuilder.AddField("Details", $"Wiedergabe in Kanal {conn.Channel.Mention} gestoppt und Warteliste geleert");
        await ctx.CreateResponseAsync(embedBuilder.Build());

        queue[ctx.Guild.Id].Clear();
        loop[ctx.Guild.Id] = false;
        await conn.StopAsync();
    }

    [UpdateCaches]
    [CustomRequireGuild]
    [SlashCommand("loop", "Wiederholt das aktuelle Lied bis loop wieder deaktiviert oder es übersprungen wird")]
    public async Task Loop(InteractionContext ctx)
    {
        var embedBuilder = new DiscordEmbedBuilder();
        var lava = ctx.Client.GetLavalink();
        var node = lava.ConnectedNodes.Values.First();
        var conn = node.GetGuildConnection(ctx.Member.VoiceState.Guild);

        if (conn == null)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Der Bot ist in keinem Sprachkanal");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        if (conn.CurrentState.CurrentTrack == null)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Momentan wird nichts abgespielt");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        if (ctx.Member.VoiceState?.Channel == null ||
            ctx.Member.VoiceState?.Channel != lava.ConnectedNodes.First().Value.GetGuildConnection(ctx.Guild)?.Channel)
        {
            embedBuilder.Title = "Fehler";
            embedBuilder.Color = errorColor;
            embedBuilder.AddField("Details", "Du bist nicht in einem Sprachkanal mit dem Bot");
            await ctx.CreateResponseAsync(embedBuilder.Build(), true);
            return;
        }

        embedBuilder.Title = "Loop";
        embedBuilder.Color = responseColor;
        embedBuilder.AddField("Details",
            $"Wiederholung für `{conn.CurrentState.CurrentTrack.Title}` in {conn.Channel.Mention} {(!loop[ctx.Guild.Id] ? "aktiviert" : "deaktiviert")}");
        await ctx.CreateResponseAsync(embedBuilder.Build());

        loop[ctx.Guild.Id] = !loop[ctx.Guild.Id];
    }
}