using UnityEngine;

namespace BatteryRushArena.NetworkSpike
{

/// <summary>
/// Creates a single runtime-owned NetworkSpikeApp per play world.
/// </summary>
public static class NetworkSpikeBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Create()
    {
        if (Object.FindFirstObjectByType<NetworkSpikeApp>() is not null)
        {
            return;
        }

        var go = new GameObject("NetworkSpikeApp");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<NetworkSpikeApp>();
    }
}
}
