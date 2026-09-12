using System.Net;
using System.Net.Sockets;
using Xunit;

namespace PcDs4Server.Tests;

public sealed class TcpClientConfigurationTests
{
    [Fact]
    public async Task AcceptedClientDisablesNagleBeforeMessageHandling()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var connectingClient = new TcpClient();
        Task connectTask = connectingClient.ConnectAsync(IPAddress.Loopback, port);
        using TcpClient acceptedClient = await listener.AcceptTcpClientAsync();
        await connectTask;

        Assert.False(acceptedClient.NoDelay);

        Ds4Service.ConfigureClient(acceptedClient);

        Assert.True(acceptedClient.NoDelay);
    }
}
