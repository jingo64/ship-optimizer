using System;
using System.Collections.Generic;
using System.Linq;
using PavonisInteractive.TerraInvicta;
using UnityEngine;

namespace ShipOptimizerConfig.Optimizer
{
    // Runs the game's own auto-designer under the Optimizer Config. "Remove" filters parts up front; resource limits are
    // met greedily: design, find the worst over-limit resource, ban the part spending most of it, design again.
    public static class ConstrainedDesigner
    {
        const int MaxPasses = 40;

        // Order in which zero limits are given up when nothing can be built: crew/propellant staples first,
        // exotics (the usual reason to remove anything) last
        static readonly FactionResource[] UntickOrder =
        {
            FactionResource.Water,
            FactionResource.Volatiles,
            FactionResource.Metals,
            FactionResource.Fissiles,
            FactionResource.NobleMetals,
            FactionResource.Antimatter,
            FactionResource.Exotics,
        };

        public class Result
        {
            public TISpaceShipTemplate design;
            public string message;
        }

        class Attempt
        {
            public TISpaceShipTemplate design;
            public TIResourcesCost cost;
            public float overrun;
        }

        // True when the config would not change anything, so the vanilla Auto Design should run untouched
        public static bool IsDefault => OptimizerConfig.All.All(e => e.noLimit);

        public static Result Design(TIFactionState faction, ShipRole role, TIShipHullTemplate hull)
        {
            var excluded = OptimizerConfig.All.Where(e => e.Excluded).Select(e => e.resource).ToList();
            var attempts = new List<Attempt>();
            TIFactionState.ShipDesignerOutcome firstOutcome;

            using (var scope = new OptimizerScope(faction, excluded))
            {
                firstOutcome = TryDesign(faction, role, hull, out var design);
                for (int pass = 0; design != null && pass < MaxPasses; pass++)
                {
                    var attempt = Evaluate(design, excluded);
                    attempts.Add(attempt);
                    if (attempt.overrun <= 0f)
                        return new Result { design = design };
                    var worst = OverLimitResources(attempt.cost, excluded).OrderByDescending(x => x.ratio).First().resource;
                    var culprit = PartCosts(faction, design, worst)
                        .Where(x => x.amount > 0f && !scope.IsBanned(x.part))
                        .OrderByDescending(x => x.amount)
                        .Select(x => x.part)
                        .FirstOrDefault();
                    if (culprit == null)
                        break;
                    scope.Ban(culprit);
                    Main.Log($"Optimizer: {OptimizerConfig.Name(worst)} over limit, excluding {culprit.dataName}");
                    TryDesign(faction, role, hull, out design);
                }
            }

            if (attempts.Count == 0)
                return Unbuildable(faction, role, hull, excluded, firstOutcome);
            var best = attempts.OrderBy(a => a.overrun).First();
            return new Result
            {
                message = "No design for this hull and role fits within these limits. " + RaiseLimitsTo(best.cost, excluded),
            };
        }

        // Nothing could be designed even before limits were checked: give up zero limits until something can be
        static Result Unbuildable(TIFactionState faction, ShipRole role, TIShipHullTemplate hull, List<FactionResource> excluded,
            TIFactionState.ShipDesignerOutcome outcome)
        {
            var remaining = new List<FactionResource>(excluded);
            var unticked = new List<FactionResource>();
            foreach (var resource in UntickOrder.Where(remaining.Contains))
            {
                remaining.Remove(resource);
                unticked.Add(resource);
                TISpaceShipTemplate design;
                using (new OptimizerScope(faction, remaining))
                    TryDesign(faction, role, hull, out design);
                if (design == null)
                    continue;
                var cost = design.spaceResourceConstructionCost(true, null);
                foreach (var r in unticked)
                    OptimizerConfig.Get(r).limit = RoundUp(cost.GetSingleCostValue(r));
                var needed = string.Join(", ", unticked.Select(r => $"{OptimizerConfig.Name(r)} to {OptimizerConfig.Get(r).limit:#,0.##}"));
                var raised = RaiseLimitsTo(cost, remaining);
                return new Result
                {
                    message = $"This hull and role can't be built without {string.Join(", ", unticked.Select(OptimizerConfig.Name))}. Raised {needed}. {raised}".Trim(),
                };
            }
            return new Result { message = $"Auto Design can't build this hull and role ({outcome})." };
        }

        static TIFactionState.ShipDesignerOutcome TryDesign(TIFactionState faction, ShipRole role, TIShipHullTemplate hull, out TISpaceShipTemplate design)
        {
            design = null;
            try
            {
                var outcome = faction.DesignShip(playerAutodesign: true, role, out var result, faction.DesiredStrategicRange_AU(),
                    AllowResource(faction, FactionResource.Exotics, faction.UnlockedExotics, faction.GetCurrentResourceAmount(FactionResource.Exotics)),
                    AllowResource(faction, FactionResource.Antimatter, faction.UnlockedAntimatter, faction.GetDailyIncome(FactionResource.Antimatter)),
                    hull);
                if (outcome == TIFactionState.ShipDesignerOutcome.Success)
                    design = result;
                return outcome;
            }
            catch (Exception e)
            {
                // Filtering can empty a part category the designer assumes is never empty (e.g. no armor left)
                Main.Log("Optimizer: designer failed with the current part filter: " + e.Message);
                return TIFactionState.ShipDesignerOutcome.NoCandidateDesigns;
            }
        }

        // Mirrors the vanilla Auto Design rule (unlocked and on hand), but also allows the resource when the player
        // explicitly budgeted some of it, and never when it is removed
        static bool AllowResource(TIFactionState faction, FactionResource resource, bool unlocked, float available)
        {
            var entry = OptimizerConfig.Get(resource);
            if (entry.Excluded || !unlocked)
                return false;
            return available > 0f || entry.Limited;
        }

        static Attempt Evaluate(TISpaceShipTemplate design, List<FactionResource> excluded)
        {
            var cost = design.spaceResourceConstructionCost(true, null);
            return new Attempt
            {
                design = design,
                cost = cost,
                overrun = OverLimitResources(cost, excluded).Sum(x => x.ratio),
            };
        }

        // Only resources with "No limit" unticked are enforced; zero limits are handled by the part filter
        static IEnumerable<(FactionResource resource, float ratio)> OverLimitResources(TIResourcesCost cost, List<FactionResource> excluded)
        {
            foreach (var entry in OptimizerConfig.All.Where(e => e.Limited && !excluded.Contains(e.resource)))
            {
                var spent = cost.GetSingleCostValue(entry.resource);
                if (spent > entry.limit + 0.001f)
                    yield return (entry.resource, (spent - entry.limit) / Mathf.Max(entry.limit, 0.01f));
            }
        }

        // Raises only the limits this design exceeds, to exactly what it costs; returns a description for the player
        static string RaiseLimitsTo(TIResourcesCost cost, List<FactionResource> excluded)
        {
            var raised = new List<string>();
            foreach (var (resource, _) in OverLimitResources(cost, excluded).ToList())
            {
                var entry = OptimizerConfig.Get(resource);
                entry.limit = RoundUp(cost.GetSingleCostValue(resource));
                raised.Add($"{OptimizerConfig.Name(resource)} to {entry.limit:#,0.##}");
            }
            return raised.Count == 0 ? "" : "Raised " + string.Join(", ", raised) + " - press Auto Design again to use the new limits.";
        }

        static float RoundUp(float value) => Mathf.Ceil(value * 100f) / 100f;

        // How much of a resource each swappable part of the design costs (the hull is fixed by the player)
        static IEnumerable<(TIShipPartTemplate part, float amount)> PartCosts(TIFactionState faction, TISpaceShipTemplate design, FactionResource resource)
        {
            if (design.driveTemplate != null)
                yield return (design.driveTemplate, design.driveTemplate.buildCost().GetSingleCostValue(resource)
                                                    + design.propellantTanksBuildCost(faction, resource));
            if (design.powerPlantTemplate != null)
                yield return (design.powerPlantTemplate, design.powerPlantBuildCost.GetSingleCostValue(resource));
            if (design.radiatorTemplate != null)
                yield return (design.radiatorTemplate, design.radiatorsBuildCost.GetSingleCostValue(resource));

            var armor = new Dictionary<TIShipPartTemplate, float>();
            void AddArmor(TIShipArmorTemplate material, TIResourcesCost armorCost)
            {
                if (material == null)
                    return;
                armor.TryGetValue(material, out var sum);
                armor[material] = sum + armorCost.GetSingleCostValue(resource);
            }
            AddArmor(design.noseArmor.materialTemplate, design.noseArmorBuildCost);
            AddArmor(design.lateralArmor.materialTemplate, design.lateralArmorBuildCost);
            AddArmor(design.tailArmor.materialTemplate, design.tailArmorBuildCost);
            foreach (var kv in armor)
                yield return (kv.Key, kv.Value);

            var magazine = design.magazineModuleMultiplier;
            foreach (var group in design.allWeaponTemplates.GroupBy(w => w))
                yield return (group.Key, group.Sum(w => w.buildCost(magazine).GetSingleCostValue(resource)));
            foreach (var group in design.utilitySlotModuleTemplates.GroupBy(m => m))
                yield return (group.Key, group.Sum(m => m.buildCost().GetSingleCostValue(resource)));
        }
    }
}
