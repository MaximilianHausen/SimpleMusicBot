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
                               throw new InvalidOperationException("Environment variable \"MUSIC_BOT_TOKEN\" not found");

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
            Hostname = "127.0.0.1",
            Port = 2333
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

        await Client.ConnectAsync();
        await lavalink.ConnectAsync(lavalinkConfig);

        MusicCommands.Initialize();

        await Task.Delay(Timeout.Infinite);
    }
}