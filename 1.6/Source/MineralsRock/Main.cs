using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;
using MineralsFramework;

namespace MineralsRock
{
    public class MineralsRockMain : Mod
    {
        public static Harmony harmony;

        public MineralsRockMain(ModContentPack content) : base(content)
        {
            harmony = new Harmony("zacharyfoster.MineralsFramework");
            harmony.PatchAll();

            if (MineralsFrameworkMain.Settings.debugModeEnabled)
            {
                Log.Message("MineralsRock: Harmony patches applied");
            }
        }

    }
}
