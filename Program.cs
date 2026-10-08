using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using TimeoutException = System.TimeoutException;

namespace Switcher
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var user = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator))
            {
                MessageBox.Show("Первый запуск: открой Install.cmd и один раз подтверди права администратора.\r\nПосле установки используй ярлык «Tailscale - zapret» на рабочем столе: повторный запрос не нужен.", "Установка переключателя");
                return;
            }
            if (args.Length == 1 && args[0] == "--prepare-engine")
            {
                try { BundledEngine.EnsureAvailable(); }
                catch (Exception ex) { Environment.ExitCode = 1; MessageBox.Show(ex.Message, "Установка движка Switcher", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                return;
            }
            bool first;
            using (var mutex = new Mutex(true, @"Local\TailscaleZapretSwitcher-" + user.User.Value, out first))
            {
                if (!first) return;
                try { using (var form = new Form1()) Application.Run(form); }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
