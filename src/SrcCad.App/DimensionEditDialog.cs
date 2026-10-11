using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SrcCad.App;

public sealed class DimensionEditDialog : Window
{
    private readonly TextBox _valueBox;
    private readonly bool _allowZero;

    public double Value { get; private set; }

    public DimensionEditDialog(string title, double currentValue, string unit, bool allowZero = false)
    {
        _allowZero = allowZero;
        Title = title;
        Width = 320;
        Height = 165;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(16) };
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
            Text = currentValue.ToString("0.###", CultureInfo.CurrentCulture),
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
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => _valueBox.Focus();
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
        var text = _valueBox.Text.Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
            !double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            MessageBox.Show(this, "Geçerli bir sayı girin.", "SrcCad", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!double.IsFinite(value) || value < 0 || (!_allowZero && value == 0))
        {
            MessageBox.Show(this, _allowZero ? "Ölçü negatif olamaz." : "Ölçü sıfırdan büyük olmalıdır.", "SrcCad", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Value = value;
        DialogResult = true;
    }
}

