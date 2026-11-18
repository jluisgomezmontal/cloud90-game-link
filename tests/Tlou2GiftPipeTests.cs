using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace Cloud90.GameLink
{
    public static class Tlou2GiftPipeTests
    {
        public static void Run()
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            using (var adapter = new Tlou2GiftPipe())
            {
                var link = new Link(() => null, "tlou2", "TLOU2 test");
                var json = new JavaScriptSerializer();
                var handle = typeof(Tlou2GiftPipe).GetMethod("ReadMessage", fields);
                var pending = typeof(Tlou2GiftPipe).GetField("pending", fields);
                var gameReady = typeof(Tlou2GiftPipe).GetField("gameReady", fields);
                Action<string> read = message => handle.Invoke(adapter,
                    new object[] { json.Deserialize<Dictionary<string, object>>(message), link });
                Action<string> arm = preset =>
                {
                    pending.SetValue(adapter, new Inbox.Item { Id = "test" });
                    typeof(Tlou2GiftPipe).GetField("pendingSequence", fields).SetValue(adapter, 23);
                    typeof(Tlou2GiftPipe).GetField("pendingPreset", fields).SetValue(adapter, preset);
                };

                read("{\"t\":\"welcome\",\"protocol\":1,\"mod\":\"1.2.0\",\"build\":\"t2r-pc-1.6.0\",\"presets\":[" +
                     "{\"id\":\"Runner\",\"faction\":\"infected\"},{\"id\":\"Clicker\",\"faction\":\"infected\"}," +
                     "{\"id\":\"WLF dog\",\"faction\":\"ally\"}]}");
                read("{\"t\":\"state\",\"gameReady\":true,\"queued\":0,\"alive\":0,\"aliveCap\":5}");
                Check(adapter.Capabilities().SequenceEqual(new[]
                    { "spawn_runner", "enemy_ambush", "enemy_ambush_ready", "enemy_ambush:clicker", "enemy_ambush:runner" }),
                    "only the exact allowlisted preset and faction are advertised");

                arm("Clicker");
                read("{\"t\":\"ack\",\"ref\":23,\"accepted\":1}");
                read("{\"t\":\"state\",\"gameReady\":true,\"queued\":1,\"alive\":0,\"aliveCap\":5}");
                Check((bool)gameReady.GetValue(adapter) &&
                    adapter.Capabilities().Contains("enemy_ambush:clicker") &&
                    !adapter.Capabilities().Contains("enemy_ambush_ready"),
                    "a busy spawner keeps the campaign active while withholding new spawns");
                read("{\"t\":\"spawned\",\"ref\":24,\"result\":\"ok\",\"preset\":\"Clicker\"}");
                Check(pending.GetValue(adapter) != null, "a queued ACK and a different sequence leave the command pending");
                read("{\"t\":\"spawned\",\"ref\":23,\"result\":\"ok\",\"preset\":\"Clicker\"}");
                read("{\"t\":\"spawned\",\"ref\":23,\"result\":\"ok\",\"preset\":\"Clicker\"}");
                Check(pending.GetValue(adapter) == null, "a matching spawn settles once");

                arm("Runner");
                read("{\"t\":\"spawned\",\"ref\":23,\"result\":\"ok\",\"preset\":\"Stalker\"}");
                Check(pending.GetValue(adapter) == null, "a mismatched preset settles as uncertain");
                arm("Runner");
                read("{\"t\":\"ack\",\"ref\":23,\"accepted\":0}");
                Check(pending.GetValue(adapter) == null, "a refusal settles immediately");
            }
        }

        static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("TLOU2: " + description);
            Console.WriteLine("PASS TLOU2: " + description);
        }
    }
}
