using BepInEx;
using HarmonyLib;
using UnityEngine;


[BepInPlugin("minusgix.companion.invincible", "Invincible Companion", "1.0.0.0")]
public class Plugin : BaseUnityPlugin
{
    private void Start()
    {
        var harmony = new Harmony("minusgix.companion.invincible");
        harmony.PatchAll();
    }
}

[HarmonyPatch(typeof(Chara), "Die", new System.Type[] { typeof(Element), typeof(Card), typeof(AttackSource) })]
class CardPatch2
{
    static bool Prefix(Chara __instance, Element e = null, Card origin = null, AttackSource attackSource = AttackSource.None)
    {
        if (__instance.GetInt(CINT.fiamaPet, 0) == 1 && __instance.id != "loytel")
        {
            __instance.hp = 0;
            // Only add fear effect if the card doesn't already have the fear condition
            // This avoids stacking multiple fear effects and gives the pet a chance to fight a bit.
            if (!__instance.Chara.HasCondition<ConFear>())
            {
                __instance.Chara.AddCondition<ConFear>(130 + EClass.rnd(100));
            }
            return false;
        }
        return true;
    }
}