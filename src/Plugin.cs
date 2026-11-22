using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;
using HarmonyLib;

namespace ExtendZombieFarm
{
    [BepInPlugin("ckbaker10.gyk.extendzombiefarm", "Extend Zombie Farm", "1.0.0")]
    [HarmonyPatch]
    public class Plugin : BaseUnityPlugin
    {
        // Vanilla zombie crop whose crafts are used as the template for missing crops.
        private const string TemplateCrop = "carrot";

        internal static Plugin Instance;
        internal static readonly Dictionary<string, string> VisualParams = new Dictionary<string, string>();
        internal static ConfigEntry<string> VisualCrop;

        private static readonly MethodInfo MemberwiseClone =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo NameCache = typeof(CraftDefinition).GetField("_cached_name", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private void Awake()
        {
            Instance = this;
            VisualCrop = Config.Bind("General", "Visual Crop", "",
                "Advanced: leave empty. Param name used to draw added crops on the zombie bed; empty = the crop's own name (hamp).");
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

                    var seedId = group.First().needs[0].id;                  // e.g. hamp_seed:1 (what the player field yields)
                    var ready = GameBalance.me.objs_data.FirstOrDefault(o => o.id == "garden_" + crop + "_ready");
                    var cropId = ready?.drop_items.FirstOrDefault(i => i.id.Contains("_crop"))?.id; // e.g. hamp_crop:1
                    if (cropId == null) { Instance.Logger.LogWarning("No crop drop found for " + crop + ", skipped"); continue; }
                    var cropBase = cropId.Split(':')[0];
                    var visual = string.IsNullOrEmpty(VisualCrop.Value) ? seedId.Split('_')[0] : VisualCrop.Value;
                    VisualParams[crop] = visual;

                    foreach (var template in all.Where(c => c.id == "garden_" + TemplateCrop + "_grow_desk_planting").ToList())
                    {
                        var craft = (CraftDefinition)MemberwiseClone.Invoke(template, null);
                        craft.id = "garden_" + crop + "_grow_desk_planting";
                        craft.craft_in = new List<string>(template.craft_in);
                        craft.needs = template.needs.Select(i => Remap(i, seedId, cropId)).ToList();
                        craft.output = template.output.Select(i => Remap(i, seedId, cropId)).ToList();
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
                    Instance.Logger.LogInfo("Zombie bed: added crop '" + crop + "' (" + seedId + ")");
                }
                Instance.Logger.LogInfo("Added " + added + " zombie garden craft(s)");
            }
            catch (Exception e)
            {
                Instance.Logger.LogError("Failed to add zombie crafts: " + e);
            }
        }

        private static Item Remap(Item src, string seedId, string cropId)
        {
            var item = new Item(src);
            if (src.id.StartsWith(TemplateCrop + "_seed")) item.id = seedId;
            else if (src.id.StartsWith(TemplateCrop + "_crop")) item.id = cropId;
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

        // ---- Visuals: draw added crops on the zombie bed using the normal field's sprites ----

        private static Dictionary<string, Sprite> _sprites;

        private static Sprite FindSprite(string name)
        {
            if (_sprites == null)
            {
                _sprites = new Dictionary<string, Sprite>();
                foreach (var sp in Resources.FindObjectsOfTypeAll<Sprite>())
                    if (sp != null && sp.name.StartsWith("garden_") && !_sprites.ContainsKey(sp.name)) _sprites[sp.name] = sp;
            }
            Sprite r;
            return _sprites.TryGetValue(name, out r) ? r : null;
        }

        [HarmonyPatch(typeof(SmartDrawer), "Redraw")]
        [HarmonyPrefix]
        private static void SmartDrawer_Redraw(SmartDrawer __instance)
        {
            try
            {
                if (__instance.smart_drawer_atoms == null || VisualParams.Count == 0) return;
                var atoms = __instance.smart_drawer_atoms;
                if (!atoms.Any(a => a.string_value == TemplateCrop && a.obj != null)) return;
                foreach (var kv in VisualParams)
                {
                    var param = kv.Value;
                    if (atoms.Any(a => a.string_value == param)) continue;
                    var hemp = kv.Key == "cannabis" ? "hemp" : kv.Key;
                    foreach (var tmpl in atoms.Where(a => a.string_value == TemplateCrop && a.obj != null).ToList())
                    {
                        var go = UnityEngine.Object.Instantiate(tmpl.obj, tmpl.obj.transform.parent);
                        go.name = param;
                        go.transform.localPosition = tmpl.obj.transform.localPosition;
                        var stage = tmpl.obj.transform.parent.name; // st_2 .. st_8
                        foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
                        {
                            var n = sr.sprite != null ? sr.sprite.name : null;
                            if (n == null || !n.StartsWith("garden_" + TemplateCrop + "_")) continue;
                            var tail = n.Substring(("garden_" + TemplateCrop + "_").Length);   // st_4_layer_1_pL
                            var sprite = FindSprite("garden_" + hemp + "_" + tail)
                                         ?? FindSprite("garden_" + hemp + "_" + (tail.EndsWith("_pL") || tail.EndsWith("_pR") ? tail.Substring(0, tail.Length - 3) : tail));
                            if (sprite != null && !(tail.EndsWith("_pR") && FindSprite("garden_" + hemp + "_" + tail) == null))
                            {
                                sr.sprite = sprite;
                            }
                            else
                            {
                                sr.gameObject.SetActive(false); // hemp has no matching sprite for this layer
                            }
                        }
                        var atom = new SmartDrawerAtom
                        {
                            obj = go,
                            condition = tmpl.condition,
                            string_value = param,
                            int_value = tmpl.int_value,
                            int_value_2 = tmpl.int_value_2,
                            float_value = tmpl.float_value,
                            float_value_2 = tmpl.float_value_2,
                            update_period = tmpl.update_period
                        };
                        atoms.Add(atom);
                    }
                    Instance.Logger.LogInfo("Zombie bed visuals added for '" + param + "' on " + __instance.name);
                }
            }
            catch (Exception e)
            {
                Instance.Logger.LogError("Zombie bed visuals failed: " + e);
            }
        }
    }
}
