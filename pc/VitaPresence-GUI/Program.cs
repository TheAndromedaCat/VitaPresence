using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace VitaPresence_GUI
{
    static class Program
    {
        [DllImport("user32.dll")]
        private static extern int PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const int WM_SHOWME = 0x8000;

        [STAThread]
        static void Main()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "VitaPresence_GUI", out createdNew))
            {
                if (createdNew)
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new MainForm());
                }
                else
                {
                    IntPtr hWnd = FindWindow(null, "VitaPresence"); // Asegúrate de que el título de la ventana sea correcto
                    if (hWnd != IntPtr.Zero)
                    {
                        PostMessage(hWnd, WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                        SetForegroundWindow(hWnd);
                    }
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    }
}
