using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Switcher
{
    // Dynamic WFP session: Windows removes every object when this process dies.
    // Only the verified routing engine, on one selected physical interface, is permitted.
    public sealed class TemporaryFirewall : IDisposable
    {
        IntPtr handle; Guid sublayerKey;
        readonly System.Collections.Generic.List<Guid> filterKeys = new System.Collections.Generic.List<Guid>();
        public Guid[] FilterKeys { get { return filterKeys.ToArray(); } }
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct Display { [MarshalAs(UnmanagedType.LPWStr)] public string Name; [MarshalAs(UnmanagedType.LPWStr)] public string Description; }
        [StructLayout(LayoutKind.Sequential)] struct Blob { public uint Size; public IntPtr Data; }
        [StructLayout(LayoutKind.Sequential)] struct Value { public uint Type; public IntPtr Data; }
        [StructLayout(LayoutKind.Sequential)] struct Session { public Guid Key; public Display Display; public uint Flags, Timeout, Pid; public IntPtr Sid, Username; public int Kernel; }
        [StructLayout(LayoutKind.Sequential)] struct Sublayer { public Guid Key; public Display Display; public uint Flags; public IntPtr Provider; public Blob Data; public ushort Weight; }
        [StructLayout(LayoutKind.Sequential)] struct Condition { public Guid Field; public uint Match; public Value Value; }
        [StructLayout(LayoutKind.Sequential)] struct ActionValue { public uint Type; public Guid Key; }
        [StructLayout(LayoutKind.Explicit, Size=16)] struct Context { [FieldOffset(0)] public ulong Raw; [FieldOffset(0)] public Guid Key; }
        [StructLayout(LayoutKind.Sequential)] struct Filter { public Guid Key; public Display Display; public uint Flags; public IntPtr Provider; public Blob Data; public Guid Layer, Sublayer; public Value Weight; public uint Count; public IntPtr Conditions; public ActionValue Action; public Context Context; public IntPtr Reserved; public ulong Id; public Value EffectiveWeight; }
        [DllImport("fwpuclnt.dll", CharSet=CharSet.Unicode)] static extern uint FwpmEngineOpen0(string server, uint auth, IntPtr identity, ref Session session, out IntPtr handle);
        [DllImport("fwpuclnt.dll")] static extern uint FwpmEngineClose0(IntPtr handle);
        [DllImport("fwpuclnt.dll")] static extern uint FwpmSubLayerAdd0(IntPtr handle, ref Sublayer layer, IntPtr sd);
        [DllImport("fwpuclnt.dll")] static extern uint FwpmFilterAdd0(IntPtr handle, ref Filter filter, IntPtr sd, out ulong id);
        [DllImport("fwpuclnt.dll", CharSet=CharSet.Unicode)] static extern uint FwpmGetAppIdFromFileName0(string file, out IntPtr blob);
        [DllImport("fwpuclnt.dll")] static extern void FwpmFreeMemory0(ref IntPtr memory);
        [DllImport("iphlpapi.dll")] static extern uint ConvertInterfaceIndexToLuid(uint index, out ulong luid);
        static void Check(uint status) { if(status != 0) throw new Win32Exception(unchecked((int)status), "Временное правило Windows: 0x" + status.ToString("X8")); }
        public TemporaryFirewall(string enginePath, uint interfaceIndex)
        {
            if(IntPtr.Size != 8) throw new PlatformNotSupportedException("Нужен 64-разрядный Switcher.");
            IntPtr app=IntPtr.Zero, luidPtr=IntPtr.Zero, conditions=IntPtr.Zero;
            try
            {
                var session=new Session { Key=Guid.NewGuid(), Display=new Display { Name="Switcher temporary routing" }, Flags=1 };
                Check(FwpmEngineOpen0(null, 10, IntPtr.Zero, ref session, out handle));
                var sublayer=new Sublayer { Key=Guid.NewGuid(), Display=new Display { Name="Switcher engine direct exceptions" }, Weight=65535 };
                Check(FwpmSubLayerAdd0(handle, ref sublayer, IntPtr.Zero)); sublayerKey=sublayer.Key;
                Check(FwpmGetAppIdFromFileName0(enginePath, out app));
                ulong luid; Check(ConvertInterfaceIndexToLuid(interfaceIndex, out luid));
                luidPtr=Marshal.AllocHGlobal(8); Marshal.WriteInt64(luidPtr, unchecked((long)luid));
                var values=new[] {
                    new Condition { Field=new Guid("d78e1e87-8644-4ea5-9437-d809ecefc971"), Value=new Value {Type=12, Data=app} },
                    new Condition { Field=new Guid("4cd62a49-59c3-4969-b7f3-bda5d32890a4"), Value=new Value {Type=4, Data=luidPtr} }
                };
                int size=Marshal.SizeOf(typeof(Condition)); conditions=Marshal.AllocHGlobal(size*values.Length);
                for(int i=0;i<values.Length;i++) Marshal.StructureToPtr(values[i], IntPtr.Add(conditions,i*size), false);
                foreach(string layer in new[] {"c38d57d1-05a7-4c33-904f-7fbceee60e82","4a72393b-319f-44bc-84c3-ba54dcb3b6b4"})
                {
                    var filter=new Filter { Key=Guid.NewGuid(), Display=new Display {Name="Switcher: engine on physical interface (session only)"}, Flags=8, Layer=new Guid(layer), Sublayer=sublayer.Key, Weight=new Value {Type=1,Data=new IntPtr(15)}, Count=(uint)values.Length, Conditions=conditions, Action=new ActionValue {Type=0x1002} };
                    ulong id; Check(FwpmFilterAdd0(handle, ref filter, IntPtr.Zero, out id)); filterKeys.Add(filter.Key);
                }
            }
            catch { Dispose(); throw; }
            finally { if(app!=IntPtr.Zero) FwpmFreeMemory0(ref app); if(luidPtr!=IntPtr.Zero) Marshal.FreeHGlobal(luidPtr); if(conditions!=IntPtr.Zero) Marshal.FreeHGlobal(conditions); }
        }
        public void AllowTunnel(string name)
        {
            var nic=Array.Find(System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces(), n=>n.Name==name);
            if(nic==null)throw new InvalidOperationException("Временный адаптер не найден.");
            ulong luid;Check(ConvertInterfaceIndexToLuid((uint)nic.GetIPProperties().GetIPv4Properties().Index,out luid));
            IntPtr ptr=Marshal.AllocHGlobal(8), conditions=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Condition))*2);
            try
            {
                Marshal.WriteInt64(ptr,unchecked((long)luid));
                var condition=new Condition{Field=new Guid("4cd62a49-59c3-4969-b7f3-bda5d32890a4"),Value=new Value{Type=4,Data=ptr}};
                Marshal.StructureToPtr(condition,conditions,false);
                foreach(string layer in new[]{"c38d57d1-05a7-4c33-904f-7fbceee60e82","4a72393b-319f-44bc-84c3-ba54dcb3b6b4","e1cd9fe7-f4b5-4273-96c0-592e487b8650","a3b42c97-9f04-4672-b87e-cee9c483257f"})
                {
                    var filter=new Filter{Key=Guid.NewGuid(),Display=new Display{Name="Switcher: temporary TUN access"},Flags=8,Layer=new Guid(layer),Sublayer=sublayerKey,Weight=new Value{Type=1,Data=new IntPtr(14)},Count=1,Conditions=conditions,Action=new ActionValue{Type=0x1002}};
                    ulong id;Check(FwpmFilterAdd0(handle,ref filter,IntPtr.Zero,out id));filterKeys.Add(filter.Key);
                }
                // Reject IPv6 at connect time (before a synthetic TCP handshake),
                // so applications can fall back to IPv4. Keep DNS over IPv6 usable.
                var notDns=new Condition{Field=new Guid("c35a604d-d22b-4e1a-91b4-68f674ee674b"),Match=10,Value=new Value{Type=2,Data=new IntPtr(53)}};
                Marshal.StructureToPtr(notDns,IntPtr.Add(conditions,Marshal.SizeOf(typeof(Condition))),false);
                var ipv6=new Filter{Key=Guid.NewGuid(),Display=new Display{Name="Switcher: IPv4 mode on temporary TUN"},Layer=new Guid("4a72393b-319f-44bc-84c3-ba54dcb3b6b4"),Sublayer=sublayerKey,Weight=new Value{Type=1,Data=new IntPtr(15)},Count=2,Conditions=conditions,Action=new ActionValue{Type=0x1001}};
                ulong blockId;Check(FwpmFilterAdd0(handle,ref ipv6,IntPtr.Zero,out blockId));filterKeys.Add(ipv6.Key);
            } finally {Marshal.FreeHGlobal(ptr);Marshal.FreeHGlobal(conditions);}
        }
        [DllImport("fwpuclnt.dll")] static extern uint FwpmFilterGetByKey0(IntPtr engine, ref Guid key, out IntPtr filter);
        public static bool FilterExists(Guid key)
        {
            IntPtr engine=IntPtr.Zero, filter=IntPtr.Zero;
            var session=new Session{Key=Guid.NewGuid(),Flags=1};
            Check(FwpmEngineOpen0(null,10,IntPtr.Zero,ref session,out engine));
            try {uint result=FwpmFilterGetByKey0(engine,ref key,out filter); if(result==0x80320003)return false; Check(result);return true;}
            finally {if(filter!=IntPtr.Zero)FwpmFreeMemory0(ref filter);FwpmEngineClose0(engine);}
        }
        public void Dispose() { if(handle!=IntPtr.Zero) { FwpmEngineClose0(handle); handle=IntPtr.Zero; } }
    }
}



