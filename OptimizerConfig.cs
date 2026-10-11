using System.Collections.Generic;
using System.Linq;
using PavonisInteractive.TerraInvicta;
using UnityModManagerNet;

namespace ShipOptimizerConfig
{
    // One row of the Optimizer Config tab
    public class ResourceLimit
    {
        public FactionResource resource;

        // "No limit": Auto Design ignores this resource, exactly as the game does
        public bool noLimit = true;

        // Ship construction may use at most this much of the resource; 0 keeps parts that use it out of the design.
        // Negative means never set, which the UI shows as the current stockpile.
        public float limit = -1f;

        // A limit of 0 removes the resource: handled by filtering parts rather than by totals, because crew
        // water/volatiles are spent whatever parts are chosen
        public bool Excluded => !noLimit && limit <= 0f;
        public bool Limited => !noLimit && limit > 0f;
    }

    public static class OptimizerConfig
    {
        // Resources that ship construction spends (see TISpaceShipTemplate.spaceResourceConstructionCost)
        public static readonly FactionResource[] ShipResources =
        {
            FactionResource.Water,
            FactionResource.Volatiles,
            FactionResource.Metals,
            FactionResource.NobleMetals,
            FactionResource.Fissiles,
            FactionResource.Antimatter,
            FactionResource.Exotics,
        };

        static readonly Dictionary<FactionResource, ResourceLimit> limits =
            ShipResources.ToDictionary(r => r, r => new ResourceLimit { resource = r });

        public static IEnumerable<ResourceLimit> All => ShipResources.Select(r => limits[r]);

        public static ResourceLimit Get(FactionResource resource) => limits[resource];

        public static string Name(FactionResource resource) => TIUtilities.GetResourceString(resource);

        public static float Stockpile(FactionResource resource) =>
            GameControl.control?.activePlayer?.GetCurrentResourceAmount(resource) ?? 0f;

        // The value the slider and number box show
        public static float DisplayedLimit(ResourceLimit entry) => entry.limit >= 0f ? entry.limit : Stockpile(entry.resource);

        public static void ClearAll()
        {
            foreach (var entry in limits.Values)
            {
                entry.noLimit = true;
                entry.limit = -1f;
            }
        }

        public static void LoadFrom(Settings settings)
        {
            foreach (var saved in settings.limits)
            {
                if (!limits.TryGetValue(saved.resource, out var entry))
                    continue;
                entry.limit = saved.limit;
                entry.noLimit = saved.noLimit;
            }
        }

        public static void SaveTo(Settings settings)
        {
            settings.limits = All.Select(e => new ResourceLimit
            {
                resource = e.resource,
                limit = e.limit,
                noLimit = e.noLimit,
            }).ToList();
        }
    }

    public class Settings : UnityModManager.ModSettings
    {
        public List<ResourceLimit> limits = new List<ResourceLimit>();

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }
    }
}
