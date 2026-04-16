using UnityEngine;

namespace BatteryRushArena.NetworkSpike
{

/// <summary>
/// Creates a lightweight runtime bootstrap object for the network-session spike scene.
/// </summary>
public static class NetworkSpikeBootstrap
{
    /// <summary>
    /// Creates the spike app before the first scene loads so the project is runnable without manual wiring.
    /// </summary>
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
