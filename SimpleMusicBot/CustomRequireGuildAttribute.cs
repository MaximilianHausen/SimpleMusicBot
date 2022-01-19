using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;

namespace SimpleMusicBot;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
public class CustomRequireGuildAttribute : SlashCheckBaseAttribute
{
    private readonly string _message;

    public CustomRequireGuildAttribute(string errormessage = "Dieser Bot kann nur in einem Server verwendet werden")
        => _message = errormessage;

    public override async Task<bool> ExecuteChecksAsync(InteractionContext ctx)
    {
        if (ctx.Guild != null) return true;

        DiscordEmbedBuilder embedBuilder = new() { Title = "Fehler", Color = MusicCommands.errorColor };
        embedBuilder.AddField("Details", _message);
        await ctx.CreateResponseAsync(embedBuilder.Build(), true);
        return false;
    }
}