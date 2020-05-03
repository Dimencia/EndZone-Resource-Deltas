using System;
using PatchZone.Hatch;
using PatchZone.Hatch.Utils;
using ResourceDeltas.Services;

using ECS;
using Service.Achievement;
using Service.Building;
using Service.Localization;
using Service.Street;
using Service.UserWorldTasks;
using HarmonyLib;
using UserInterface;
using Globals;
using System.Collections.Generic;
using Service.UI;
using System.Linq;
using UnityEngine;
using Component = UnityEngine.Component;

// So the goal here is just to put something like (+12) next to each resource in the toolbar at the top to indicate its movement
// Basically every 5 seconds or something like that, get the value, compare to last value, and put that value in

namespace ResourceDeltas
{
    public class ResourceDeltas : Singleton<ResourceDeltas>, IPatchZoneMod
    {
        public static IPatchZoneContext Context { get; private set; }
        public static Harmony Harmony;

        public void Init(IPatchZoneContext context)
        {
            Context = context;
            Harmony = new Harmony("Resource Deltas");
        }

        public void OnBeforeGameStart()
        {
            Harmony.PatchAll();
        }
    }

    [HarmonyPatch(typeof(UIResourceOutput), "UpdateResourceOutput")]
    public static class PatchedResourceOutput
    {
        // So we *could* override the command
        // But probably better as a postfix to keep mod compat
        // And all we have to do is add the delta which we can get from PatchedBaseTick
        public static HashSet<ResourceComponent.ResourceType> initializedTypes = new HashSet<ResourceComponent.ResourceType>();

        public static void Postfix(UIResourceOutput __instance, int amount)
        {
            // I don't think we have to check, they've definitely overwritten whatever we had there previously
            if (PatchedBaseTick.rollingAverageAmounts.ContainsKey(__instance.resourceType))
            {
                if(!initializedTypes.Contains(__instance.resourceType)) 
                {
                    //ServiceMapper.optionsService.SetUIScaleFactorIndex(25); // IDK?  
                    // Setup the tmp object
                    // Default font size is 14 it seems
                    __instance.amountOutput.fontSize *= 0.8f;
                    /*
                    if (__instance.resourceType == ResourceComponent.ResourceType.Food || __instance.resourceType == ResourceComponent.ResourceType.Water)
                    {
                        __instance.amountOutput.transform.parent.parent.parent.parent.localScale = new Vector3(1.2f, 1, 1);
                        // Scale all text back
                        var tmps = __instance.amountOutput.transform.parent.parent.parent.parent.GetComponentsInChildren<TMPro.TextMeshProUGUI>();
                        foreach (var t in tmps)
                        {
                            t.rectTransform.localScale = new Vector3(1 / 1.2f, 1, 1);
                            //t.fontSize *= 1.5f;
                        }
                    }
                    else
                    {
                        __instance.amountOutput.transform.parent.parent.parent.parent.parent.localScale = new Vector3(1.2f, 1, 1);
                        // Scale all text back
                        var tmps = __instance.amountOutput.transform.parent.parent.parent.parent.parent.GetComponentsInChildren<TMPro.TextMeshProUGUI>();
                        foreach (var t in tmps)
                        {
                            t.rectTransform.localScale = new Vector3(1 / 1.2f, 1, 1);
                            //t.fontSize *= 1.5f;
                        }
                    }
                    */
                    
                     // An extra parent for everything else?

                    //__instance.amountOutput.fontSizeMax = 12; // But man autosizing with 14 as max goes nuts.  This doesn't seem to do anything
                    __instance.amountOutput.enableWordWrapping = false; 
                    //__instance.amountOutput.enableAutoSizing = true;
                    //__instance.amountOutput.rectTransform.localScale = new Vector3(0.75f, 0.65f, 0.75f); // The rect is too big for autosize, and too tall

                    // So, scaling it seems to also move it, like it scales anchored at center.  Annoyingly.


                    // Move it a few pixels left because it's got way too much padding there
                    //__instance.amountOutput.rectTransform.localPosition = new Vector3(__instance.amountOutput.rectTransform.localPosition.x - 20, __instance.amountOutput.rectTransform.localPosition.y, __instance.amountOutput.rectTransform.localPosition.z);
                    // Can't seem to do that to: gameobject, transform, recttransform
                    // Let's try the gameobject's parent
                    //__instance.amountOutput.transform.parent.localPosition = new Vector3(__instance.amountOutput.transform.parent.localPosition.x - 20, __instance.amountOutput.transform.parent.localPosition.y, __instance.amountOutput.transform.parent.localPosition.z);
                    // I think that's the whole bar.  It must be in a flowlayout or something, I'd have to size down some of the resource icons
                    //var components = __instance.amountOutput.transform.parent.GetComponents<Component>();
                    //ResourceDeltas.Context.Log.Log(__instance.resourceType.ToString());
                    //foreach (var c in components)
                    //    ResourceDeltas.Context.Log.Log(c.GetType() + " - " + c.name);
                    //
                    //components = __instance.amountOutput.transform.parent.parent.GetComponents<Component>();
                    //ResourceDeltas.Context.Log.Log(__instance.resourceType.ToString());
                    //foreach (var c in components)
                    //    ResourceDeltas.Context.Log.Log(c.GetType() + " - " + c.name);
                    // So maybe not, I don't see what it could be here I don't think.  Let's check the parents

                    // There's a UnityEngine.UI.Image in the parent, and also one in the parent's parent, and horizontallayoutgroups in both
                    // But I think the one in the parent is the one we're after, if we do that.


                    initializedTypes.Add(__instance.resourceType);
                }
                
                int average = (int)Math.Round(PatchedBaseTick.rollingAverageAmounts[__instance.resourceType].Average() * 60);
                if (average > 0)
                {
                    __instance.amountOutput.text = $"{amount}(+{average})";
                }
                else
                {
                    __instance.amountOutput.text = $"{amount}({average})";
                }
            }

        }
    }


    // TODO: This happens even when the game is paused.  We need to check for that
    // And I guess probably check for game speed in that case and do things based on that

    [HarmonyPatch(typeof(UIBase), "Tick")]
    public static class PatchedBaseTick
    {
        public static Dictionary<ResourceComponent.ResourceType, List<float>> rollingAverageAmounts = new Dictionary<ResourceComponent.ResourceType, List<float>>();
        public static Dictionary<ResourceComponent.ResourceType, float> lastAmount = new Dictionary<ResourceComponent.ResourceType, float>();
        public static Dictionary<ResourceComponent.ResourceType, int> numTicks = new Dictionary<ResourceComponent.ResourceType, int>();

        public static void Postfix(UIBase __instance)
        {
            if (__instance is UIResourceOutput output)
            {
                if (numTicks.ContainsKey(output.resourceType))
                    numTicks[output.resourceType]++;
                else
                    numTicks[output.resourceType] = 0;
                if (numTicks[output.resourceType] % 20 == 0) // About 60 ticks per second.  This is about 4/second
                {
                    numTicks[output.resourceType] = 0;
                    // Get the amount from globalInventory somehow
                    var amount = ServiceMapper.globalInventoryService.GetAmount(output.resourceType);
                    // Add it to our rollingAverages
                    if (!rollingAverageAmounts.ContainsKey(output.resourceType))
                    {
                        rollingAverageAmounts[output.resourceType] = new List<float>();
                    }
                    if (!lastAmount.ContainsKey(output.resourceType))
                    {
                        lastAmount[output.resourceType] = amount;
                    }

                    //if(output.resourceType == ResourceComponent.ResourceType.Wood)
                    //    ResourceDeltas.Context.Log.Log("Delta: " + (amount - lastAmount[output.resourceType]));

                    rollingAverageAmounts[output.resourceType].Add(amount - lastAmount[output.resourceType]);
                    // Remove old amounts
                    while (rollingAverageAmounts[output.resourceType].Count > 100) // Keep last 2ish seconds?  Let's try way more. 20 seconds.
                        rollingAverageAmounts[output.resourceType].RemoveAt(0);
                    // That's it.  Let the other method calculate the rest



                    // Oh and, well.  Force it to update.  If it's a non-category item... which.... I have no idea how to find out
                    // Because category items give us 0's for inventory amount constantly.  Damnit.  
                    float maxCapPercentage = Mathf.Clamp01(1f - (float)ServiceMapper.globalInventoryService.AmountNeededTillMaxCap(output.resourceType) / (float)ServiceMapper.globalInventoryService.GetMaxCap(output.resourceType));
                    output.UpdateResourceOutput(amount, maxCapPercentage);


                    lastAmount[output.resourceType] = amount;
                }

            }
        }
    }
}
