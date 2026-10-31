using System.Windows.Forms;
using LangKinhHuKhong.TTS.UI;

namespace LangKinhHuKhong.TTS;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            MessageBox.Show(e.Exception.Message, "Lăng Kính Hư Không – Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        Application.Run(new MainForm());
    }
}
