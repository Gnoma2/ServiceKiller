using System;
using System.Drawing;
using System.Windows.Forms;

namespace ServiceKillerV1.UI
{
    public sealed class DiagnosticForm : Form
    {
        private readonly TextBox _text;
        private readonly Button _copy;

        public DiagnosticForm(string report)
        {
            Text = "ServiceKiller - diagnóstico anonimizado / verificación";
            Theme.ApplyApplicationIcon(this);
            StartPosition = FormStartPosition.CenterParent;
            Width = 980;
            Height = 720;
            MinimumSize = new Size(600, 400);
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(11f, FontStyle.Regular);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));
            Controls.Add(root);

            Label title = Theme.MakeLabel("DIAGNÓSTICO ANONIMIZADO PARA SOPORTE", 14f, true, Theme.Text);
            title.Dock = DockStyle.Fill;
            title.Padding = new Padding(14, 10, 0, 0);
            root.Controls.Add(title, 0, 0);

            Label help = Theme.MakeLabel("Incluye journals, tarea de restauración, estado actual y fragmentos de LOG. ServiceKiller oculta automáticamente nombres de cuenta/equipo, SID, rutas de perfil y emails detectables. La anonimización es best effort: revisa el texto antes de publicarlo.", 8.6f, false, Theme.Muted);
            help.Dock = DockStyle.Fill;
            help.Padding = new Padding(14, 0, 14, 6);
            help.AutoSize = false;
            root.Controls.Add(help, 0, 1);

            _text = new TextBox();
            _text.Dock = DockStyle.Fill;
            _text.Margin = new Padding(12, 0, 12, 0);
            _text.Multiline = true;
            _text.ReadOnly = true;
            _text.WordWrap = false;
            _text.ScrollBars = ScrollBars.Both;
            _text.BackColor = Color.FromArgb(12, 14, 17);
            _text.ForeColor = Theme.Text;
            _text.BorderStyle = BorderStyle.FixedSingle;
            _text.Font = new Font("Consolas", 9.5f, FontStyle.Regular);
            _text.Text = report ?? string.Empty;
            root.Controls.Add(_text, 0, 2);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.Padding = new Padding(12, 10, 12, 10);
            footer.BackColor = Theme.Panel;
            root.Controls.Add(footer, 0, 3);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Right;
            buttons.AutoSize = true;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.WrapContents = false;
            footer.Controls.Add(buttons);

            _copy = Theme.MakeButton("COPIAR TODO", true);
            _copy.Width = 150;
            _copy.Click += CopyAll;
            buttons.Controls.Add(_copy);

            Button close = Theme.MakeButton("CERRAR", false);
            close.Width = 110;
            close.Click += delegate { Close(); };
            buttons.Controls.Add(close);
        }

        private void CopyAll(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(_text.Text ?? string.Empty);
                _copy.Text = "COPIADO ✓";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo copiar el diagnóstico:\r\n" + ex.Message, "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
