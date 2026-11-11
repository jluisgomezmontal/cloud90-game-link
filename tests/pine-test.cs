using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
namespace Cloud90.GameLink {
    class PineTest {
        sealed class FakeMemory : IPineMemory {
            internal readonly System.Collections.Generic.Dictionary<uint,uint> Words=new System.Collections.Generic.Dictionary<uint,uint>();
            public uint Read32(uint address) {if(!Words.ContainsKey(address))throw new Exception("Unexpected address "+address.ToString("X8"));return Words[address];}
            public float ReadFloat(uint address) {return BitConverter.ToSingle(BitConverter.GetBytes(Read32(address)),0);}
            internal void Float(uint address,float value) {Words[address]=BitConverter.ToUInt32(BitConverter.GetBytes(value),0);}
        }
        static void Profiles() {
            var one=new GodOfWarProfile("gow1");var two=new GodOfWarProfile("gow2");
            Assert(two.Identity("SCUS-97481","2f123fd8"),"II identity");
            Assert(!two.Identity("SCUS-97399","D6385328"),"cross-game identity");
            Assert(two.PlayerAddress!=one.PlayerAddress&&two.MagicAddress!=one.MagicAddress,"separate profiles");
            var m=new FakeMemory();m.Words[two.PlayerAddress]=0x958660;m.Words[two.ModeAddress]=11;m.Words[0x958660]=0x958160;m.Words[0x958164]=two.Vtable;m.Float(0x9582d8,200);m.Float(0x9582dc,200);
            m.Words[0x001bc0dc]=0x8c428734;m.Words[0x00213760]=0xc4400178;m.Words[0x001d9250]=0x90655867;
            uint player,character;
            Assert(two.Character(m,out player,out character)&&character==0x958160,"II gameplay");
            m.Words[0x001d9250]=0;Assert(!two.Character(m,out player,out character),"modified program rejected");m.Words[0x001d9250]=0x90655867;
            m.Words[two.ModeAddress]=3;Assert(!two.Character(m,out player,out character),"II intro rejected");m.Words[two.ModeAddress]=11;
            m.Words[0x958164]=one.Vtable;Assert(!two.Character(m,out player,out character),"wrong vtable");m.Words[0x958164]=two.Vtable;
            m.Words[0x335864]=5u<<24;m.Words[0x2d870c]=0x680000;
            int displacement=0x67ff00-0x680004;m.Words[0x680004]=unchecked((uint)(displacement<<12));m.Words[0x67ff08]=200u<<16;
            Assert(two.MagicMaximum(m)==200,"signed relative max table and upper halfword");
            m.Words[0x335864]=0;Assert(two.MagicMaximum(m)==0,"locked magic");m.Words[0x335864]=5u<<24;
            m.Float(two.MagicAddress,0);m.Float(two.RageAddress,100);m.Words[two.OrbsAddress]=0;m.Float(0x00335844,0);Assert(Array.IndexOf(two.Capabilities(m),"magic_max")>=0,"II magic capability");
            Assert(Array.IndexOf(two.Capabilities(m),"heal_max")<0,"full health not offered");
            Assert(Array.IndexOf(two.Capabilities(m),"magic_empty")<0,"empty magic not offered");
            Assert(Array.IndexOf(two.Capabilities(m),"rage_max")<0,"full rage not offered");
            Assert(Array.IndexOf(two.Capabilities(m),"orbs_remove")<0,"empty currency not offered");
            Assert(Array.IndexOf(two.Capabilities(m),"amulet_max")>=0,"II amulet");
            Assert(one.OrbsAddress!=two.OrbsAddress,"separate orb counters");
            m.Words[two.OrbsAddress]=1000000;Assert(Array.IndexOf(two.Capabilities(m),"orbs_add")<0,"invalid counter");m.Words[two.OrbsAddress]=0;
            Assert(Array.IndexOf(two.Capabilities(m),"levels_max")<0,"II unmapped upgrades rejected");
            m.Words[0x67ff08]=65535u<<16;Assert(two.MagicMaximum(m)==0,"negative maximum rejected");
            var first=new FakeMemory();first.Words[one.PlayerAddress]=0xd534c0;first.Words[one.ModeAddress]=11;first.Words[0xd534c0]=0x795800;first.Words[0xd534d0]=0;first.Words[0x795804]=one.Vtable;first.Float(0x795978,100);first.Float(0x79597c,100);first.Words[0x795b24]=0x800000;first.Words[0x800084]=0;
            Assert(one.Character(first,out player,out character),"I gameplay");first.Words[0xd534d0]=1;Assert(!one.Character(first,out player,out character),"I scripted hold");first.Words[0xd534d0]=0;first.Words[0x800084]=1;Assert(!one.Character(first,out player,out character),"I finisher");
        }
        static void Safety() {
            var gate=new GodOfWarContextGate();
            Assert(!gate.Observe("II",10,20)&&!gate.Observe("II",10,20)&&gate.Observe("II",10,20),"three stable observations");
            string epoch=gate.Epoch;Assert(gate.Observe("II",10,20)&&gate.Epoch==epoch,"stable epoch");
            gate.Reset();Assert(!gate.Observe("II",10,20)&&!gate.Observe("II",10,20)&&gate.Observe("II",10,20)&&gate.Epoch!=epoch,"new transition epoch");
            Assert(!gate.Observe("II",11,20),"player replacement invalidates");
            string file=Path.GetTempFileName();
            try {
                var guard=new Pcsx2LogGuard(file);Assert(!guard.Refresh(),"empty log blocks");File.AppendAllText(file,"boot\n");Assert(guard.Refresh(),"verified log");
                File.AppendAllText(file,"(VMManager) Pausing...\n");Assert(guard.Refresh()&&guard.Generation==1,"pause invalidates generation");Assert(guard.Refresh()&&guard.Generation==1,"no duplicate events");
                File.AppendAllText(file,"  Loading SP");Assert(guard.Refresh(),"partial load line");File.AppendAllText(file,"U2\n");Assert(!guard.Refresh()&&guard.Latched,"split native state-load log");
                File.WriteAllText(file,"boot\n");Assert(!guard.Refresh(),"manual resume cannot bypass load latch");
            }finally {File.Delete(file);}
            file=Path.GetTempFileName();try {File.WriteAllText(file,"long initial log\n");var guard=new Pcsx2LogGuard(file);Assert(guard.Refresh(),"initial log");File.WriteAllText(file,"x");Assert(!guard.Refresh()&&guard.Latched,"truncated log blocks");}finally {File.Delete(file);}
        }
        static void Assert(bool condition,string message) {if(!condition)throw new Exception(message);}
        static byte[] Exact(NetworkStream s,int n) {var b=new byte[n];int done=0;while(done<n){int count=s.Read(b,done,n-done);if(count==0)throw new EndOfStreamException();done+=count;}return b;}
        static void Case(byte[] reply,Action<Pine> test,byte[] expected=null) {
            var server=new TcpListener(IPAddress.Loopback,0);server.Start();int port=((IPEndPoint)server.LocalEndpoint).Port;
            Exception failure=null;
            var thread=new Thread(delegate(){try{using(var c=server.AcceptTcpClient()){var stream=c.GetStream();int len=BitConverter.ToInt32(Exact(stream,4),0);byte[] request=Exact(stream,len-4);if(expected!=null){Assert(request.Length==expected.Length,"request width");for(int i=0;i<expected.Length;i++)Assert(request[i]==expected[i],"request encoding");}foreach(byte b in reply)stream.WriteByte(b);}}catch(Exception e){failure=e;}});thread.IsBackground=true;thread.Start();
            try {using(var pine=new Pine(port))test(pine);}finally {server.Stop();Assert(thread.Join(3000),"server hung");}if(failure!=null)throw failure;
        }
        static void Rejected(Action action) {bool rejected=false;try{action();}catch(IOException){rejected=true;}Assert(rejected,"accepted invalid response");}
        static int Main() {
            try {
                Profiles();
                Safety();
                Case(new byte[]{9,0,0,0,0,0,0,200,66},p=>Assert(GodOfWarEffects.SetFloat(p,0x100000,100).Contains("límite"),"no write or false success at limit"));
                Assert(GodOfWarEffects.OrbsTarget("orbs_add",59900,60000)==60000,"orb cap");
                Assert(GodOfWarEffects.OrbsTarget("orbs_remove",100,60000)==0,"orb floor");
                Assert(GodOfWarEffects.Policy("orbs_remove",true,true,true)!=null,"orb shield");
                Assert(GodOfWarEffects.Policy("orbs_add",true,false,false)!=null,"persistent permission");
                Case(new byte[]{5,0,0,0,0},p=>p.Write16(0x302d28,60000),new byte[]{5,0x28,0x2d,0x30,0,0x60,0xea});
                Case(new byte[]{9,0,0,0,0,0,0,0,0},p=>Assert(p.Status()==0,"status"));
                Case(new byte[]{9,0,0,0,0,0,0,200,66},p=>Assert(p.ReadFloat(0x100000)==100,"float"));
                Case(new byte[]{5,0,0,0,255},p=>Rejected(()=>p.Status()));
                Case(new byte[]{4,0,0,0},p=>Rejected(()=>p.Status()));
                Case(new byte[]{255,255,255,127},p=>Rejected(()=>p.Status()));
                Case(new byte[]{10,0,0,0,0,1,0,0,0,0},p=>Assert(p.Text(12)=="","empty string"));
                Case(new byte[]{10,0,0,0,0,2,0,0,0,65},p=>Rejected(()=>p.Text(12)));
                Assert(GodOfWarHost.ValidMeter(100,100),"valid meter");
                Assert(!GodOfWarHost.ValidMeter(float.NaN,100),"NaN");
                Assert(!GodOfWarHost.ValidMeter(0,100),"dead");
                Assert(!GodOfWarHost.ValidMeter(110,100),"over max");
                Assert(!GodOfWarHost.ValidMeter(1,float.PositiveInfinity),"infinite");
                Assert(GodOfWarEffects.HealthTarget("heal_max",10,200)==200,"full health");
                Assert(GodOfWarEffects.HealthTarget("damage",1,200)==1,"safe damage");
                Assert(GodOfWarEffects.HealthTarget("kill",100,200)==0,"kill");
                Assert(GodOfWarEffects.MagicTarget("magic_max",0,150)==150,"full magic");
                Assert(GodOfWarEffects.MagicTarget("magic_empty",150,150)==0,"empty magic");
                Assert(GodOfWarEffects.Policy("kill",true,true,false)!=null,"no death");
                Assert(GodOfWarEffects.Policy("kill",false,false,false)!=null,"destructive permission");
                Assert(GodOfWarEffects.Policy("kill",false,true,true)!=null,"shield kill");
                Assert(GodOfWarEffects.Policy("kill",false,true,false)==null,"explicit kill");
                Assert(GodOfWarEffects.Policy("levels_max",false,false,false)!=null,"progress permission");
                Assert(GodOfWarEffects.Policy("petrify",false,true,false)!=null,"unsupported petrification");
                Assert(GodOfWarEffects.UpgradeTargets("levels_max").Length==6,"six upgrade fields");
                Assert(GodOfWarEffects.UpgradeTargets("blades_min")[0].Value==1,"minimum blades");
                Assert(GodOfWarEffects.UpgradeTargets("blades_max")[0].Value==5,"maximum blades");
                foreach(var target in GodOfWarEffects.UpgradeTargets("levels_min"))Assert(target.Key>=0x0076D7F4&&target.Key<=0x0076D808,"upgrade bounds");
                Case(new byte[]{5,0,0,0,0},p=>p.WriteFloat(0x100000,100));
                Console.WriteLine("PINE, effect limits, destructive policy and upgrade target checks passed");return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
    }
}
