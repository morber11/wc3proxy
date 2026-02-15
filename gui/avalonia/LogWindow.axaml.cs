using Avalonia.Controls;

namespace wc3proxy.avalonia
{
    public partial class LogWindow : Window
    {
        public bool IsClosed { get; private set; }

        public LogWindow()
        {
            InitializeComponent();
            Closed += (_, _) => IsClosed = true;
        }

        public void AppendText(string s)
        {
            LogBox.Text += s;
            LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
        }
    }
}
