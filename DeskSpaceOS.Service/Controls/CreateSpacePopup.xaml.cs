using System;
using System.Windows;
using System.Windows.Input;

namespace DeskSpaceOS.Service.Controls;

public partial class CreateSpacePopup : System.Windows.Controls.UserControl
{
    public event EventHandler? CreateSpaceClicked;
    
    public CreateSpacePopup()
    {
        InitializeComponent();
        this.MouseLeftButtonDown += (s, e) => CreateSpaceClicked?.Invoke(this, EventArgs.Empty);
    }
    
    private void CreateText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CreateSpaceClicked?.Invoke(this, EventArgs.Empty);
    }
}
