using System.Windows.Forms;

namespace SrcCad.App;

// Panel is not selectable by default. The hosted native HWND must own keyboard
// input for Esc/Delete/Ctrl shortcuts, independently of the WPF focus scope.
internal sealed class CadViewportPanel : Panel
{
    public CadViewportPanel()
    {
        SetStyle(ControlStyles.Selectable,true);
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
