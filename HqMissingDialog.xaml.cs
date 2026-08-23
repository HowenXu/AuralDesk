using System.Windows;

namespace AuralDesk
{
    /// <summary>HQPlayer 未安装时的提醒弹窗（与软件整体风格一致）。</summary>
    public partial class HqMissingDialog : Window
    {
        public HqMissingDialog()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
