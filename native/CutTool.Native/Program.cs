using System;
using System.Threading;
using System.Windows.Forms;

namespace CutTool.Native
{
    internal static class Program
    {
        private const string MutexName = "Local\\CutTool.Native.SingleInstance";
        private const string WakeEventName = "Local\\CutTool.Native.Capture";

        [STAThread]
        private static void Main(string[] args)
        {
            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    try
                    {
                        using (var wake = EventWaitHandle.OpenExisting(WakeEventName))
                        {
                            wake.Set();
                        }
                    }
                    catch { }
                    return;
                }

                NativeMethods.EnablePerMonitorDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (var wake = new EventWaitHandle(false, EventResetMode.AutoReset, WakeEventName))
                {
                    bool probeMode = Array.Exists(args, delegate(string value)
                    {
                        return string.Equals(value, "--memory-probe", StringComparison.OrdinalIgnoreCase);
                    });
                    using (var controller = new AppController(wake, probeMode))
                    {
                        Application.Run(controller);
                    }
                }
            }
        }
    }
}
