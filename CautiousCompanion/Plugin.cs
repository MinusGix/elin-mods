using BepInEx;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using BepInEx.Logging;
using System.Linq;

public enum HealthCaution
{
    /// <summary>
    /// 1/5th of max health.
    /// </summary>
    Default,
    /// <summary>
    /// 1/4th of max health.
    /// </summary>
    Fourth,
    /// <summary>
    /// 1/3rd of max health.
    /// </summary>
    Third,
    /// <summary>
    /// 1/2 of max health.
    /// </summary>
    Half,
}

public enum DistanceFalloffType
{
    None,
    Sigmoid,
    Alone,
}

public class FearModifier(HealthCaution healthCaution, DistanceFalloffType distanceFalloffType)
{
    /// <summary>
    /// If true, the card will allow fearing at half of its max health rather than a fifth.
    /// </summary>
    public HealthCaution healthCaution = healthCaution;
    public DistanceFalloffType distanceFalloffType = distanceFalloffType;

    public static FearModifier Default = new(HealthCaution.Third, DistanceFalloffType.Sigmoid);

    public float HealthDiv()
    {
        return healthCaution switch
        {
            HealthCaution.Default => 5.0f,
            HealthCaution.Fourth => 4.0f,
            HealthCaution.Third => 3.0f,
            HealthCaution.Half => 2.0f,
            _ => 5.0f,
        };
    }
}

// TODO: Decide whether we should weaken the default from health 1/5th to 1/3 or 1/2.
// TODO: Save fear modifiers if they've been edited.
// TODO: add a 'Flee' option to characters in your party.
// TODO: We should have a separate condition for Fear which is more about running away towards the player?
[BepInPlugin("minusgix.companion.cautious", "Cautious Companion", "1.0.0.0")]
public class Plugin : BaseUnityPlugin
{
    public static Dictionary<int, FearModifier> fearModifiers = new Dictionary<int, FearModifier>();

    public static FearModifier GetFearModifier(Chara chara)
    {
        if (!fearModifiers.ContainsKey(chara.uid))
        {
            if (chara.IsPCParty)
            {
                return FearModifier.Default;
            }
            return null;
        }
        return fearModifiers[chara.uid];
    }

    public static void SetFearModifier(Chara chara, FearModifier modifier)
    {
        fearModifiers[chara.uid] = modifier;
    }

    public static void RemoveFearModifier(Chara chara)
    {
        if (fearModifiers.ContainsKey(chara.uid))
            fearModifiers.Remove(chara.uid);
    }

    private void Start()
    {
        var harmony = new Harmony("minusgix.companion.cautious");
        harmony.PatchAll();
    }

    public static float DistanceFloat(Point origin, Point target)
    {
        return Mathf.Sqrt((origin.x - target.x) * (origin.x - target.x) + (origin.z - target.z) * (origin.z - target.z));
    }

    // Just for reference, the default fear check.
    // public static bool DefaultFear(Card card, int dmg)
    // {
    //     // I presume the 423 value is the negateFear enc, as that is the only matching value.
    //     return card.hp < card.MaxHP / 5 && card.Evalue(ENC.negateFear) <= 0 && dmg * 100 / card.MaxHP + 10 > EClass.rnd(card.IsPowerful ? 400 : 150);
    // }

    public static float DistanceSigmoidFalloff(float distance)
    {
        return 1.0f + (0.6f / (1.0f + Mathf.Exp(-(distance - 5.0f)))) + (0.4f / (1.0f + Mathf.Exp(-(distance - 11.0f))));
    }

    public static float DistanceAloneRandShift(float distance)
    {
        return Mathf.Exp(-Mathf.Max(0, distance - 3.0f) / 6.0f);
    }

    public static bool AdjustedFear(Card card, FearModifier fearModifier, int dmg, float distance)
    {
        float healthThreshold = card.MaxHP / fearModifier.HealthDiv();
        switch (fearModifier.distanceFalloffType)
        {
            case DistanceFalloffType.Sigmoid:
                return AdjustedFearGeneral(card, healthThreshold, DistanceSigmoidFalloff(distance), 1.0f, dmg);
            case DistanceFalloffType.Alone:
                return AdjustedFearGeneral(card, healthThreshold, 1.0f, DistanceAloneRandShift(distance), dmg);
            default:
                // Don't use the default fear check because that would double count.
                return false;
        }
    }

    static bool AdjustedFearGeneral(Card card, float healthThreshold, float distanceFalloff, float randMul, int dmg)
    {
        return card.hp < healthThreshold && card.Evalue(ENC.negateFear) <= 0 && (dmg * 100.0f / card.MaxHP + 10) * distanceFalloff > EClass.rndf((card.IsPowerful ? 400.0f : 150.0f) * randMul);
    }

    // Not needed atm since we aren't localizing.
    // public static void TempTalkTopic(DramaCustomSequence inst, Chara c, string idTopic, string idJump)
    // {
    //     inst._TempTalk("tg", inst.GetTopic(c, idTopic), idJump);
    // }

    public static bool FindTalkAfterStep(DramaCustomSequence drama, string stepName)
    {
        // Find the index of the step
        int stepIndex = drama.events.FindIndex(e => e.step == stepName);

        if (stepIndex == -1) return false;

        // Find the next talk event after this step
        var talkEvent = drama.events
            .Skip(stepIndex)
            .OfType<DramaEventTalk>()
            .FirstOrDefault();

        if (talkEvent != null)
        {
            drama.manager.lastTalk = talkEvent;
            return true;
        }

        return false;
    }
}

[HarmonyPatch(typeof(Card), "DamageHP", [typeof(int), typeof(int), typeof(int), typeof(AttackSource), typeof(Card), typeof(bool)])]
class InvokeFearPatch
{
    static void Postfix(Card __instance, int dmg, int ele, int eleP, AttackSource attackSource, Card origin, bool showEffect)
    {
        if (!__instance.IsPCParty || __instance.HasCondition<ConFear>())
            return;

        var fearMod = Plugin.GetFearModifier(__instance.Chara);
        if (fearMod != null)
        {
            float distance = Plugin.DistanceFloat(__instance.pos, EClass.pc.pos);
            if (Plugin.AdjustedFear(__instance, fearMod, dmg, distance))
            {
                // WidgetFeed.Instance?.Nerun($"Adding fear based on modified formula for {__instance.Name}");
                // System.Console.WriteLine($"Adding fear based on modified formula for {__instance.Name}");
                // System.Console.WriteLine($"Distance: {distance}");
                // System.Console.WriteLine($"FearMod: {fearMod.healthCaution} {fearMod.distanceFalloffType}");
                __instance.Chara.AddCondition<ConFear>(100 + EClass.rnd(100));
            }
        }
    }

}

[HarmonyPatch(typeof(DramaCustomSequence), "Build")]
class DramaCustomSequenceBuildPatch
{
    static void Postfix(DramaCustomSequence __instance, Chara c)
    {
        if (c.IsHomeMember() && c.IsPCParty && c.memberType != FactionMemberType.Livestock)
        {
            if (!Plugin.FindTalkAfterStep(__instance, "Resident"))
            {
                // TODO: better logging
                System.Console.WriteLine("Warning: Failed to find talk event for 'Resident'");
                return;
            }
            // TODO: It would be good to translate this dialogue. Unfortunately I don't know how to do that.

            // This is given a unique id so that there's no conflict with other mods.
            __instance.Choice2("Flee Style", "_MinusGixFleeStyle");

            __instance.Step("_MinusGixFleeStyle");
            __instance.Method(delegate
            {
                FearModifier fm = Plugin.GetFearModifier(c);
                __instance._TempTalk("tg", $"How do you want to flee?\nCurrent health threshold: {fm.healthCaution}\nCurrent distance rule: {fm.distanceFalloffType}", __instance.StepEnd);
                __instance.Choice("Health", delegate
                {
                    FearModifier fm = Plugin.GetFearModifier(c);
                    __instance._TempTalk("tg", $"Current health threshold: {fm.healthCaution}", __instance.StepEnd);

                    __instance.Choice("1/5th of max health (vanilla)", delegate
                    {
                        var newMod = new FearModifier(HealthCaution.Default, fm.distanceFalloffType);
                        Plugin.SetFearModifier(c, newMod);
                        __instance._TempTalk("tg", "I'll be more brave now.", __instance.StepDefault);
                    });
                    __instance.Choice("1/4th of max health", delegate
                    {
                        var newMod = new FearModifier(HealthCaution.Fourth, fm.distanceFalloffType);
                        Plugin.SetFearModifier(c, newMod);
                        __instance._TempTalk("tg", "I'll be somewhat cautious.", __instance.StepDefault);
                    });
                    __instance.Choice("1/3rd of max health", delegate
                    {
                        var newMod = new FearModifier(HealthCaution.Third, fm.distanceFalloffType);
                        Plugin.SetFearModifier(c, newMod);
                        __instance._TempTalk("tg", "I'll be quite cautious.", __instance.StepDefault);
                    });
                    __instance.Choice("1/2 of max health", delegate
                    {
                        var newMod = new FearModifier(HealthCaution.Half, fm.distanceFalloffType);
                        Plugin.SetFearModifier(c, newMod);
                        __instance._TempTalk("tg", "I'll be very cautious. My health is valuable!", __instance.StepDefault);
                    });
                    __instance.Choice("Back", __instance.StepDefault);
                });

                __instance.Choice("Distance From Player", delegate
                {
                    FearModifier fm = Plugin.GetFearModifier(c);
                    __instance._TempTalk("tg", $"Current distance rule: {fm.distanceFalloffType}. This makes fleeing more likely when you are far away.", __instance.StepEnd);

                    __instance.Choice("No distance effect (vanilla)", delegate
                    {
                        var newMod = new FearModifier(fm.healthCaution, DistanceFalloffType.None);
                        Plugin.SetFearModifier(c, newMod);
                        __instance._TempTalk("tg", "I won't consider distance when fleeing.", __instance.StepDefault);
                    });
                    __instance.Choice("Gradual distance", delegate
                    {
                        var newMod = new FearModifier(fm.healthCaution, DistanceFalloffType.Sigmoid);
                        Plugin.SetFearModifier(c, newMod);
                        __instance._TempTalk("tg", "I'll be more likely to flee when far from you.", __instance.StepDefault);
                    });
                    __instance.Choice("Flee when alone (Scaredy-cat)", delegate
                    {
                        var newMod = new FearModifier(fm.healthCaution, DistanceFalloffType.Alone);
                        Plugin.SetFearModifier(c, newMod);
                        __instance._TempTalk("tg", "I'll be **very** likely to flee when isolated.", __instance.StepDefault);
                    });
                    __instance.Choice("Back", __instance.StepDefault);
                });

                __instance.Choice("Back", __instance.StepDefault);
            });

            __instance.Goto(__instance.StepDefault);
        }
    }
}

[HarmonyPatch(typeof(GameIO))]
class GameIOSavePatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(GameIO.SaveGame))]
    public static void GameIO_SaveGame_Post()
    {
        try
        {
            GameIO.SaveFile(GameIO.pathCurrentSave + "/mods/MinusGix_CautiousCompanion.txt", Plugin.fearModifiers);
        }
        catch (System.Exception e)
        {
            System.Console.WriteLine($"Error: Failed to save cautious companion fear modifiers: {e}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(GameIO.LoadGame))]
    public static void GameIO_LoadGame_Post(string id)
    {
        try
        {
            var loaded = GameIO.LoadFile<Dictionary<int, FearModifier>>(GameIO.pathCurrentSave + "/mods/MinusGix_CautiousCompanion.txt");
            if (loaded != null)
            {
                Plugin.fearModifiers = loaded;
            }
        }
        catch (System.Exception e)
        {
            System.Console.WriteLine($"Error: Failed to load cautious companion fear modifiers: {e}");
            // Ensure we have a valid dictionary even if load fails
            Plugin.fearModifiers = new Dictionary<int, FearModifier>();
        }
    }
}