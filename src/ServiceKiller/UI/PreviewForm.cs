using System;
using System.Drawing;
using System.Windows.Forms;

namespace ServiceKillerV1.UI
{
    public sealed class PreviewForm : Form
    {
        private readonly TextBox _text;
        private readonly Button _confirm;
        private readonly Button _cancel;

        public PreviewForm(string title, string content, string confirmText)
        {
            Text = title;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterParent;
            Width = 820;
            Height = 620;
            MinimumSize = new Size(460, 320);
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(11.0f, FontStyle.Regular);
            ShowIcon = false;
            MaximizeBox = false;

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 74;
            header.BackColor = Theme.Panel;
            Controls.Add(header);

            Label heading = Theme.MakeLabel(title, 15f, true, Theme.Text);
            heading.Location = new Point(20, 14);
            header.Controls.Add(heading);

            Label sub = Theme.MakeLabel("Nada se ejecutará hasta que confirmes.", 9f, false, Theme.Muted);
            sub.Location = new Point(22, 43);
            header.Controls.Add(sub);

            _text = new TextBox();
            _text.Multiline = true;
            _text.ReadOnly = true;
            _text.ScrollBars = ScrollBars.Both;
            _text.WordWrap = false;
            _text.Dock = DockStyle.Fill;
            _text.BackColor = Theme.Panel2;
            _text.ForeColor = Theme.Text;
            _text.BorderStyle = BorderStyle.None;
            _text.Font = new Font("Consolas", 9.5f, FontStyle.Regular);
            _text.Text = content;
            _text.Margin = new Padding(16);
            Controls.Add(_text);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 66;
            footer.BackColor = Theme.Panel;
            footer.Padding = new Padding(16, 14, 16, 14);
            Controls.Add(footer);

            _cancel = Theme.MakeButton("Cancelar", false);
            _cancel.Width = 120;
            _cancel.Dock = DockStyle.Right;
            _cancel.DialogResult = DialogResult.Cancel;
            footer.Controls.Add(_cancel);

            Panel spacer = new Panel();
            spacer.Dock = DockStyle.Right;
            spacer.Width = 10;
            footer.Controls.Add(spacer);

            _confirm = Theme.MakeButton(confirmText, true);
            _confirm.Width = confirmText != null && confirmText.Length > 14 ? 260 : 180;
            _confirm.Dock = DockStyle.Right;
            _confirm.DialogResult = DialogResult.OK;
            footer.Controls.Add(_confirm);

            AcceptButton = _confirm;
            CancelButton = _cancel;
        }
    }
}
