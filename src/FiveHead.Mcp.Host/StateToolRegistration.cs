using System.Text.Json;
using FiveHead.State.Client;
using Microsoft.Extensions.DependencyInjection;

namespace FiveHead.Mcp.Host;

internal static class StateToolRegistration
{
    internal static void Add(IServiceCollection services, IMcpServerBuilder mcp, StateClientSettings settings, JsonSerializerOptions serializerOptions)
    {
        services.AddSingleton(settings);
        services.AddSingleton(_ => StateApiClient.CreateHttpClient(settings.BaseUrl, settings.Token));
        services.AddSingleton(sp => new StateApiClient(sp.GetRequiredService<HttpClient>(), settings.Agent));
        services.AddSingleton<StateMcpTools>();
        mcp.WithTools<StateMcpTools>(serializerOptions);
    }
}
