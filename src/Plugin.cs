using System;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using HarmonyLib;

namespace ExtendZombieFarm
{
    [BepInPlugin("ckbaker10.gyk.extendzombiefarm", "Extend Zombie Farm", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        private static readonly System.Reflection.FieldInfo ExprField =
            typeof(SmartExpression).GetField("_expression", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        private static string E(SmartExpression e) => e == null ? "null" : (string)ExprField?.GetValue(e);

        private void Awake()
        {
            new Harmony("ckbaker10.gyk.extendzombiefarm").PatchAll();
        }

        // Phase 1: dump the balance data we need to understand the zombie garden.
        [HarmonyPatch(typeof(GameBalance), "LoadGameBalance")]
        [HarmonyPostfix]
        private static void Dump()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var c in GameBalance.me.craft_data.Where(a => a.id.StartsWith("garden_") || a.craft_in.Any(x => x.Contains("zombie_garden"))))
                {
                    sb.AppendLine("CRAFT " + c.id + " in=[" + string.Join(",", c.craft_in.ToArray()) + "]"
                        + " needs=[" + string.Join(",", c.needs.Select(n => n.id + "x" + n.value).ToArray()) + "]"
                        + " out=[" + string.Join(",", c.output.Select(n => n.id + " " + E(n.min_value) + ".." + E(n.max_value) + " ch=" + E(n.self_chance)).ToArray()) + "]"
                        + " change_wgo=" + c.change_wgo + " after=" + c.craft_after_finish + " time=" + E(c.craft_time)
                        + " icon=" + c.icon + " type=" + c.craft_type + " sub=" + c.sub_type + " cond=" + E(c.condition)
                        + " out_wgo=" + c.output_res_wgo + " set_wgo=" + c.output_set_res_wgo + " to_wgo=" + c.output_to_wgo.Count
                        + " needs_unlock=" + c.needs_unlock + " hidden=" + c.hidden);
                }
                foreach (var o in GameBalance.me.objs_data.Where(a => a.id.StartsWith("zombie_garden_desk") || a.id.Contains("cannabis") || a.id.StartsWith("garden_onion")))
                {
                    sb.AppendLine("OBJ " + o.id + " type=" + o.type + " has_craft=" + o.has_craft + " craft_preset=" + o.craft_preset
                        + " drops=[" + string.Join(",", o.drop_items.Select(n => n.id + " " + E(n.min_value) + ".." + E(n.max_value)).ToArray()) + "]");
                }
                var path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "balance-dump.txt");
                File.WriteAllText(path, sb.ToString());
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("ExtendZombieFarm dump failed: " + e);
            }
        }
    }
}
