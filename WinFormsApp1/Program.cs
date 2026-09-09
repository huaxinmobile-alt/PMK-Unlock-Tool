using System;
using System.Windows.Forms;

namespace WinFormsApp1  // ? ?? namespace ? Form1.cs ??? ????? ????????!
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());  // ? Form1 ??? ????? ???????
        }
    }
}