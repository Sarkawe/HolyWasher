using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;


namespace HolyWasher
{

    // Adds a washtub and washing machine to clean tainted clothes.  Does not repair apparel.
    // If desired, in a future version, it would be dead simple to add some risk of damage or some minimum condition requirement.
    // We could also make it so clothes which don't meet our standards could be damaged further or torn to shreds (producing a small quantity of raw material).

    // But for now, we'll just untaint the apparel.
    // RimWorld can handle all the jobs and progress bars and item hauling and stuff.
    // We provide XML to add new research projects, recipes, and buildings, then patch a workGiver to handle logistics.
    // The only thing we need to handle in C# is our unusual processing of ingredients into products.
    // Actually, since ingredients get consumed, we'll just duplicate our item as an untainted product, then allow the original to be destroyed.

    // Strategy: Postfix GenRecipe.MakeRecipeProducts
    // Since our RecipeDef has no products or specialProducts, the vanilla method will not return any products.
    // So we'll postfix it, check for our RecipeDef, and create a new item that matches the old one (but isn't tainted).
    // The original item will be consumed by ConsumeIngredients in the next step of FinishRecipeAndStartStoringProduct.
    // This is the same thing that happens with any other recipe: ingredients are consumed immediately after the product is spawned.

    // NOTE: The wonderful TD Enhancement Pack has an option that introduces some color varieties for newly spawned apparel.
    //       If enabled, this option causes some color changes after washing since we're actually making newapparel.
    //       So we add a custom property that we can use to mark Washed clothes, then patch TD to ignore those.

    // TO DO: Colonists always drop the washed apparel instead of delivering it to the stockpile designated by the bill.
    //        Verify balance of overall and relative work speeds
    //        Double-check language and possibly get a new Polish translation

    [StaticConstructorOnStartup]
    public static class HarmonyPatches
    {
        static HarmonyPatches()
        {
            // The Harmony_id is only used by other patches that might want to load before/after this one
            Harmony harmonyInstance = new Harmony(id: "RimWorld.OkraDonkey.HolyWasher.main");
            harmonyInstance.PatchAll();
        }
    }

    [HarmonyPatch]
    static class Patch_TDPack_Variate
    {
        static MethodBase target;
        static bool Prepare()
        {
            var mod = LoadedModManager.RunningMods.FirstOrDefault(m => m.PackageId == "uuugggg.tdpack");
            if (mod == null)
            {
                Log.Warning("HolyWasher did NOT find uuugggg.tdpack");
                return false;
            }
            var type = mod.assemblies.loadedAssemblies.FirstOrDefault(a => a.GetName().Name == "TD_Enhancement_Pack").GetType("TD_Enhancement_Pack.ColorVariation");
            if (type == null)
            {
                Log.Warning("HolyWasher: TD patch failed. ColorVariation class not found!");
                return false;
            }
            target = AccessTools.DeclaredMethod(type, "Variate");
            if (target == null)
            {
                Log.Warning("HolyWasher: TD patch failed. Variate method not found!");
                return false;
            }
            // We could check to see if TD has Color Variations enabled, but the cost to just patch it anyway is small.
            Log.Message("HolyWasher: TD patched.");
            return true;
        }
        static MethodBase TargetMethod()
        {
            return target;
        }
        //static IEnumerable<Thing> Prefix(IEnumerable<Thing> things)
        static bool Prefix(ref IEnumerable<Thing> __result, IEnumerable<Thing> things)
        {
            foreach (Thing t in things)
            {
                if (t.def.GetModExtension<LaundryTracker>()?.wasWashed ?? true)
                {
                    // Allow Variate to continue passing along the "things" but don't actually do anything to them.
                    __result = things;
                    return false;
                }
                else
                {
                    // Nothing changed; allow Variate to slightly alter color of new apparel
                    return true;
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(GenRecipe), "MakeRecipeProducts")]
    public static class HolyWasher
    {
        [HarmonyPostfix]
        public static void PostFix(ref IEnumerable<Thing> __result, RecipeDef recipeDef, List<Thing> ingredients)
        {
            // Prepare a new list to collect products
            List<Thing> products = new List<Thing>();
            // Add any products from the existing method.  There will be none if the recipe was HolyWashApparel.
            foreach (Thing thing in __result)
            {
                products.Add(thing);
            }

            if (recipeDef.defName == "HolyWashApparel")
            {
                // The "ingredient" is the tainted apparel.
                Thing dirtyApparel = ingredients[0];

                // Once we know what it's made of, we can create our replacement
                ThingDef dirtyStuff = dirtyApparel.Stuff;
                Thing cleanApparel = ThingMaker.MakeThing(dirtyApparel.def, dirtyStuff);

                // Set the new item's quality to match the old
                dirtyApparel.TryGetQuality(out QualityCategory dirtyQuality);
                CompQuality compQuality = cleanApparel.TryGetComp<CompQuality>();
                if (compQuality != null)
                {
                    compQuality.SetQuality(dirtyQuality, ArtGenerationContext.Outsider);
                }

                // Set the hit points to match
                cleanApparel.HitPoints = dirtyApparel.HitPoints;

                // Check for stuffed apparels, and match the color
                if (dirtyApparel.Stuff != null)
                {
                    cleanApparel.Stuff.stuffProps.color = dirtyApparel.Stuff.stuffProps.color;
                }
                cleanApparel.SetColor(dirtyApparel.DrawColor);

                // Mark apparel as washed so TD and other mods can know to not alter it upon spawning
                cleanApparel.def.GetModExtension<LaundryTracker>().wasWashed = true;

                // No need to untaint it since it's new.
                // Return the new apparel to the bill so it can be delivered or dropped.
                products.Add(cleanApparel);
            }

            // give the updated or original product list back to MakeRecipeProducts.
            IEnumerable<Thing> output = products;
            __result = output;

        }
    }

    public class LaundryTracker : DefModExtension
    {
        public bool wasWashed = false;
    }
}
