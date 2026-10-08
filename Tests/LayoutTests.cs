using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Switcher;
class LayoutTests
{
    static int count;
    static object Field(object value,string name) { return value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value); }
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); count++; }
    static Control Find(Control parent,string name) { foreach(Control child in parent.Controls) { if(child.Name==name)return child; var found=Find(child,name); if(found!=null)return found; } return null; }
    static TextBox Body(Control parent) { foreach(Control child in parent.Controls) { if(child is TextBox)return (TextBox)child; var found=Body(child); if(found!=null)return found; } return null; }
    static void Show(Form form) { form.StartPosition=FormStartPosition.Manual; form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents(); }
    [STAThread] static void Main(string[] args) { try { Run(args); } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; } }
    static void Run(string[] args)
    {
        Application.EnableVisualStyles();
        using(var help=new HelpForm()) {
            Show(help); Check(Find(help,"helpTabs")!=null,"help topics exist");
            for(int i=0;i<5;i++) {
                var tab=(Button)Find(help,"helpTab"+i); Check(tab!=null && tab.Visible,"topic visible"); tab.PerformClick();
                Check(!String.IsNullOrWhiteSpace(Body(help).Text),"topic has content");
            }
            Check(Body(help).Text.Contains("1.1.0.1"),"help version updated"); help.Close();
        }
        using(var controller=new RoutingController(Path.Combine(args[0],"layout")))
        using(var form=new RoutingForm(controller,null)) {
            Show(form); var split=(SplitContainer)Find(form,"routingSplit");
            Check(split!=null && !split.IsSplitterFixed,"log splitter enabled");
            int before=split.Panel2.Height; split.SplitterDistance-=30;
            Check(split.Panel2.Height>before,"dragging splitter increases log height");
            form.Size=form.MinimumSize; Application.DoEvents();
            Check(split.Panel1.Height>=split.Panel1MinSize && split.Panel2.Height>=split.Panel2MinSize,"minimum window keeps both panels usable");
            var grid=(DataGridView)Field(form,"grid"); var origin=form.PointToClient(grid.PointToScreen(Point.Empty)); Check(origin.X==16 && form.ClientSize.Width-origin.X-grid.Width==16,"minimum width preserves equal outer margins"); Check(grid.Columns.Contains("pickProcess"),"process picker column exists");
            Check(grid.EditMode==DataGridViewEditMode.EditOnEnter,"editable cell opens on click");form.Close();
        }
        using(var picker=new ProcessPickerForm()) {
            typeof(ProcessPickerForm).GetField("names",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(picker,new[]{"ExampleApp.exe","Other.exe"});
            var search=(TextBox)Field(picker,"search"); search.Text="example";
            var list=(DataGridView)Field(picker,"list"); Check(list.Rows.Count==1,"process search filters case insensitively");
            list.CurrentCell=list.Rows[0].Cells[0];
            typeof(ProcessPickerForm).GetMethod("SelectCurrent",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(picker,null);
            Check(picker.SelectedProcess=="ExampleApp.exe","selection returns executable name only");
        }
        Console.WriteLine("PASS "+count+" layout and process picker checks");
    }
}
