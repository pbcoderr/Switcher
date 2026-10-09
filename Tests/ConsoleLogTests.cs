using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Switcher;
class ConsoleLogTests
{
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
    static int count;
    static void Check(bool value,string text) {if(!value)throw new Exception(text);count++;}
    static int Top(ConsoleLogBox box) {return (int)SendMessage(box.Handle,0xCE,IntPtr.Zero,IntPtr.Zero);}
    [STAThread]static void Main() {try {Run();}catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}}
    static void Run()
    {
        using(var form=new Form {Size=new Size(500,200),StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)})
        using(var box=new ConsoleLogBox {Dock=DockStyle.Fill}) {
            form.Controls.Add(box);form.Show();Application.DoEvents();
            string[] lines=Enumerable.Range(0,160).Select(i=>"Log line "+i).ToArray();
            box.UpdateLog(String.Join("\r\n",lines));Application.DoEvents();
            Check(Top(box)>130,"first update shows newest lines");
            SendMessage(box.Handle,0xB6,IntPtr.Zero,new IntPtr(-70));int top=Top(box);string anchor=box.Lines[top];
            box.UpdateLog(String.Join("\r\n",lines)+"\r\nLog line 160");
            Check(Top(box)==top,"new lines preserve reading position");
            box.UpdateLog(String.Join("\r\n",lines.Skip(10))+"\r\nLog line 160");
            Check(box.Lines[Top(box)]==anchor,"rolling buffer preserves visible line");
            box.Select(box.TextLength,0);box.ScrollToCaret();int before=Top(box);
            box.UpdateLog(box.Text+"\r\nLog line 161\r\nLog line 162");
            Check(Top(box)>before,"scrolling to bottom resumes follow");
            form.Close();
        }
        Console.WriteLine("PASS "+count+" console scroll checks");
    }
}
