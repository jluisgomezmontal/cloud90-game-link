using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
namespace Cloud90.Cuphead {
 public static class BridgeJson {
  static object Value(JsonTextReader r, int depth) {
   if(depth>8) throw new FormatException("JSON depth");
   if(r.TokenType==JsonToken.StartObject) {
    var d=new Dictionary<string,object>();
    while(r.Read() && r.TokenType!=JsonToken.EndObject) {
     if(r.TokenType!=JsonToken.PropertyName) throw new FormatException("JSON property");
     string key=(string)r.Value; if(!r.Read()) throw new FormatException("JSON incomplete"); d.Add(key,Value(r,depth+1));
    }
    if(r.TokenType!=JsonToken.EndObject) throw new FormatException("JSON incomplete"); return d;
   }
   if(r.TokenType==JsonToken.StartArray) {
    var a=new List<object>(); while(r.Read() && r.TokenType!=JsonToken.EndArray) a.Add(Value(r,depth+1));
    if(r.TokenType!=JsonToken.EndArray) throw new FormatException("JSON incomplete"); return a;
   }
   if(r.TokenType==JsonToken.String || r.TokenType==JsonToken.Integer || r.TokenType==JsonToken.Boolean || r.TokenType==JsonToken.Null) return r.Value;
   throw new FormatException("JSON value");
  }
  static string Text(Dictionary<string,object> d,string k) {object v;return d.TryGetValue(k,out v)?v as string:null;}
  static long Num(Dictionary<string,object> d,string k) {object v;if(!d.TryGetValue(k,out v)||!(v is long))throw new FormatException("JSON number: "+k);return (long)v;}
  static bool Flag(Dictionary<string,object> d,string k) {object v;if(!d.TryGetValue(k,out v)||!(v is bool))throw new FormatException("JSON bool: "+k);return (bool)v;}
  static bool OptionalFlag(Dictionary<string,object> d,string k,bool fallback) {return d.ContainsKey(k)?Flag(d,k):fallback;}
  public static T Read<T>(string text) where T:class {
   using(var r=new JsonTextReader(new StringReader(text))) {
    r.DateParseHandling=DateParseHandling.None;
    if(!r.Read())throw new FormatException("JSON empty"); var d=Value(r,0) as Dictionary<string,object>;
    if(d==null||r.Read())throw new FormatException("JSON root");
    if(typeof(T)==typeof(Launch))return new Launch{session=Text(d,"session")} as T;
    var inbox=new InboxMessage{session=Text(d,"session"),heartbeatMs=Num(d,"heartbeatMs"),paused=Flag(d,"paused"),noDeath=OptionalFlag(d,"noDeath",true),allowDestructive=OptionalFlag(d,"allowDestructive",false)};
    object raw;
    if(d.TryGetValue("effects",out raw) && raw is List<object>) {
     var commands=new List<Command>();
     foreach(object item in (List<object>)raw) {
      var c=item as Dictionary<string,object>;if(c==null)throw new FormatException("JSON command");
      commands.Add(new Command{id=Text(c,"id"),effect=Text(c,"effect"),arg=Text(c,"arg"),epoch=Text(c,"epoch"),ms=checked((int)Num(c,"ms")),safe=Flag(c,"safe"),expiresMs=Num(c,"expiresMs"),allowDestructive=OptionalFlag(c,"allowDestructive",false)});
     }
     inbox.effects=commands.ToArray();
    }
    return inbox as T;
   }
  }
  static void WriteValue(JsonTextWriter w,object v) {
   if(v==null){w.WriteNull();return;}
   if(v is string){w.WriteValue((string)v);return;} if(v is bool){w.WriteValue((bool)v);return;}
   if(v is int){w.WriteValue((int)v);return;} if(v is long){w.WriteValue((long)v);return;}
   if(v is Array){w.WriteStartArray();foreach(object i in (Array)v)WriteValue(w,i);w.WriteEndArray();return;}
   w.WriteStartObject();foreach(var f in v.GetType().GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public)){w.WritePropertyName(f.Name);WriteValue(w,f.GetValue(v));}w.WriteEndObject();
  }
  public static string Write(Status status){using(var output=new StringWriter(System.Globalization.CultureInfo.InvariantCulture)){using(var w=new JsonTextWriter(output))WriteValue(w,status);return output.ToString();}}
 }
}
