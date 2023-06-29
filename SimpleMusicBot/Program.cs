using DSharpPlus;
using DSharpPlus.Lavalink;
using DSharpPlus.Net;
using DSharpPlus.SlashCommands;
using Microsoft.Extensions.Logging;

namespace SimpleMusicBot;

public class Program
{
    public static DiscordClient Client { get; private set; }

    public static async Task Main()
    {
        var token = Environment.GetEnvironmentVariable("MUSIC_BOT_TOKEN") ??
                    throw new InvalidOperationException("Environment variable \"MUSIC_BOT_TOKEN\" not found");
        var lavalinkPassword = Environment.GetEnvironmentVariable("LAVALINK_PASSWORD") ??
                               throw new InvalidOperationException("Environment variable \"LAVALINK_PASSWORD\" not found");
        var lavalinkHostname = Environment.GetEnvironmentVariable("LAVALINK_HOSTNAME") ?? "127.0.0.1";
        var lavalinkPort = Environment.GetEnvironmentVariable("LAVALINK_PORT") ?? "2333";

        Client = new DiscordClient(new DiscordConfiguration
        {
            Token = token,
            TokenType = TokenType.Bot,
            Intents = DiscordIntents.AllUnprivileged,
            LogTimestampFormat = "d/M/yyyy hh:mm:ss",
            MinimumLogLevel = LogLevel.Information
        });

        var endpoint = new ConnectionEndpoint
        {
            Hostname = lavalinkHostname,
            Port = int.Parse(lavalinkPort)
        };

        var lavalinkConfig = new LavalinkConfiguration
        {
            Password = lavalinkPassword,
            RestEndpoint = endpoint,
            SocketEndpoint = endpoint
        };

        var slashExt = Client.UseSlashCommands();
        slashExt.RegisterCommands<MusicCommands>();

        var lavalink = Client.UseLavalink();

        Client.GuildDownloadCompleted += async (client, _) => client.Logger.Log(LogLevel.Information, new EventId(0, "Startup"), "Active Guilds: " + client.Guilds.Select(g => g.Value.Name).Aggregate((a, b) => $"{a}, {b}"), null, (s, e) => s);

        await Client.ConnectAsync();
        await lavalink.ConnectAsync(lavalinkConfig);

        MusicCommands.Initialize();

        await Task.Delay(Timeout.Infinite);
    }
}