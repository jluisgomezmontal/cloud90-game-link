using System;
using Cloud90.Cuphead;

public static class BridgeJsonTests
{
    public static int Run()
    {
        string valid = "{\"session\":\"s\",\"heartbeatMs\":12345678900,\"paused\":false,\"effects\":[{\"id\":\"one\",\"effect\":\"damage\",\"arg\":\"\",\"epoch\":\"attempt-1\",\"ms\":1,\"safe\":true,\"expiresMs\":12345679900}]}";
        var inbox = BridgeJson.Read<InboxMessage>(valid);
        if (inbox.effects.Length != 1 || !inbox.effects[0].safe || inbox.heartbeatMs != 12345678900 || inbox.effects[0].epoch != "attempt-1") throw new Exception("Lost nested command or safety fields");
        if (!inbox.noDeath || inbox.allowDestructive || inbox.effects[0].allowDestructive) throw new Exception("Missing policy must fail closed");
        var policy = BridgeJson.Read<InboxMessage>(valid.Replace("\"paused\":false", "\"paused\":false,\"noDeath\":false,\"allowDestructive\":true"));
        if (policy.noDeath || !policy.allowDestructive) throw new Exception("Policy lost in bridge JSON");
        foreach (string bad in new[] { valid + "{}", valid.Replace("\"paused\":false", "\"paused\":false,\"allowDestructive\":\"true\""), valid.Substring(0,valid.Length-1), valid.Replace("\"safe\":true", "\"safe\":\"false\""), valid.Replace("\"heartbeatMs\":12345678900", "\"heartbeatMs\":1.5"), "{\"session\":\"a\",\"session\":\"b\"}" })
        {
            bool rejected = false;
            try { BridgeJson.Read<InboxMessage>(bad); } catch { rejected = true; }
            if (!rejected) throw new Exception("Accepted malformed or ambiguous command JSON");
        }
        string status = BridgeJson.Write(new Status { session="s", health=2, acks=new[] {new Ack {id="one",result="ok"}}, activeEffects=new[]{"slow"}, character="Ms. Chalice" });
        if (!status.Contains("\"acks\":[{\"id\":\"one\",\"result\":\"ok\"}]") || !status.Contains("\"activeEffects\":[\"slow\"]")) throw new Exception("Status arrays omitted");
        Console.WriteLine("Cuphead streaming JSON: nested commands, ACKs, safety flags, long timestamps and malformed input passed");
        return 0;
    }
}
