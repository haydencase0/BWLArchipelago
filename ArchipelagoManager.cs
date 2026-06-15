using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json;
using Planet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace BWLArchipelago
{
    public static class ArchipelagoManager
    {
        // Connection details - set by ArchipelagoConnectionUI before Connect() is called
        public static string ServerUrl = "archipelago.gg";
        public static int ServerPort = 38281;
        public static string SlotName = "";
        public static string Password = "";
        public static string GameName = "Before We Leave";

        // Session
        private static ArchipelagoSession session;

        // State flags
        public static bool IsConnected = false;
        public static bool IsGrantingTechnology = false;
        public static bool IsCompletingPlayerCheck = false;
        public static bool IsCheckingResearchValidity = false;
        public static string CurrentlyValidatingTech = null;

        // Pending items
        private static List<string> pendingUnlocks = new List<string>();
        private static List<string> pendingChecks = new List<string>();
        private static List<(string resourceName, int amount)> pendingResourceGrants
            = new List<(string, int)>();

        // Tracked state - persisted to disk
        private static HashSet<string> researchedTechnologies = new HashSet<string>();
        private static HashSet<string> grantedTechnologies = new HashSet<string>();
        private static HashSet<string> queuedResearches = new HashSet<string>();
        private static HashSet<string> sentChecks = new HashSet<string>();

        // Progressive technology groups - ordered lists of tech names.
        // Each time a progressive item is received, the next ungranted tech
        // in the list is granted.
        private static readonly Dictionary<string, List<string>> progressiveTechGroups
            = new Dictionary<string, List<string>>
        {
            { "Progressive Housing", new List<string> { "House", "School", "Apartment" } },
            { "Progressive Mining", new List<string> { "Mining", "Metalwork", "Glass", "Laser" } },
            { "Progressive Elevator", new List<string> { "Elevator", "SpaceElevator" } },
            { "Progressive Power", new List<string> { "Repair", "Power", "OilPower", "CleanPower" } },
            { "Progressive Happiness", new List<string> { "Pump", "Music", "MeetingSquare", "RoadDecoration" } },
            { "Progressive Food", new List<string> { "Gardening", "Cooking", "Farming", "Baking" } },
            { "Progressive Upgrades", new List<string> { "Tinkering", "Automation", "Filtering" } },
            { "Progressive Rocket", new List<string> { "Fuel", "Space" } },
            { "Progressive Shipping", new List<string> { "Shipping", "AdvancedShipping", "Airships" } },
        };

        // Resource grant items - maps AP item name to resource name and amount
        private static readonly Dictionary<string, (string resourceName, int amount)> resourceGrants
            = new Dictionary<string, (string, int)>
        {
            { "10 Stone", ("Stone", 10) },
        };

        // Logger
        private static ManualLogSource Log;

        // Save data class for JSON serialization
        [Serializable]
        private class ArchipelagoSaveData
        {
            public List<string> ResearchedTechnologies = new List<string>();
            public List<string> GrantedTechnologies = new List<string>();
            public List<string> QueuedResearches = new List<string>();
            public List<string> SentChecks = new List<string>();
        }

        public static void Initialize(ManualLogSource log)
        {
            Log = log;
            Log.LogInfo("ArchipelagoManager initialized.");
        }

        // Returns the path for the Archipelago companion save file.
        // Uses the game's unique save ID so each save slot has its own file.
        private static string GetSaveFilePath()
        {
            try
            {
                FieldInfo uniqueIdField = AccessTools.Field(
                    AccessTools.TypeByName("GameManager"), "uniqueGameId"
                );
                string uniqueId = uniqueIdField?.GetValue(null) as string;

                PropertyInfo savePathProp = AccessTools.Property(
                    AccessTools.TypeByName("AppData"), "GameSavePath"
                );
                string savePath = savePathProp?.GetValue(null) as string;

                if (string.IsNullOrEmpty(uniqueId) || string.IsNullOrEmpty(savePath))
                    return null;

                return Path.Combine(savePath, "archipelago.json");
            }
            catch (Exception ex)
            {
                Log.LogError("Error getting save file path: " + ex.Message);
                return null;
            }
        }

        // Saves our state to a JSON file alongside the game save.
        public static void SaveArchipelagoState()
        {
            try
            {
                string path = GetSaveFilePath();
                if (path == null)
                {
                    Log.LogWarning("Could not determine save file path - state not saved.");
                    return;
                }

                ArchipelagoSaveData data = new ArchipelagoSaveData
                {
                    ResearchedTechnologies = new List<string>(researchedTechnologies),
                    GrantedTechnologies = new List<string>(grantedTechnologies),
                    QueuedResearches = new List<string>(queuedResearches),
                    SentChecks = new List<string>(sentChecks)
                };

                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(path, json);
                Log.LogInfo("Archipelago state saved to: " + path);
            }
            catch (Exception ex)
            {
                Log.LogError("Error saving Archipelago state: " + ex.Message);
            }
        }

        // Loads our state from the companion save file.
        // Called before ReadyToStartGame fires so state is ready when AP replays items.
        public static void LoadArchipelagoState()
        {
            try
            {
                string path = GetSaveFilePath();
                if (path == null || !File.Exists(path))
                {
                    Log.LogInfo("No Archipelago save file found - starting fresh.");
                    return;
                }

                string json = File.ReadAllText(path);
                ArchipelagoSaveData data = JsonConvert.DeserializeObject<ArchipelagoSaveData>(json);

                if (data == null)
                {
                    Log.LogWarning("Failed to deserialize Archipelago save data.");
                    return;
                }

                researchedTechnologies = new HashSet<string>(
                    data.ResearchedTechnologies ?? new List<string>()
                );
                grantedTechnologies = new HashSet<string>(
                    data.GrantedTechnologies ?? new List<string>()
                );
                queuedResearches = new HashSet<string>(
                    data.QueuedResearches ?? new List<string>()
                );
                sentChecks = new HashSet<string>(
                    data.SentChecks ?? new List<string>()
                );

                Log.LogInfo(
                    "Archipelago state loaded - Researched: " + researchedTechnologies.Count +
                    " | Granted: " + grantedTechnologies.Count +
                    " | Queued: " + queuedResearches.Count +
                    " | Sent checks: " + sentChecks.Count
                );
            }
            catch (Exception ex)
            {
                Log.LogError("Error loading Archipelago state: " + ex.Message);
            }
        }

        // Triggers a fresh EvaluateAvailableBuildings on all colonized islands
        // so buildings unlocked by AP grants are available after load.
        public static void RefreshAvailableBuildings()
        {
            try
            {
                foreach (Entity entity in GameManager.EntityManager
                    .GetEntitiesOfType(EntityType.Planet))
                {
                    if (!entity.Enabled) continue;
                    PlanetComponent planet = entity.GetPlanet();
                    if (planet == null) continue;

                    for (int i = 0; i < planet.islands.Count; i++)
                    {
                        PlanetIsland island = planet.islands[i];
                        if (island.owningPlayer != null)
                            island.EvaluateAvailableBuildings(true, false);
                    }
                }

                Log.LogInfo("Available buildings refreshed.");
            }
            catch (Exception ex)
            {
                Log.LogError("Error refreshing available buildings: " + ex.Message);
            }
        }

        public static bool Connect()
        {
            Log.LogInfo(
                "Connecting to Archipelago server: " +
                ServerUrl + ":" + ServerPort + " as " + SlotName
            );

            session = ArchipelagoSessionFactory.CreateSession(ServerUrl, ServerPort);

            session.Items.ItemReceived += OnItemReceived;
            session.Socket.ErrorReceived += OnError;
            session.Socket.SocketClosed += OnSocketClosed;

            LoginResult result = session.TryConnectAndLogin(
                GameName,
                SlotName,
                ItemsHandlingFlags.AllItems,
                password: Password
            );

            if (result.Successful)
            {
                IsConnected = true;
                Log.LogInfo("Connected to Archipelago successfully!");

                if (pendingChecks.Count > 0)
                {
                    Log.LogInfo("Flushing " + pendingChecks.Count + " pending checks.");
                    List<string> toSend = new List<string>(pendingChecks);
                    pendingChecks.Clear();
                    foreach (string check in toSend)
                        SendCheckInternal(check);
                }

                return true;
            }
            else
            {
                IsConnected = false;
                LoginFailure failure = (LoginFailure)result;
                foreach (string error in failure.Errors)
                    Log.LogError("Archipelago connection error: " + error);
                return false;
            }
        }

        public static void Disconnect()
        {
            if (session != null && IsConnected)
            {
                session.Socket.DisconnectAsync();
                IsConnected = false;
                Log.LogInfo("Disconnected from Archipelago.");
            }
        }

        public static void SendCheck(string locationName)
        {
            string techName = locationName.Replace("Researched ", "");

            if (!IsConnected)
            {
                Log.LogWarning("Not connected - queuing check: " + locationName);
                queuedResearches.Add(techName);
                pendingChecks.Add(locationName);
                return;
            }

            queuedResearches.Add(techName);
            SendCheckInternal(locationName);
        }

        private static void SendCheckInternal(string locationName)
        {
            if (sentChecks.Contains(locationName))
            {
                Log.LogInfo("Check already sent, skipping: " + locationName);
                return;
            }

            Log.LogInfo("Sending check: " + locationName);

            long locationId = session.Locations.GetLocationIdFromName(
                GameName, locationName
            );

            if (locationId < 0)
            {
                Log.LogError("Unknown location: " + locationName);
                return;
            }

            session.Locations.CompleteLocationChecks(locationId);
            sentChecks.Add(locationName);
            Log.LogInfo("Check sent: " + locationName);
        }

        public static void MarkTechnologyResearched(string techName)
        {
            researchedTechnologies.Add(techName);
            Log.LogInfo("Marked as researched (checkmark): " + techName);
        }

        public static void CancelResearch(string techName)
        {
            queuedResearches.Remove(techName);
            Log.LogInfo("Research cancelled, removed from queue: " + techName);
        }

        public static bool IsResearchQueued(string techName)
            => queuedResearches.Contains(techName);

        public static bool IsTechnologyResearched(string techName)
            => researchedTechnologies.Contains(techName);

        public static bool IsTechnologyGranted(string techName)
            => grantedTechnologies.Contains(techName);

        public static bool IsCheckSent(string locationName)
            => sentChecks.Contains(locationName);

        public static void AddGrantedTechsToResearched()
        {
            object[] context = GetPlayerAndTechManager();
            if (context == null) return;

            object player = context[0];
            object techManager = context[1];
            MethodInfo getTechnology = context[2] as MethodInfo;

            FieldInfo researchedField = AccessTools.Field(
                player.GetType(), "researchedTechnologies"
            );
            object researchedSet = researchedField?.GetValue(player);
            if (researchedSet == null) return;

            MethodInfo addMethod = researchedSet.GetType().GetMethod("Add");

            foreach (string techName in grantedTechnologies)
            {
                object techDef = getTechnology?.Invoke(techManager, new object[] { techName });
                if (techDef != null)
                    addMethod?.Invoke(researchedSet, new object[] { techDef });
            }
        }

        public static void RemoveGrantedTechsFromResearched()
        {
            object[] context = GetPlayerAndTechManager();
            if (context == null) return;

            object player = context[0];
            object techManager = context[1];
            MethodInfo getTechnology = context[2] as MethodInfo;

            FieldInfo researchedField = AccessTools.Field(
                player.GetType(), "researchedTechnologies"
            );
            object researchedSet = researchedField?.GetValue(player);
            if (researchedSet == null) return;

            MethodInfo removeMethod = researchedSet.GetType().GetMethod("Remove");

            foreach (string techName in grantedTechnologies)
            {
                object techDef = getTechnology?.Invoke(techManager, new object[] { techName });
                if (techDef != null)
                    removeMethod?.Invoke(researchedSet, new object[] { techDef });
            }
        }

        private static object[] GetPlayerAndTechManager()
        {
            Type gameControllerType = AccessTools.TypeByName("GameController");
            if (gameControllerType == null) return null;

            MethodInfo getSinglePlayerView = AccessTools.Method(
                gameControllerType, "GetSinglePlayerView"
            );
            object playerView = getSinglePlayerView?.Invoke(null, null);
            if (playerView == null) return null;

            PropertyInfo playerProp = AccessTools.Property(playerView.GetType(), "Player");
            object player = playerProp?.GetValue(playerView, null);
            if (player == null) return null;

            Type appDataType = AccessTools.TypeByName("AppData");
            FieldInfo techManagerField = AccessTools.Field(
                appDataType, "technologyDefinitionManager"
            );
            object techManager = techManagerField?.GetValue(null);
            if (techManager == null) return null;

            MethodInfo getTechnology = AccessTools.Method(
                techManager.GetType(),
                "GetTechnology",
                new Type[] { typeof(string) }
            );

            return new object[] { player, techManager, getTechnology };
        }

        private static bool IsAnyLibraryBusy()
        {
            try
            {
                foreach (Entity entity in GameManager.EntityManager
                    .GetEntitiesWithComponent(EntityComponentType.Library))
                {
                    if (!entity.Enabled) continue;
                    LibraryComponent lib = entity.GetLibrary();
                    if (lib != null && lib.IsResearching())
                        return true;
                }
            }
            catch { }
            return false;
        }

        public static void OnGameReady()
        {
            Log.LogInfo("Game ready. Flushing " + pendingUnlocks.Count + " pending unlocks.");

            List<string> toGrant = new List<string>(pendingUnlocks);
            pendingUnlocks.Clear();

            foreach (string techName in toGrant)
                GrantTechnology(techName);

            // Flush pending resource grants
            FlushPendingResourceGrants();

            // Re-evaluate buildings after all grants are applied so buildings
            // unlocked by AP grants are available after loading a save
            RefreshAvailableBuildings();
        }

        private static void FlushPendingResourceGrants()
        {
            if (pendingResourceGrants.Count == 0) return;

            List<(string, int)> toGrant = new List<(string, int)>(pendingResourceGrants);
            pendingResourceGrants.Clear();

            foreach (var (resourceName, amount) in toGrant)
                GrantResource(resourceName, amount);
        }

        private static void GrantResource(string resourceName, int amount)
        {
            foreach (Entity entity in GameManager.EntityManager
                .GetEntitiesWithComponent(EntityComponentType.Storage))
            {
                if (!entity.Enabled) continue;
                BuildingComponent building = entity.GetBuilding();
                if (building == null) continue;
                if (!building.BuildingIsWarehouseOrStorageHub()) continue;

                StorageComponent storage = entity.GetStorage();
                if (storage == null) continue;

                ResourceStorage resource = storage.GetIncomingResourceByName(resourceName);
                if (resource != null)
                {
                    resource.Store(amount, false, false);
                    Log.LogInfo("Granted " + amount + " " + resourceName +
                        " to " + building.Name);
                    return;
                }
            }

            Log.LogInfo("No warehouse found - deferring resource grant: " +
                amount + " " + resourceName);
            pendingResourceGrants.Add((resourceName, amount));
        }

        private static void OnItemReceived(ReceivedItemsHelper helper)
        {
            while (helper.Any())
            {
                ItemInfo item = helper.DequeueItem();
                string itemName = item.ItemName;

                Log.LogInfo("Item received from Archipelago: " + itemName);

                // Check for resource grant items
                if (resourceGrants.TryGetValue(itemName, out var grant))
                {
                    Log.LogInfo("Resource item received: " + itemName);
                    GrantResource(grant.resourceName, grant.amount);
                    continue;
                }

                // Check for progressive items
                if (progressiveTechGroups.TryGetValue(itemName, out List<string> techList))
                {
                    bool found = false;
                    foreach (string techName in techList)
                    {
                        if (!grantedTechnologies.Contains(techName))
                        {
                            Log.LogInfo(
                                "Progressive item: " + itemName +
                                " -> granting: " + techName
                            );
                            GrantTechnology(techName);
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        Log.LogWarning(
                            "Progressive item received but all techs already granted: " +
                            itemName
                        );
                    }

                    continue;
                }

                // Non-progressive item - strip " Technology" suffix and grant
                string singleTechName = itemName;
                if (singleTechName.EndsWith(" Technology"))
                    singleTechName = singleTechName.Substring(
                        0, singleTechName.Length - " Technology".Length
                    );

                GrantTechnology(singleTechName);
            }
        }

        private static void GrantTechnology(string techName)
        {
            if (grantedTechnologies.Contains(techName))
            {
                Log.LogInfo("Already granted, skipping: " + techName);
                return;
            }

            Type gameControllerType = AccessTools.TypeByName("GameController");
            if (gameControllerType == null)
            {
                Log.LogWarning("GameController not found. Queuing: " + techName);
                pendingUnlocks.Add(techName);
                return;
            }

            MethodInfo getSinglePlayerView = AccessTools.Method(
                gameControllerType, "GetSinglePlayerView"
            );
            object playerView = getSinglePlayerView?.Invoke(null, null);

            if (playerView == null)
            {
                Log.LogInfo("No PlayerView yet. Queuing: " + techName);
                pendingUnlocks.Add(techName);
                return;
            }

            if (IsAnyLibraryBusy())
            {
                Log.LogInfo("Library busy - deferring grant: " + techName);
                pendingUnlocks.Add(techName);
                return;
            }

            PropertyInfo playerProp = AccessTools.Property(playerView.GetType(), "Player");
            object player = playerProp?.GetValue(playerView, null);

            if (player == null)
            {
                Log.LogWarning("Could not get Player. Queuing: " + techName);
                pendingUnlocks.Add(techName);
                return;
            }

            Type appDataType = AccessTools.TypeByName("AppData");
            FieldInfo techManagerField = AccessTools.Field(
                appDataType, "technologyDefinitionManager"
            );
            object techManager = techManagerField?.GetValue(null);

            if (techManager == null)
            {
                Log.LogWarning("TechnologyDefinitionManager not available. Queuing: " + techName);
                pendingUnlocks.Add(techName);
                return;
            }

            MethodInfo getTechnology = AccessTools.Method(
                techManager.GetType(),
                "GetTechnology",
                new Type[] { typeof(string) }
            );
            object techDef = getTechnology?.Invoke(techManager, new object[] { techName });

            if (techDef == null)
            {
                Log.LogWarning("Could not find TechnologyDefinition for: " + techName);
                return;
            }

            Type technologyType = AccessTools.TypeByName("TechnologyDefinition");
            Type entityType = AccessTools.TypeByName("Entity");

            MethodInfo addResearched = AccessTools.Method(
                player.GetType(),
                "AddResearchedTechnology",
                new Type[] { technologyType, entityType, typeof(bool) }
            );

            if (addResearched == null)
            {
                Log.LogWarning("Could not find AddResearchedTechnology method.");
                return;
            }

            FieldInfo researchedField = AccessTools.Field(
                player.GetType(), "researchedTechnologies"
            );
            object researchedSet = researchedField?.GetValue(player);

            if (researchedSet == null)
            {
                Log.LogWarning("Could not get researchedTechnologies set.");
                return;
            }

            grantedTechnologies.Add(techName);

            IsGrantingTechnology = true;
            addResearched.Invoke(player, new object[] { techDef, null, false });
            IsGrantingTechnology = false;

            MethodInfo removeSelf = researchedSet.GetType().GetMethod("Remove");
            object wasRemoved = removeSelf?.Invoke(researchedSet, new object[] { techDef });
            Log.LogInfo("Remove result for " + techName + ": " + wasRemoved);

            Log.LogInfo("Technology granted: " + techName);

            // Flush any pending resource grants now that a tech was granted
            FlushPendingResourceGrants();
        }

        private static void OnError(Exception exception, string message)
        {
            Log.LogError("Archipelago socket error: " + message);
            if (exception != null)
                Log.LogError(exception.ToString());
            IsConnected = false;
        }

        private static void OnSocketClosed(string reason)
        {
            Log.LogWarning("Archipelago socket closed: " + reason);
            IsConnected = false;
        }
    }
}