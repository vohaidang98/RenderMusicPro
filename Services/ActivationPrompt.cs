using System.Drawing;
using System.Windows.Forms;

namespace RenderMusicPro.Services;

/// <summary>Hộp nhập mã kích hoạt (chữ hiện dấu *). Chưa đúng mã thì không vào được app.</summary>
internal sealed class ActivationPrompt : Form
{
    private readonly TextBox _code = new();
    private readonly Label _message = new();

    private ActivationPrompt()
    {
        Text = "Kích hoạt Render Music Pro";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(400, 160);
        Font = new Font("Segoe UI", 9F);

        var prompt = new Label { Text = "Nhập mã kích hoạt để sử dụng Render Music Pro.", AutoSize = true, Location = new Point(20, 18) };
        _code.UseSystemPasswordChar = true;
        _code.Location = new Point(20, 48);
        _code.Width = 360;
        _message.ForeColor = Color.Firebrick;
        _message.AutoSize = true;
        _message.Location = new Point(20, 80);

        var ok = new Button { Text = "Kích hoạt", Location = new Point(202, 112), Size = new Size(84, 30) };
        var cancel = new Button { Text = "Thoát", Location = new Point(296, 112), Size = new Size(84, 30), DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Submit();
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange(new Control[] { prompt, _code, _message, ok, cancel });
    }

    private void Submit()
    {
        try
        {
            if (Licensing.Activate(_code.Text)) { DialogResult = DialogResult.OK; Close(); return; }
            _message.Text = "Mã không đúng. Hãy thử lại.";
        }
        catch (Exception ex)
        {
            _message.Text = "Không lưu được kích hoạt: " + ex.Message;
        }
        _code.Clear();
        _code.Focus();
    }

    /// <summary>Trả về true nếu đã kích hoạt (sẵn có hoặc vừa nhập đúng mã).</summary>
    public static bool EnsureActivated()
    {
        if (Licensing.IsActivated()) return true;
        using var form = new ActivationPrompt();
        return form.ShowDialog() == DialogResult.OK && Licensing.IsActivated();
    }
}
