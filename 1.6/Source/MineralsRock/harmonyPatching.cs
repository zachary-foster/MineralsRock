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

    [HarmonyPatch(typeof(Skyfaller), "SpawnSetup")]
    public static class Skyfaller_SpawnSetup_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Skyfaller __instance, Map map, bool respawningAfterLoad)
        {
            if (respawningAfterLoad || __instance.innerContainer.Count == 0)
                return;

            // Get nearest rocky terrain within 15 tiles
            IntVec3 center = __instance.Position;
            TerrainDef nearestRockTerrain = null;
            int maxDist = 15;
            
            for (int x = center.x - maxDist; x <= center.x + maxDist; x++)
            {
                for (int z = center.z - maxDist; z <= center.z + maxDist; z++)
                {
                    IntVec3 pos = new IntVec3(x, 0, z);
                    if (pos.InBounds(map))
                    {
                        TerrainDef terrain = map.terrainGrid.TerrainAt(pos);
                        if (terrain != null && terrain.IsRock)
                        {
                            nearestRockTerrain = terrain;
                            break;
                        }
                    }
                }
                if (nearestRockTerrain != null) break;
            }

            if (nearestRockTerrain == null)
                return;

            // Try to find matching passable rock def (e.g. "Granite_Rough" -> "PassableGranite")
            string rockName = nearestRockTerrain.defName.Split('_')[0];
            ThingDef rockDef = DefDatabase<ThingDef>.GetNamedSilentFail($"Passable{rockName}");
            
            if (rockDef == null)
                return;

            // Calculate ring size based on item count
            int itemCount = __instance.innerContainer.Count;
            float radius = Mathf.Sqrt(itemCount) * 1.5f;
            int diameter = Mathf.Clamp(Mathf.RoundToInt(radius * 2), 3, 15);

            // Generate circular pattern
            foreach (IntVec3 pos in GenRadial.RadialPatternInRadius(diameter/2))
            {
                IntVec3 targetPos = center + pos;
                if (targetPos.InBounds(map) && 
                    targetPos.GetEdifice(map) == null && 
                    GenSight.LineOfSight(center, targetPos, map))
                {
                    GenSpawn.Spawn(rockDef, targetPos, map);
                }
            }

            if (MineralsFrameworkMain.Settings.debugModeEnabled)
            {
                Log.Message($"Generated {rockDef.defName} ring with diameter {diameter} around {center}");
            }
        }
    }
}
