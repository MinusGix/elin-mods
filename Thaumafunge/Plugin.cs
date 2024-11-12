using BepInEx;
using HarmonyLib;
using UnityEngine;


[BepInPlugin("minusgix.thaumafunge", "Thaumafunge", "1.0.0.0")]
public class Plugin : BaseUnityPlugin
{
    private void Start()
    {
        var harmony = new Harmony("minusgix.thaumafunge");
        harmony.PatchAll();
    }
}