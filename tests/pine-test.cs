using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
namespace Cloud90.GameLink {
    class PineTest {
        static void Assert(bool condition,string message) {if(!condition)throw new Exception(message);}
        static byte[] Exact(NetworkStream s,int n) {var b=new byte[n];int done=0;while(done<n){int count=s.Read(b,done,n-done);if(count==0)throw new EndOfStreamException();done+=count;}return b;}
        static void Case(byte[] reply,Action<Pine> test) {
            var server=new TcpListener(IPAddress.Loopback,0);server.Start();int port=((IPEndPoint)server.LocalEndpoint).Port;
            Exception failure=null;
            var thread=new Thread(delegate(){try{using(var c=server.AcceptTcpClient()){var stream=c.GetStream();int len=BitConverter.ToInt32(Exact(stream,4),0);Exact(stream,len-4);foreach(byte b in reply)stream.WriteByte(b);}}catch(Exception e){failure=e;}});thread.IsBackground=true;thread.Start();
            try {using(var pine=new Pine(port))test(pine);}finally {server.Stop();Assert(thread.Join(3000),"server hung");}if(failure!=null)throw failure;
        }
        static void Rejected(Action action) {bool rejected=false;try{action();}catch(IOException){rejected=true;}Assert(rejected,"accepted invalid response");}
        static int Main() {
            try {
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
                Console.WriteLine("12 PINE/profile checks passed");return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
    }
}
