using System;
using System.Collections.Generic;
using NHSE.Core;

namespace NHSE.Villagers;

/// <summary>
/// Browser stand-in for NHSE.Villagers' embedded villager templates, with the same API.
/// The app downloads the two template files of a villager and registers them with
/// <see cref="Add"/> before it calls <see cref="VillagerSwap.GetReplacementVillager"/>.
/// </summary>
public static class VillagerResources
{
    private static readonly Dictionary<string, VillagerData> Loaded = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers the template of a villager (files <c>{name}V.bin</c> and <c>{name}H.bin</c> of NHSE.Villagers).
    /// </summary>
    public static void Add(string villagerName, byte[] villager, byte[] house)
    {
        if (villager.Length != Villager2.SIZE)
            throw new ArgumentException($"Villager data for {villagerName} has {villager.Length} bytes, expected {Villager2.SIZE}", nameof(villager));
        if (house.Length != VillagerHouse2.SIZE)
            throw new ArgumentException($"House data for {villagerName} has {house.Length} bytes, expected {VillagerHouse2.SIZE}", nameof(house));
        Loaded[villagerName] = new VillagerData(villager, house);
    }

    /// <summary>
    /// Checks if the template of the villager has been registered.
    /// </summary>
    /// <param name="villagerName">Internal villager name.</param>
    public static bool IsVillagerDataKnown(string villagerName) => Loaded.ContainsKey(villagerName);

    /// <summary>
    /// Gets a copy of the raw Villager data and house data for the <see cref="villagerName"/>.
    /// </summary>
    /// <param name="villagerName">Internal villager name.</param>
    public static VillagerData GetVillager(string villagerName)
    {
        if (!Loaded.TryGetValue(villagerName, out var data))
            throw new ArgumentException($"Villager data not loaded for {villagerName}", nameof(villagerName));
        // the swap writes into the buffers, so every call gets fresh copies
        return new VillagerData(data.Villager.ToArray(), data.House.ToArray());
    }
}
