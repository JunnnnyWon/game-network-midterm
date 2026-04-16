using BatteryRushArena.NetworkSpikeServer;

var config = SpikeServerConfig.CreateDefault();
var roomRegistry = new RoomRegistry();
var host = new SpikeServerHost(config, roomRegistry);
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cts.Cancel();
};
await host.RunAsync(cts.Token);
