using System;
using System.Globalization;
using SrcCad.Core.Sketch;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SrcCad.App;

public sealed class DimensionEditDialog : Window
{
    private readonly TextBox _valueBox;
    private readonly Func<string,string?> _commit;
    private readonly TextBlock _error=new(){Foreground=System.Windows.Media.Brushes.Firebrick,TextWrapping=TextWrapping.Wrap};

    public DimensionEditDialog(string title, double currentValue, string unit, Func<string,string?> commit)
    {
        _commit = commit;
        Title = title;
        Width = 320;
        Height = 235;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = $"Değer ({unit})",
            Margin = new Thickness(0, 0, 0, 6)
        };
        root.Children.Add(label);

        _valueBox = new TextBox
        {
            Text = currentValue.ToString("G17", CultureInfo.CurrentCulture),
            FontSize = 18,
            MinWidth = 220
        };
        _valueBox.SelectAll();
        Grid.SetRow(_valueBox, 1);
        root.Children.Add(_valueBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var ok = new Button { Content = "Tamam", Width = 82, Height = 30, IsDefault = true };
        var cancel = new Button { Content = "İptal", Width = 82, Height = 30, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(_error,2);root.Children.Add(_error);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        Content = root;
        PreviewKeyDown += (_,e)=>{if(e.Key==Key.Escape){DialogResult=false;e.Handled=true;}};
        Loaded += (_, _) => {_valueBox.Focus();_valueBox.SelectAll();};
        _valueBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Accept();
                e.Handled = true;
            }
        };
    }

    private void Accept()
    {
        var error=_commit(_valueBox.Text);
        if(error is not null){_error.Text=error;_valueBox.Focus();_valueBox.SelectAll();return;}
        DialogResult=true;
    }
}
