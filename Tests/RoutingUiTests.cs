using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Switcher;
class RoutingUiTests
{
    sealed class Backend : INativeRoutingBackend
    {
        public NativeRoutingPlan ReadRoutingPlan() { return new NativeRoutingPlan { PhysicalInterface="Ethernet", TailscaleInterface="Tailscale", PhysicalId="nic", TailscaleId="tail", NodeId="node", ExitNodeId="exit", PhysicalIndex=5 }; }
        public State Read() { return new State(true,false); }
        public void Validate() { } public void Tail(bool on) { throw new Exception("Unexpected Tailscale change"); } public void Zap(bool on) { throw new Exception("Unexpected zapret change"); }
    }
    sealed class Lease : IDisposable { public void Dispose() { } }
    static int count;
    static object Field(object target,string name) { return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target); }
    static void Refresh(RoutingForm form) { typeof(RoutingForm).GetMethod("UpdateState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null); }
    static void Check(bool value,string message) { if(!value) throw new Exception(message); count++; }
    [STAThread] static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        string root=Path.Combine(args[0],"ui-state"); Directory.CreateDirectory(root);
        var backend=new Backend();
        using(var controller=new RoutingController(root,(path,index)=>new Lease()))
        using(var form=new RoutingForm(controller,backend))
        {
            int changes=0; form.StateChanged += () => changes++;
            var start=(Button)Field(form,"start"); var stop=(Button)Field(form,"stop");
            Refresh(form); Check(start.Enabled && start.Text=="Включить","idle start available");
            controller.StartRouting(new RoutingSettings {EnginePath=args[1]},backend); Refresh(form);
            Check(start.Enabled && start.Text=="Перезапустить","active routing offers restart");
            Check(stop.Enabled,"active routing can stop"); Check(changes>=2,"state changes reach tray observer");
            form.SetExternalBusy(true); Check(!start.Enabled && !stop.Enabled,"external changes serialize controls");
            form.SetExternalBusy(false); Check(start.Enabled && stop.Enabled,"controls restored after external changes");
            controller.Stop(backend); Refresh(form);
            Check(start.Enabled && start.Text=="Включить" && !stop.Enabled,"stopped routing is ready to start");
            var failing=new Action(() => {throw new Exception("test-start-failure");});
            var task=(Task)typeof(RoutingForm).GetMethod("WorkAsync",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{failing,"unused"});
            var limit=DateTime.UtcNow.AddSeconds(5); while(!task.IsCompleted && DateTime.UtcNow<limit) {Application.DoEvents(); System.Threading.Thread.Sleep(10);}
            Check(task.IsCompleted && !form.OperationBusy && start.Enabled,"failure releases busy state");
            Check(((TextBox)Field(form,"log")).Text.StartsWith("test-start-failure"),"failure remains visible at top of log");
        }
        Console.WriteLine("PASS "+count+" routing UI checks; no live network changes");
    }
}
