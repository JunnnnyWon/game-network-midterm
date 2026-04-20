using BatteryRushArena.NetworkSpikeServer;

var config = SpikeServerConfig.CreateDefault();
var roomRegistry = new RoomRegistry();
var persistenceService = new MySqlPersistenceService(MySqlPersistenceService.BuildConnectionStringFromEnvironment());
var host = new SpikeServerHost(config, roomRegistry, persistenceService);
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cts.Cancel();
};
await host.RunAsync(cts.Token);
