using System;
using System.Windows.Forms;

namespace PeakMasterMenu.Installer
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 任何漏网的异常都别让程序「啪」地消失，至少给用户一个提示
            Application.ThreadException += (sender, args) => ShowFatalError(args.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, args) => ShowFatalError(args.ExceptionObject as Exception);

            Application.Run(new MainForm());
        }

        private static void ShowFatalError(Exception exception)
        {
            var message = exception == null ? "未知错误。" : exception.Message;
            MenuDialog.Show(
                null,
                "安装器遇到了未处理的错误：\r\n\r\n" + message,
                "出错了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
