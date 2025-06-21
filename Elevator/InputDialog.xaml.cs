using System.Windows;

namespace Elevator
{
    public partial class InputDialog : Window
    {
        public string ResponseText { get; private set; } = string.Empty;
        public InputDialog(string question)
        {
            InitializeComponent();
            lblQuestion.Content = question;
        }
        private void btnDialogOk_Click(object sender, RoutedEventArgs e)
        {
            ResponseText = txtAnswer.Text;
            DialogResult = true;
        }
    }
}
