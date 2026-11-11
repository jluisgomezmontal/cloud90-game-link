using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Cloud90.GameLink
{
    internal interface IPineMemory {
        uint Read32(uint address);
        float ReadFloat(uint address);
    }
    // PCSX2's local IPC protocol. Addresses are EE addresses, never host pointers.
    internal sealed class Pine : IDisposable, IPineMemory
    {
        readonly TcpClient client;
        readonly NetworkStream stream;
        readonly object gate = new object();
        public Pine(int port)
        {
            if (port < 1024 || port > 65535) throw new ArgumentOutOfRangeException("port");
            client = new TcpClient();
            try {
                var pending = client.BeginConnect(IPAddress.Loopback, port, null, null);
                using (pending.AsyncWaitHandle) {
                    if (!pending.AsyncWaitHandle.WaitOne(1500)) throw new IOException("PCSX2 no responde: activa PINE en Ajustes avanzados");
                    client.EndConnect(pending);
                }
                client.NoDelay = true;
                stream = client.GetStream(); stream.ReadTimeout = 1500; stream.WriteTimeout = 1500;
            } catch { client.Close(); throw; }
        }
        byte[] ReadExact(int count)
        {
            byte[] bytes = new byte[count]; int done = 0;
            while (done < count) { int n = stream.Read(bytes, done, count-done); if (n == 0) throw new EndOfStreamException("PCSX2 cerró PINE"); done += n; }
            return bytes;
        }
        internal byte[] Call(byte[] body)
        {
            lock(gate) {
                byte[] size = BitConverter.GetBytes(body.Length + 4);
                stream.Write(size,0,4); stream.Write(body,0,body.Length);
                int length = BitConverter.ToInt32(ReadExact(4),0);
                if (length < 5 || length > 450000) throw new IOException("Respuesta PINE inválida");
                byte[] reply = ReadExact(length-4);
                if (reply[0] != 0) throw new IOException("PCSX2 rechazó la operación");
                byte[] result = new byte[reply.Length-1]; Array.Copy(reply,1,result,0,result.Length); return result;
            }
        }
        public string Text(byte opcode)
        {
            byte[] data = Call(new[]{opcode});
            if (data.Length < 4) throw new IOException("Cadena PINE incompleta");
            uint size = BitConverter.ToUInt32(data,0);
            if (size < 1 || size > data.Length-4 || data[3+size] != 0) throw new IOException("Cadena PINE inválida");
            return new UTF8Encoding(false,true).GetString(data,4,(int)size-1);
        }
        public uint Status() { byte[] b=Call(new byte[]{15}); if(b.Length!=4)throw new IOException("Estado PINE inválido"); return BitConverter.ToUInt32(b,0); }
        static void CheckAddress(uint address) { if(address>0x01fffffc || (address&3)!=0)throw new ArgumentOutOfRangeException("address"); }
        public uint Read32(uint address)
        {
            CheckAddress(address); byte[] request=new byte[5]; request[0]=2; Array.Copy(BitConverter.GetBytes(address),0,request,1,4);
            byte[] data=Call(request); if(data.Length!=4)throw new IOException("Lectura PINE incompleta"); return BitConverter.ToUInt32(data,0);
        }
        public float ReadFloat(uint address) { return BitConverter.ToSingle(BitConverter.GetBytes(Read32(address)),0); }
        internal void Write32(uint address, uint value)
        {
            CheckAddress(address);
            byte[] request=new byte[9];request[0]=6;
            Array.Copy(BitConverter.GetBytes(address),0,request,1,4);Array.Copy(BitConverter.GetBytes(value),0,request,5,4);
            if(Call(request).Length!=0)throw new IOException("Escritura PINE inválida");
        }
        internal void WriteFloat(uint address, float value)
        {
            if(float.IsNaN(value)||float.IsInfinity(value))throw new ArgumentOutOfRangeException("value");
            Write32(address,BitConverter.ToUInt32(BitConverter.GetBytes(value),0));
        }
        internal void Write16(uint address, ushort value) {
            if(address>0x01fffffe||(address&1)!=0)throw new ArgumentOutOfRangeException("address");
            byte[] request=new byte[7];request[0]=5;
            Array.Copy(BitConverter.GetBytes(address),0,request,1,4);Array.Copy(BitConverter.GetBytes(value),0,request,5,2);
            if(Call(request).Length!=0)throw new IOException("Escritura PINE inválida");
        }
        public void Dispose() { client.Close(); }
    }
}
