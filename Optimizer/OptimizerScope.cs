using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PavonisInteractive.TerraInvicta;

namespace ShipOptimizerConfig.Optimizer
{
    // While a constrained Auto Design runs, swaps the faction's cached allowed-part lists (which the game's
    // DesignShip and GetBest* helpers read through the allowed* getters) for filtered copies, and restores the
    // originals on Dispose. Swapping the backing fields rather than patching the getters means JIT inlining of
    // those one-line getters can't bypass the filter.
    public sealed class OptimizerScope : IDisposable
    {
        interface IPartList
        {
            void Apply(TIFactionState faction, Func<TIShipPartTemplate, bool> allows);
            void Restore(TIFactionState faction);
        }

        sealed class PartList<T> : IPartList where T : TIShipPartTemplate
        {
            readonly AccessTools.FieldRef<TIFactionState, List<T>> field;
            List<T> original;

            public PartList(string fieldName)
            {
                field = AccessTools.FieldRefAccess<TIFactionState, List<T>>(fieldName);
            }

            public void Apply(TIFactionState faction, Func<TIShipPartTemplate, bool> allows)
            {
                if (original == null)
                    original = field(faction);
                if (original != null)
                    field(faction) = original.Where(p => allows(p)).ToList();
            }

            public void Restore(TIFactionState faction)
            {
                if (original != null)
                    field(faction) = original;
                original = null;
            }
        }

        readonly TIFactionState faction;
        readonly HashSet<FactionResource> excluded;
        readonly HashSet<string> banned = new HashSet<string>();
        readonly IPartList[] lists =
        {
            new PartList<TIShipHullTemplate>("cachedAllowedShipHulls"),
            new PartList<TIRadiatorTemplate>("cachedAllowedRadiators"),
            new PartList<TIDriveTemplate>("cachedAllowedDrives"),
            new PartList<TIBatteryTemplate>("cachedAllowedBatteries"),
            new PartList<TIShipArmorTemplate>("cachedAllowedArmors"),
            new PartList<TIPowerPlantTemplate>("cachedAllowedPowerPlants"),
            new PartList<TIShipWeaponTemplate>("cachedAllowedNoseWeapons"),
            new PartList<TIShipWeaponTemplate>("cachedAllowedHullWeapons"),
            new PartList<TIHeatSinkTemplate>("cachedAllowedHeatSinks"),
            new PartList<TIUtilityModuleTemplate>("cachedAllowedUtilityModules"),
        };

        public OptimizerScope(TIFactionState faction, IEnumerable<FactionResource> excluded)
        {
            this.faction = faction;
            this.excluded = new HashSet<FactionResource>(excluded);
            Apply();
        }

        public bool IsBanned(TIShipPartTemplate part) => banned.Contains(part.dataName);

        public void Ban(TIShipPartTemplate part)
        {
            banned.Add(part.dataName);
            Apply();
        }

        public void Dispose()
        {
            foreach (var list in lists)
                list.Restore(faction);
        }

        void Apply()
        {
            foreach (var list in lists)
                list.Apply(faction, Allows);
        }

        bool Allows(TIShipPartTemplate part)
        {
            if (banned.Contains(part.dataName))
                return false;
            if (excluded.Count == 0)
                return true;
            if (excluded.Any(r => Amount(part.weightedBuildMaterials, r) > 0f))
                return false;
            // A drive's propellant is part of the ship's cost too
            return !(part is TIDriveTemplate drive) || !excluded.Any(r => Amount(drive.GetPerTankPropellantMaterials(faction), r) > 0f);
        }

        static float Amount(ResourceCostBuilder materials, FactionResource resource)
        {
            switch (resource)
            {
                case FactionResource.Water: return materials.water;
                case FactionResource.Volatiles: return materials.volatiles;
                case FactionResource.Metals: return materials.metals;
                case FactionResource.NobleMetals: return materials.nobleMetals;
                case FactionResource.Fissiles: return materials.fissiles;
                case FactionResource.Antimatter: return materials.antimatter;
                case FactionResource.Exotics: return materials.exotics;
                default: return 0f;
            }
        }
    }
}
