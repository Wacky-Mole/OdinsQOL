﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using CraftyBoxes.Compatibility.WardIsLove;
using HarmonyLib;
using OdinQOL.Patches;
using OdinQOL.Patches.BiFrost;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OdinQOL
{
    public class ConnectionParams
    {
        public GameObject Connection = null!;
        public Vector3 StationPos;
    }

    internal class Utilities
    {
        internal static bool AllowByKey()
        {
            if (CFC.preventModKey.Value.IsPressed())
                return CFC.switchPrevent.Value;
            return !CFC.switchPrevent.Value;
        }

        public static bool IgnoreKeyPresses(bool extra = false)
        {
            if (!extra)
                return ZNetScene.instance == null || Player.m_localPlayer == null || Minimap.IsOpen() ||
                       Console.IsVisible() || TextInput.IsVisible() || ZNet.instance.InPasswordDialog() ||
                       Chat.instance?.HasFocus() == true;
            return ZNetScene.instance == null || Player.m_localPlayer == null || Minimap.IsOpen() ||
                   Console.IsVisible() || TextInput.IsVisible() || ZNet.instance.InPasswordDialog() ||
                   Chat.instance?.HasFocus() == true || StoreGui.IsVisible() || InventoryGui.IsVisible() ||
                   Menu.IsVisible() || TextViewer.instance?.IsVisible() == true;
        }

        public static bool CheckKeyDown(string value)
        {
            try
            {
                return Input.GetKeyDown(value.ToLower());
            }
            catch
            {
                return false;
            }
        }

        public static bool CheckKeyHeld(string value, bool req = true)
        {
            try
            {
                return Input.GetKey(value);
            }
            catch
            {
                return !req;
            }
        }

        public static bool CheckKeyDownKeycode(KeyCode value)
        {
            try
            {
                return Input.GetKeyDown(value);
            }
            catch
            {
                return false;
            }
        }

        public static bool CheckKeyHeldKeycode(KeyCode value, bool req = true)
        {
            try
            {
                return Input.GetKey(value);
            }
            catch
            {
                return !req;
            }
        }

        public static float ApplyModifierValue(float targetValue, float value)
        {
            if (value <= -100)
                value = -100;

            float newValue;

            if (value >= 0)
                newValue = targetValue + targetValue / 100 * value;
            else
                newValue = targetValue - targetValue / 100 * (value * -1);

            return newValue;
        }

        public static void AddContainer(Container container, ZNetView nview)
        {
            try
            {
                OdinQOLplugin.QOLLogger.LogDebug(
                    $"Checking {container.name} {nview != null} {nview?.GetZDO() != null} {nview?.GetZDO()?.GetLong("creator".GetStableHashCode())}");
                if (container.GetInventory() == null || nview?.GetZDO() == null ||
                    (!container.name.StartsWith("piece_", StringComparison.Ordinal) &&
                     !container.name.StartsWith("Container", StringComparison.Ordinal) &&
                     nview.GetZDO().GetLong("creator".GetStableHashCode()) == 0)) return;
                OdinQOLplugin.QOLLogger.LogDebug($"Adding {container.name}");
                OdinQOLplugin.ContainerList.Add(container);
            }
            catch
            {
                // ignored
            }
        }


        public static List<Container> GetNearbyContainers(Vector3 center)
        {
            List<Container> containers = new();
            foreach (Container container in OdinQOLplugin.ContainerList.Where(container => container != null &&
                         container.GetComponentInParent<Piece>() != null && Player.m_localPlayer != null &&
                         container?.transform != null && container.GetInventory() != null && (CFC.mRange.Value <= 0 ||
                             Vector3.Distance(center, container.transform.position) <
                             CFC.mRange.Value) && container.CheckAccess(Player.m_localPlayer.GetPlayerID()) &&
                         !container.IsInUse()))
            {
                if (WardIsLovePlugin.IsLoaded() && WardIsLovePlugin.WardEnabled()!.Value)
                {
                    if (!WardMonoscript.CheckInWardMonoscript(container.transform.position))
                    {
                        container.Load();
                        containers.Add(container);
                        continue;
                    }

                    if (!WardMonoscript.CheckAccess(container.transform.position, 0f, false)) continue;
                    //container.GetComponent<ZNetView>()?.ClaimOwnership();
                    container.Load();
                    containers.Add(container);
                }
                else
                {
                    container.Load();
                    containers.Add(container);
                }
            }


            return containers;
        }

        public static int ConnectionExists(CraftingStation station)
        {
            foreach (ConnectionParams c in OdinQOLplugin.ContainerConnections.Where(c =>
                         Vector3.Distance(c.StationPos, station.GetConnectionEffectPoint()) < 0.1f))
            {
                return OdinQOLplugin.ContainerConnections.IndexOf(c);
            }

            return -1;
        }

        public static void StopConnectionEffects()
        {
            if (OdinQOLplugin.ContainerConnections.Count > 0)
            {
                foreach (ConnectionParams c in OdinQOLplugin.ContainerConnections)
                {
                    Object.Destroy(c.Connection);
                }
            }

            OdinQOLplugin.ContainerConnections.Clear();
        }

        internal static void PullResources(Player player, Piece.Requirement[] resources, int qualityLevel)
        {
            Inventory pInventory = Player.m_localPlayer.GetInventory();
            List<Container> nearbyContainers = GetNearbyContainers(Player.m_localPlayer.transform.position);
            foreach (Piece.Requirement requirement in resources)
            {
                if (requirement.m_resItem)
                {
                    int totalRequirement = requirement.GetAmount(qualityLevel);
                    if (totalRequirement <= 0)
                        continue;

                    string reqName = requirement.m_resItem.m_itemData.m_shared.m_name;
                    int totalAmount = 0;
                    OdinQOLplugin.QOLLogger.LogDebug(
                        $"have {totalAmount}/{totalRequirement} {reqName} in player inventory");

                    foreach (Container c in nearbyContainers)
                    {
                        Inventory cInventory = c.GetInventory();
                        int thisAmount = Mathf.Min(cInventory.CountItems(reqName), totalRequirement - totalAmount);

                        OdinQOLplugin.QOLLogger.LogDebug(
                            $"Container at {c.transform.position} has {cInventory.CountItems(reqName)}");

                        if (thisAmount == 0)
                            continue;


                        for (int i = 0; i < cInventory.GetAllItems().Count; ++i)
                        {
                            ItemDrop.ItemData item = cInventory.GetItem(i);
                            if (item.m_shared.m_name != reqName) continue;
                            OdinQOLplugin.QOLLogger.LogDebug($"Got stack of {item.m_stack} {reqName}");
                            int stackAmount = Mathf.Min(item.m_stack, totalRequirement - totalAmount);

                            if (!pInventory.HaveEmptySlot())
                                stackAmount =
                                    Math.Min(
                                        pInventory.FindFreeStackSpace(item.m_shared.m_name), stackAmount);

                            OdinQOLplugin.QOLLogger.LogDebug($"Sending {stackAmount} {reqName} to player");

                            ItemDrop.ItemData sendItem = item.Clone();
                            sendItem.m_stack = stackAmount;

                            if (OdinQOLplugin.ItemStackMultiplier.Value > 0)
                            {
                                sendItem.m_shared.m_weight = ApplyModifierValue(sendItem.m_shared.m_weight,
                                    OdinQOLplugin.WeightReduction.Value);
                                OdinQOLplugin.QOLLogger.LogDebug("Send ItemStackSize:" +
                                                                 sendItem.m_shared.m_maxStackSize +
                                                                 $"\nRequirement Stack Value: {requirement.m_resItem.m_itemData.m_shared.m_maxStackSize}");

                                if (sendItem.m_shared.m_maxStackSize > 1)
                                    if (OdinQOLplugin.ItemStackMultiplier.Value >= 1)
                                        sendItem.m_shared.m_maxStackSize = requirement.m_resItem.m_itemData.m_shared.m_maxStackSize * (int)OdinQOLplugin.ItemStackMultiplier.Value;

                                OdinQOLplugin.QOLLogger.LogDebug($"\nFinal Value: {sendItem.m_shared.m_maxStackSize}");
                            }


                            pInventory.AddItem(sendItem);

                            if (stackAmount == item.m_stack)
                            {
                                cInventory.RemoveItem(item);
                                --i;
                            }
                            else
                                item.m_stack -= stackAmount;

                            totalAmount += stackAmount;
                            OdinQOLplugin.QOLLogger.LogDebug(
                                $"Total amount is now {totalAmount}/{totalRequirement} {reqName}");

                            if (totalAmount >= totalRequirement)
                                break;
                        }

                        c.Save();
                        cInventory.Changed();

                        if (totalAmount < totalRequirement) continue;
                        OdinQOLplugin.QOLLogger.LogDebug($"Pulled enough {reqName}");
                        break;
                    }
                }

                if (CFC.pulledMessage.Value?.Length > 0)
                    player.Message(MessageHud.MessageType.Center, CFC.pulledMessage.Value);
            }
        }


        /* FastLink Utils */
        internal static void SaveAndReset(object sender, EventArgs e)
        {
            OdinQOLplugin.context.Config.Save();
            BiFrostSetupGui.BFRootGo.GetComponent<RectTransform>().anchoredPosition =
                new Vector2(BiFrost.UIAnchor.Value.x, BiFrost.UIAnchor.Value.y);
        }

        internal static void ReadNewServers(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(BiFrostServers.ConfigPath) || BiFrost.DisableBiFrost.Value) return;
            try
            {
                OdinQOLplugin.QOLLogger.LogDebug("FastLink: Reloading Server List");
                BiFrostSetupGui.Connecting = null;
                foreach (GameObject serverListElement in BiFrostSetupGui.MServerListElements)
                    Object.Destroy(serverListElement);
                BiFrostSetupGui.MServerListElements.Clear();

                BiFrostServers.Init();
                BiFrostFunctions.AbortConnect();
                BiFrostFunctions.PopulateServerList(BiFrostSetupGui.BF);
                BiFrostFunctions.UpdateServerList();
                BiFrostSetupGui.MJoinServer = null;
            }
            catch
            {
                OdinQOLplugin.QOLLogger.LogError($"There was an issue loading your {BiFrostServers.ConfigFileName}");
                OdinQOLplugin.QOLLogger.LogError("Please check your config entries for spelling and format!");
            }
        }
    }

    // Aedenthorn was getting this prefab almost every frame after iterating through all gameobjects...not sure why, but this should improve performance greatly in the UpdatePlacementGhost patch for CFC.
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
    static class ZNetSceneAwakePatch
    {
        static void Postfix(ZNetScene __instance)
        {
            OdinQOLplugin.ConnectionVfxPrefab = __instance.GetPrefab("vfx_ExtensionConnection");
            if (OdinQOLplugin.ConnectionVfxPrefab != null) return;
            foreach (GameObject go in (Resources.FindObjectsOfTypeAll(typeof(GameObject)) as GameObject[])!)
            {
                if (go.name != "vfx_ExtensionConnection") continue;
                OdinQOLplugin.ConnectionVfxPrefab = go;
            }
        }
    }
}