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
        internal static ConfigEntry<string> VisualCrop;

        private static readonly MethodInfo MemberwiseClone =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo NameCache = typeof(CraftDefinition).GetField("_cached_name", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private readonly HashSet<string> _dumped = new HashSet<string>();
        private float _nextScan;

        // Phase 2 (temporary): describe how the zombie bed and the hemp field draw their plants.
        private void Update()
        {
            if (Time.realtimeSinceStartup < _nextScan) return;
            _nextScan = Time.realtimeSinceStartup + 5f;
            try
            {
                foreach (var d in FindObjectsOfType<SmartDrawer>())
                {
                    var wgo = d.GetComponentInParent<WorldGameObject>();
                    var id = wgo != null ? wgo.obj_id : null;
                    if (id == null || !(id.StartsWith("zombie_garden_desk") || id.Contains("cannabis"))) continue;
                    DumpOnce("SmartDrawer", id, d.gameObject);
                    foreach (var a in d.smart_drawer_atoms) DumpAtom(id, a);
                }
                foreach (var g in FindObjectsOfType<GardenCustomDrawer>())
                {
                    var wgo = g.GetComponentInParent<WorldGameObject>();
                    var id = wgo != null ? wgo.obj_id : null;
                    if (id == null || !id.Contains("cannabis")) continue;
                    DumpOnce("GardenCustomDrawer", id, g.gameObject);
                    foreach (var o in g.garden_stages) if (o != null) DumpOnce("stage", id + "/" + o.name, o);
                }
            }
            catch (Exception e) { Logger.LogError("scan failed: " + e); }
        }

        private void DumpAtom(string id, SmartDrawerAtom a)
        {
            var key = "atom:" + id + ":" + (a.obj != null ? a.obj.name : "null");
            if (!_dumped.Add(key)) return;
            var sb = new StringBuilder("ATOM " + id + " obj=" + (a.obj != null ? a.obj.name : "null") + " condition=" + a.condition
                + " int=" + a.int_value + "/" + a.int_value_2 + " str=" + a.string_value + " f=" + a.float_value + "/" + a.float_value_2 + "\n");
            if (a.obj != null) Tree(a.obj.transform, 1, sb);
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "visual-dump.txt"), sb.ToString());
        }

        private void DumpOnce(string kind, string id, GameObject go)
        {
            if (!_dumped.Add(kind + ":" + id)) return;
            var sb = new StringBuilder(kind.ToUpper() + " " + id + "\n");
            Tree(go.transform, 1, sb);
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "visual-dump.txt"), sb.ToString());
        }

        private static void Tree(Transform t, int depth, StringBuilder sb)
        {
            if (depth > 6) return;
            foreach (Transform c in t)
            {
                var sr = c.GetComponent<SpriteRenderer>();
                var comps = string.Join(",", c.GetComponents<Component>().Where(x => x != null).Select(x => x.GetType().Name).ToArray());
                sb.AppendLine(new string(' ', depth * 2) + c.name + " active=" + c.gameObject.activeSelf + " pos=" + c.localPosition
                    + " comps=[" + comps + "]" + (sr != null ? " sprite=" + (sr.sprite != null ? sr.sprite.name : "null") : ""));
                Tree(c, depth + 1, sb);
            }
        }

        private void Awake()
        {
            Instance = this;
            VisualCrop = Config.Bind("General", "Visual Crop", "carrot",
                "Which vanilla crop sprite the zombie bed shows for added crops (wheat, cabbage, carrot, beet). " +
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

                    foreach (var template in all.Where(c => c.id == "garden_" + TemplateCrop + "_grow_desk_planting").ToList())
                    {
                        var craft = (CraftDefinition)MemberwiseClone.Invoke(template, null);
                        craft.id = "garden_" + crop + "_grow_desk_planting";
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
