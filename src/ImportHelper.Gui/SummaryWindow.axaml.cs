using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ImportHelper.Gui;

public partial class SummaryWindow : Window
{
  public SummaryWindow()
  {
    InitializeComponent();
  }

  public SummaryWindow(string summary) : this()
  {
    SummaryText.Text = summary;
  }

  private void OnOkClicked(object? sender, RoutedEventArgs e)
  {
    Close();
  }
}
