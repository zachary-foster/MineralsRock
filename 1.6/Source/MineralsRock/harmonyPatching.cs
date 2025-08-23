using HarmonyLib;
using MineralsFramework;
using MineralsRock;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MineralsRock
{
    [HarmonyPatch(typeof(ThingSetMaker_Meteorite), "Reset")]
    public static class ThingSetMaker_Meteorite_Reset_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ThingSetMaker_Meteorite __instance)
        {
            // Modify the static nonSmoothedMineables list after reset
            var filteredMineables = (DefDatabase<ThingDef>.AllDefsListForReading.Where((ThingDef x) => x.mineable && !x.building.mineablePreventMeteorite && !x.IsSmoothed &&
typeof(MineralsFramework.ThingDef_StaticMineral).IsAssignableFrom(x.GetType()))).ToList();

            ThingSetMaker_Meteorite.nonSmoothedMineables = filteredMineables;

            if (MineralsFrameworkMain.Settings.debugModeEnabled)
            {
                Log.Message($"Minerals: Replaced meteorite mineables with {filteredMineables.Count} MineralsFramework defs");
            }
        }
    }


    [HarmonyPatch(typeof(ThingSetMaker_Meteorite), "FindRandomMineableDef")]
    public static class ThingSetMaker_Meteorite_FindRandomMineableDef_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref ThingDef __result)
        {
            // Completely replace original logic with simple random selection
            if (ThingSetMaker_Meteorite.nonSmoothedMineables.Count == 0)
                return true;
            
            __result = ThingSetMaker_Meteorite.nonSmoothedMineables.RandomElement();
            
            return false; // Skip original method
        }
    }
}
