using System.Windows.Forms;

namespace SrcCad.App;

// Panel is not selectable by default. The hosted native HWND must own keyboard
// input for Esc/Delete/Ctrl shortcuts, independently of the WPF focus scope.
internal sealed class CadViewportPanel : Panel
{
    internal System.Func<Keys, bool>? CommandKey { get; set; }

    // Ctrl shortcuts are command keys and may be consumed before KeyDown.
    // Returning true prevents a second dispatch through KeyDown or the host.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        => CommandKey?.Invoke(keyData) == true || base.ProcessCmdKey(ref msg, keyData);

    public CadViewportPanel()
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick,true);
        TabStop=true;
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
    }
    protected override bool IsInputKey(Keys keyData)
        => (keyData & Keys.KeyCode) is Keys.Delete or Keys.Escape or Keys.Space || base.IsInputKey(keyData);
}
