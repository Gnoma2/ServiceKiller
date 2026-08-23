using System;
using System.Drawing;
using System.Windows.Forms;

namespace ServiceKillerV1.UI
{
    public sealed class TextPromptForm : Form
    {
        private readonly TextBox _text;
        public string Value { get { return (_text.Text ?? string.Empty).Trim(); } }

        public TextPromptForm(string title, string label, string initial)
        {
            Text = title;
            Theme.ApplyApplicationIcon(this);
            Width = 460;
            Height = 190;
            MinimumSize = new Size(360, 170);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(11f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.Dpi;

            Label prompt = Theme.MakeLabel(label, 9f, true, Theme.Text);
            prompt.SetBounds(18, 18, 405, 24);
            Controls.Add(prompt);

            _text = new TextBox();
            _text.SetBounds(18, 50, 405, 28);
            _text.Text = initial ?? string.Empty;
            Controls.Add(_text);

            Button ok = Theme.MakeButton("GUARDAR", true);
            ok.SetBounds(228, 96, 94, 36);
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            Button cancel = Theme.MakeButton("CANCELAR", false);
            cancel.SetBounds(330, 96, 94, 36);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
