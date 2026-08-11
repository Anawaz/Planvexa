namespace Planvexa.Modules.Chat;

using Microsoft.Extensions.DependencyInjection;
using Planvexa.Modules.Chat.Application.Services;

/// <summary>
/// Composition marker + DI registration for the Chat module. Store implementations and entity
/// configurations are supplied by the Infrastructure project / discovered by scanning this assembly.
/// Realtime delivery uses the shared <see cref="Planvexa.BuildingBlocks.Abstractions.IRealtimeNotifier"/>
/// (SignalR in the API host).
/// </summary>
public static class ChatModule
{
    public const string Schema = "chat";

    public static IServiceCollection AddChatModule(this IServiceCollection services)
    {
        services.AddScoped<ChatServiceContext>();
        services.AddScoped<ChatChannelService>();
        services.AddScoped<ChatMessageService>();
        services.AddScoped<ChatAttachmentService>();
        services.AddScoped<Planvexa.SharedContracts.Search.ISearchProvider, ChatSearchProvider>();
        return services;
    }
}
