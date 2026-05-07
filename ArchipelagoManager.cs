using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using BepInEx.Logging;
using System;
using System.Collections.Generic;

namespace BWLArchipelago
{
    public static class ArchipelagoManager
    {
        // Connection details
        public static string ServerUrl = "localhost";
        public static int ServerPort = 38281;
        public static string SlotName = "TestPlayer";
        public static string Password = "";
        public static string GameName = "Before We Leave";

        // Session
        private static ArchipelagoSession session;

        // State
        public static bool IsConnected = false;

        // Received items
        private static HashSet<string> unlockedTechnologies = new HashSet<string>();

        // Logger reference
        private static ManualLogSource Log;

        public static void Initialize(ManualLogSource log)
        {
            Log = log;
            Log.LogInfo("ArchipelagoManager initialized.");
        }

        public static void Connect()
        {
            Log.LogInfo(
                "Connecting to Archipelago server: " +
                ServerUrl + ":" + ServerPort
            );

            session = ArchipelagoSessionFactory.CreateSession(
                ServerUrl,
                ServerPort
            );

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
            }
            else
            {
                IsConnected = false;
                LoginFailure failure = (LoginFailure)result;

                foreach (string error in failure.Errors)
                {
                    Log.LogError("Archipelago connection error: " + error);
                }
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
            if (!IsConnected)
            {
                Log.LogWarning("Cannot send check: not connected to Archipelago.");
                return;
            }

            Log.LogInfo("Sending check: " + locationName);

            long locationId = session.Locations.GetLocationIdFromName(
                GameName,
                locationName
            );

            if (locationId < 0)
            {
                Log.LogError("Unknown location: " + locationName);
                return;
            }

            session.Locations.CompleteLocationChecks(locationId);

            Log.LogInfo("Check sent: " + locationName);
        }

        public static bool IsTechnologyUnlocked(string techName)
        {
            return unlockedTechnologies.Contains(techName);
        }

        private static void OnItemReceived(ReceivedItemsHelper helper)
        {
            while (helper.Any())
            {
                ItemInfo item = helper.DequeueItem();

                string itemName = item.ItemName;

                Log.LogInfo("Item received from Archipelago: " + itemName);

                string techName = itemName;

                if (techName.EndsWith(" Technology"))
                {
                    techName = techName.Substring(
                        0,
                        techName.Length - " Technology".Length
                    );
                }

                unlockedTechnologies.Add(techName);

                Log.LogInfo("Technology unlocked: " + techName);
            }
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