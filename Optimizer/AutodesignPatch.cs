using System;
using HarmonyLib;
using PavonisInteractive.TerraInvicta;
using PavonisInteractive.TerraInvicta.Audio;
using ShipOptimizerConfig.UI;

namespace ShipOptimizerConfig.Optimizer
{
    // Routes the designer's Auto Design button through ConstrainedDesigner when the Optimizer Config is in use.
    // Refits and the untouched default config fall through to the game's own handler.
    [HarmonyPatch(typeof(FleetsScreenController), nameof(FleetsScreenController.OnAutodesignSelected))]
    static class AutodesignPatch
    {
        static readonly AccessTools.FieldRef<FleetsScreenController, bool> refitting =
            AccessTools.FieldRefAccess<FleetsScreenController, bool>("refitting");

        static bool Prefix(FleetsScreenController __instance)
        {
            if (!Main.enabled || refitting(__instance) || ConstrainedDesigner.IsDefault)
                return true;
            var template = __instance.newShipTemplate;
            if (template == null || template.role == ShipRole.NoRole)
                return true;

            try
            {
                var faction = GameControl.control.activePlayer;
                var result = ConstrainedDesigner.Design(faction, template.role, template.hullTemplate);
                if (result.design == null)
                {
                    AudioManager.PlayOneShot("event:/SFX/UI_SFX/trig_SFX_BadUI");
                    Main.SaveSettings();
                    OptimizerPanel.ShowTab(true);
                    OptimizerPanel.ShowMessage(result.message);
                    return false;
                }

                // Same steps as the vanilla success path in FleetsScreenController.OnAutodesignSelected
                var design = result.design;
                AudioManager.PlayOneShot("event:/SFX/UI_SFX/trig_SFX_Acknowledge");
                __instance.ResetDesigner(design.hullName, "", design.role);
                Traverse.Create(__instance).Method("ResetShip", design.hullName, design.displayName, ShipRole.NoRole).GetValue();
                __instance.LoadShipTemplateIntoUI(design);
                __instance.changesMadeToExistingClass = true;
                __instance.designerSaveDesignButton.interactable = __instance.CanSaveCurrentDesign;
                OptimizerPanel.ShowMessage(null);
                return false;
            }
            catch (Exception e)
            {
                Main.Error("Constrained Auto Design failed, falling back to the game's Auto Design: " + e);
                return true;
            }
        }
    }
}
