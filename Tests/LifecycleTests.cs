using System;
using System.IO;
using Switcher;
class LifecycleTests
{
    sealed class Backend : INativeRoutingBackend {
        public int Writes; public bool Valid=true; public string Exit="exit";
        public NativeRoutingPlan ReadRoutingPlan(){if(!Valid)throw new Exception("connection lost");return new NativeRoutingPlan{PhysicalInterface="Ethernet",TailscaleInterface="Tailscale",PhysicalId="nic",TailscaleId="tail",NodeId="same-node",ExitNodeId=Exit,PhysicalIndex=5};}
        public State Read(){return new State(true,false);} public void Validate(){}
        public void Tail(bool on){Writes++;} public void Zap(bool on){Writes++;}
    }
    sealed class Lease:IDisposable {public bool Closed;public void Dispose(){Closed=true;}}
    static int n;static void Check(bool value,string name){if(!value)throw new Exception(name);n++;}
    static void Main(string[] args){
        string root=Path.Combine(args[0],"native-lifecycle");Directory.CreateDirectory(root);
        string fail=Path.Combine(Path.GetDirectoryName(args[1]),"fail");if(File.Exists(fail))File.Delete(fail);
        var backend=new Backend();Lease lease=null;
        var settings=new RoutingSettings{EnginePath=args[1]};
        using(var c=new RoutingController(root,(path,index)=>lease=new Lease())){
            c.StartRouting(settings,backend);
            Check(c.Tunnel&&c.Alive,"engine active");Check(backend.Writes==0,"native connection unchanged");
            Check(!c.HasRecovery&&!Directory.Exists(Path.Combine(root,"routing-state")),"no new recovery or identity");
            c.CheckConnection(backend);c.Stop(backend);Check(lease.Closed&&!c.Active,"stop releases lease and child");
            Check(backend.Writes==0,"stop does not toggle Tailscale");
            c.StartRouting(settings,backend);backend.Exit="other";bool failed=false;
            try{c.CheckConnection(backend);}catch{failed=true;}
            Check(failed&&lease.Closed&&!c.Active,"exit-node change stops and releases");backend.Exit="exit";
            File.WriteAllText(fail,"fail");failed=false;
            try{c.StartRouting(settings,backend);}catch{failed=true;}
            Check(failed&&lease.Closed&&!c.Active,"failed start releases everything");
            Check(backend.Writes==0,"failure leaves services untouched");File.Delete(fail);
            backend.Valid=false;lease=null;failed=false;try{c.StartRouting(settings,backend);}catch{failed=true;}
            Check(failed&&lease==null,"invalid connection never creates exceptions");backend.Valid=true;
            c.StartRouting(settings,backend);
        }
        Check(lease.Closed,"dispose removes temporary permission");
        Check(backend.Writes==0,"all native lifecycle scenarios preserve connection");
        Console.WriteLine("PASS "+n+" native lifecycle checks");
    }
}
