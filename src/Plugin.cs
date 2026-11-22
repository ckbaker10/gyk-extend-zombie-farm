using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace ExtendZombieFarm
{
    [BepInPlugin("ckbaker10.gyk.extendzombiefarm", "Extend Zombie Farm", "1.0.0")]
    [HarmonyPatch]
    public class Plugin : BaseUnityPlugin
    {
        // Vanilla zombie crop whose crafts are used as the template for missing crops.
        private const string TemplateCrop = "onion";

        internal static Plugin Instance;
        internal static ConfigEntry<string> VisualCrop;

        private static readonly MethodInfo MemberwiseClone =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo NameCache = typeof(CraftDefinition).GetField("_cached_name", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private void Awake()
        {
            Instance = this;
            VisualCrop = Config.Bind("General", "Visual Crop", "onion",
                "Which vanilla crop sprite the zombie bed shows for added crops (wheat, cabbage, carrot, beet, lentils, pumpkin, onion). " +
                "Use the crop's own name (e.g. 'hamp') to show nothing if the game has no sprite for it.");
            new Harmony("ckbaker10.gyk.extendzombiefarm").PatchAll(typeof(Plugin).Assembly);
        }

        [HarmonyPatch(typeof(GameBalance), "LoadGameBalance")]
        [HarmonyPostfix]
        private static void AddZombieCrafts()
        {
            try
            {
                var all = GameBalance.me.craft_data;

                // Player-bed crops without trellis: planting craft runs on the empty bed itself.
                var plain = all.Where(c => c.craft_in.Count == 1 && c.craft_in[0] == "garden_empty"
                                           && c.id.StartsWith("garden_") && c.needs.Count == 1
                                           && Regex.IsMatch(c.id, @"^garden_[a-z]+_planting_\d$"))
                    .GroupBy(c => c.id.Split('_')[1]).ToList();

                var added = 0;
                foreach (var group in plain)
                {
                    var crop = group.Key;
                    if (crop == TemplateCrop) continue;
                    if (all.Any(c => c.id.StartsWith("garden_" + crop + "_grow_desk_planting"))) continue;

                    var seedBase = group.First().needs[0].id.Split(':')[0]; // e.g. hamp_seed
                    var cropBase = seedBase.Replace("_seed", "_crop");
                    var visual = VisualCrop.Value;

                    foreach (var template in all.Where(c => c.id.StartsWith("garden_" + TemplateCrop + "_grow_desk_planting")).ToList())
                    {
                        var craft = (CraftDefinition)MemberwiseClone.Invoke(template, null);
                        craft.id = template.id.Replace("garden_" + TemplateCrop, "garden_" + crop);
                        craft.craft_in = new List<string>(template.craft_in);
                        craft.needs = template.needs.Select(i => Remap(i, seedBase, cropBase)).ToList();
                        craft.output = template.output.Select(i => Remap(i, seedBase, cropBase)).ToList();
                        craft.output_res_wgo = Rekey(template.output_res_wgo, visual);
                        craft.set_when_cancelled = template.set_when_cancelled;
                        craft.output_set_res_wgo = Rekey(template.output_set_res_wgo, visual);
                        craft.icon = "i_" + cropBase;
                        NameCache?.SetValue(craft, null);

                        all.Add(craft);
                        GameBalance.me.AddDataUniversal(craft);
                        GameBalance.me.AddData(craft);
                        added++;
                        Instance.Logger.LogInfo("  " + craft.id + " in=[" + string.Join(",", craft.craft_in.ToArray()) + "] needs=[" +
                            string.Join(",", craft.needs.Select(n => n.id + "x" + n.value).ToArray()) + "] out=[" +
                            string.Join(",", craft.output.Select(n => n.id + " " + n.min_value + ".." + n.max_value + " ch=" + n.self_chance).ToArray()) + "]");
                    }
                    Instance.Logger.LogInfo("Zombie bed: added crop '" + crop + "' (" + seedBase + ")");
                }
                Instance.Logger.LogInfo("Added " + added + " zombie garden craft(s)");
            }
            catch (Exception e)
            {
                Instance.Logger.LogError("Failed to add zombie crafts: " + e);
            }
        }

        private static Item Remap(Item src, string seedBase, string cropBase)
        {
            var item = new Item(src);
            item.id = src.id.Replace(TemplateCrop + "_seed", seedBase).Replace(TemplateCrop + "_crop", cropBase);
            return item;
        }

        // Template sets/clears the param named after the crop shown on the bed.
        private static GameRes Rekey(GameRes src, string visual)
        {
            var copy = src.Clone();
            if (visual == TemplateCrop) return copy; // keep vanilla onion param/sprite
            var v = src.Get(TemplateCrop, 0f);
            if (src.Has(TemplateCrop))
            {
                copy.Set(TemplateCrop, 0f);
                copy.RemoveZeroValues();
                copy.Set(visual, v);
            }
            return copy;
        }
    }
}
