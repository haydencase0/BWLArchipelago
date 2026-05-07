using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Reflection;

namespace BWLArchipelago
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class BWLArchipelagoPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        public BWLArchipelagoPlugin()
        {
            Log = BepInEx.Logging.Logger.CreateLogSource("BWL Archipelago");

            Log.LogInfo("BWL Archipelago mod loading...");

            ArchipelagoManager.Initialize(Log);

            Type playerType = AccessTools.TypeByName("Player");
            Type technologyType = AccessTools.TypeByName("TechnologyDefinition");
            Type entityType = AccessTools.TypeByName("Entity");

            if (playerType == null || technologyType == null || entityType == null)
            {
                Log.LogError("Could not find required game types. Aborting.");
                return;
            }

            MethodInfo target = AccessTools.Method(
                playerType,
                "AddResearchedTechnology",
                new Type[]
                {
                    technologyType,
                    entityType,
                    typeof(bool)
                }
            );

            if (target == null)
            {
                Log.LogError("Could not find AddResearchedTechnology. Aborting.");
                return;
            }

            Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);

            HarmonyMethod prefix = new HarmonyMethod(
                typeof(AddResearchedTechnologyPatch),
                nameof(AddResearchedTechnologyPatch.Prefix)
            );

            HarmonyMethod postfix = new HarmonyMethod(
                typeof(AddResearchedTechnologyPatch),
                nameof(AddResearchedTechnologyPatch.Postfix)
            );

            harmony.Patch(target, prefix: prefix, postfix: postfix);

            Log.LogInfo("Harmony patch applied.");

            // Connect to Archipelago
            ArchipelagoManager.Connect();

            Log.LogInfo("BWL Archipelago mod ready.");
        }
    }

    public static class AddResearchedTechnologyPatch
    {
        public static void Prefix(
            object __instance,
            object technology,
            object discoveringEntity,
            bool grantedByRepair,
            ref bool __state)
        {
            if (technology == null)
            {
                __state = false;
                return;
            }

            __state = GetHasResearched(__instance, technology);
        }

        public static void Postfix(
            object __instance,
            object technology,
            object discoveringEntity,
            bool grantedByRepair,
            bool __state)
        {
            if (technology == null)
                return;

            bool hadBefore = __state;
            bool hasAfter = GetHasResearched(__instance, technology);
            string techName = GetName(technology);

            if (!hadBefore && hasAfter)
            {
                BWLArchipelagoPlugin.Log.LogInfo(
                    "Archipelago: NEW technology researched -> " + techName
                );

                ArchipelagoManager.SendCheck("Researched " + techName);
            }
        }

        private static bool GetHasResearched(object player, object technology)
        {
            if (player == null || technology == null)
                return false;

            MethodInfo method = AccessTools.Method(
                player.GetType(),
                "HasResearchedTechnology",
                new Type[] { technology.GetType() }
            );

            if (method == null)
                return false;

            object result = method.Invoke(player, new object[] { technology });

            return result is bool b && b;
        }

        internal static string GetName(object technology)
        {
            if (technology == null)
                return "<null>";

            FieldInfo field = AccessTools.Field(technology.GetType(), "Name");
            if (field != null)
            {
                object val = field.GetValue(technology);
                if (val != null)
                    return val.ToString();
            }

            PropertyInfo prop = AccessTools.Property(technology.GetType(), "Name");
            if (prop != null)
            {
                object val = prop.GetValue(technology, null);
                if (val != null)
                    return val.ToString();
            }

            return technology.GetType().Name;
        }
    }
    public static class HasResearchedTechnologyPatch
    {
        public static bool Prefix(
            object __instance,
            object technology,
            ref bool __result)
        {
            if (technology == null)
                return true; // let original run

            string techName = AddResearchedTechnologyPatch.GetName(technology);

            if (ArchipelagoManager.IsTechnologyUnlocked(techName))
            {
                __result = true;
                return false; // skip original method, return our result
            }

            return true; // technology not from AP, let original run
        }
    }
}