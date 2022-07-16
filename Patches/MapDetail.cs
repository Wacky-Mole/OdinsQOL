﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace OdinQOL.Patches
{
    internal class MapDetail
    {
        private static Vector2 lastPos = Vector2.zero;
        private static List<int> lastPixels = new();
        private static Texture2D mapTexture;

        public static ConfigEntry<float> showRange;
        public static ConfigEntry<float> updateDelta;
        public static ConfigEntry<bool> showBuildings;
        public static ConfigEntry<bool> MapDetailOn;
        public static ConfigEntry<bool> displayCartsAndBoats;

        public static ConfigEntry<Color> personalBuildingColor;
        public static ConfigEntry<Color> otherBuildingColor;
        public static ConfigEntry<Color> unownedBuildingColor;
        public static ConfigEntry<string> customPlayerColors;

        public static IEnumerator UpdateMap(bool force)
        {
            if (!MapDetailOn.Value)
                yield break;
            if (Player.m_localPlayer == null)
                yield break;

            if (force)
                yield return null;


            Vector3 position = Player.m_localPlayer.transform.position;
            Vector2 coords = new(position.x,
                position.z);

            if (!force && Vector2.Distance(lastPos, coords) < updateDelta.Value)
                yield break;

            lastPos = coords;

            Dictionary<int, long>? pixels = new();
            bool newPix = false;

            foreach (Collider? collider in Physics.OverlapSphere(Player.m_localPlayer.transform.position,
                         Mathf.Max(showRange.Value, 0), LayerMask.GetMask("piece")))
            {
                Piece? piece = collider.GetComponentInParent<Piece>();
                if (piece != null && piece.GetComponent<ZNetView>().IsValid())
                {
                    Vector3 pos = piece.transform.position;
                    float mx;
                    float my;
                    int num = Minimap.instance.m_textureSize / 2;
                    mx = pos.x / Minimap.instance.m_pixelSize + num;
                    my = pos.z / Minimap.instance.m_pixelSize + num;

                    int x = Mathf.RoundToInt(mx / Minimap.instance.m_textureSize * mapTexture.width);
                    int y = Mathf.RoundToInt(my / Minimap.instance.m_textureSize * mapTexture.height);

                    int idx = x + y * mapTexture.width;
                    if (!pixels.ContainsKey(idx))
                    {
                        if (!lastPixels.Contains(idx))
                            newPix = true;
                        pixels[idx] = piece.GetCreator();
                    }
                    //OdinQOLplugin.QOLLogger.LogDebug($"pos {pos}; map pos: {mx},{my} pixel pos {x},{y}; index {idx}");
                }
            }

            if (!newPix)
            {
                foreach (int i in lastPixels)
                    if (!pixels.ContainsKey(i))
                        goto newpixels;
                OdinQOLplugin.QOLLogger.LogDebug("No new pixels");
                yield break;
            }

            newpixels:

            lastPixels = new List<int>(pixels.Keys);

            if (pixels.Count == 0)
            {
                OdinQOLplugin.QOLLogger.LogDebug("No pixels to add");
                SetMaps(mapTexture);
                yield break;
            }

            List<string>? customColors = new();
            if (customPlayerColors.Value.Length > 0) customColors = customPlayerColors.Value.Split(',').ToList();
            Dictionary<long, Color>? assignedColors = new();
            bool named = customPlayerColors.Value.Contains(":");

            Color32[]? data = mapTexture.GetPixels32();
            foreach (KeyValuePair<int, long> kvp in pixels)
            {
                Color color = Color.clear;
                if (assignedColors.ContainsKey(kvp.Value))
                {
                    color = assignedColors[kvp.Value];
                }
                else if (customColors.Count > 0 && kvp.Value != 0)
                {
                    if (!named)
                    {
                        ColorUtility.TryParseHtmlString(customColors[0], out color);
                        if (color != Color.clear)
                        {
                            assignedColors[kvp.Value] = color;
                            customColors.RemoveAt(0);
                        }
                    }
                    else
                    {
                        string? pair = customColors.Find(s =>
                            s.StartsWith(Player.GetPlayer(kvp.Value)?.GetPlayerName() + ":"));
                        if (pair is { Length: > 0 })
                            ColorUtility.TryParseHtmlString(pair.Split(':')[1], out color);
                    }
                }

                if (color == Color.clear)
                    GetUserColor(kvp.Value, out color);
                data[kvp.Key] = color;
                /*
                for (int i = 0; i < data.Length; i++)
                {
                    if (Vector2.Distance(new Vector2(i % mapTexture.width, i / mapTexture.width), new Vector2(kvp.Key % mapTexture.width, kvp.Key / mapTexture.width)) < 10)
                        data[i] = kvp.Value;
                }
                */
                //OdinQOLplugin.QOLLogger.LogDebug($"pixel coords {kvp.Key % mapTexture.width},{kvp.Key / mapTexture.width}");
            }

            Texture2D? tempTexture = new(mapTexture.width, mapTexture.height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp
            };
            tempTexture.SetPixels32(data);
            tempTexture.Apply();

            SetMaps(tempTexture);

            OdinQOLplugin.QOLLogger.LogDebug($"Added {pixels.Count} pixels");
        }

        private static void GetUserColor(long id, out Color color)
        {
            color = id == 0 ? unownedBuildingColor.Value :
                id == Player.m_localPlayer.GetPlayerID() ? personalBuildingColor.Value : otherBuildingColor.Value;
        }

        private static void SetMaps(Texture2D texture)
        {
            Minimap.instance.m_mapImageSmall.material.SetTexture("_MainTex", texture);
            Minimap.instance.m_mapImageLarge.material.SetTexture("_MainTex", texture);
            if (Minimap.instance.m_smallRoot.activeSelf)
            {
                Minimap.instance.m_smallRoot.SetActive(false);
                Minimap.instance.m_smallRoot.SetActive(true);
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.GenerateWorldMap))]
        private static class GenerateWorldMap_Patch
        {
            private static void Postfix(Texture2D ___m_mapTexture)
            {
                if (!OdinQOLplugin.modEnabled.Value) return;
                if (!MapDetailOn.Value) return;
                Color32[]? data = ___m_mapTexture.GetPixels32();

                mapTexture = new Texture2D(___m_mapTexture.width, ___m_mapTexture.height, TextureFormat.RGBA32, false);
                mapTexture.wrapMode = TextureWrapMode.Clamp;
                mapTexture.SetPixels32(data);
                mapTexture.Apply();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
        private static class Player_PlacePiece_Patch
        {
            private static void Postfix(bool __result)
            {
                if (!OdinQOLplugin.modEnabled.Value || !__result)
                    return;
                if (MapDetailOn.Value)
                    OdinQOLplugin.context.StartCoroutine(UpdateMap(true));
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.RemovePiece))]
        private static class Player_RemovePiece_Patch
        {
            private static void Postfix(bool __result)
            {
                if (!OdinQOLplugin.modEnabled.Value || !__result)
                    return;
                if (MapDetailOn.Value)
                    OdinQOLplugin.context.StartCoroutine(UpdateMap(true));
            }
        }
    }
}