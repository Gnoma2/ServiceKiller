using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.UI
{
    public sealed class ProcessAnalyzerForm : Form
    {
        private readonly ListView _list;
        private readonly List<ResidentProcessCandidate> _candidates;
        public List<ResidentProcessCandidate> SelectedCandidates { get; private set; }

        public ProcessAnalyzerForm(IEnumerable<ResidentProcessCandidate> candidates)
        {
            _candidates = candidates == null ? new List<ResidentProcessCandidate>() : candidates.ToList();
            SelectedCandidates = new List<ResidentProcessCandidate>();
            Text = "ServiceKiller - Analizador de procesos residentes";
            Theme.ApplyApplicationIcon(this);
            Width = 900;
            Height = 620;
            MinimumSize = new Size(620, 420);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(11f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.Dpi;

            Label title = Theme.MakeLabel("APLICACIONES / PROCESOS RESIDENTES DETECTADOS", 13f, true, Theme.Text);
            title.Dock = DockStyle.Top;
            title.Height = 46;
            title.Padding = new Padding(16, 12, 0, 0);
            Controls.Add(title);

            Label note = Theme.MakeLabel("ServiceKiller solo propone candidatos. Marca los que quieras añadir a Mis aplicaciones. No se cierra ni modifica nada desde esta pantalla.", 8.7f, false, Theme.Muted);
            note.Dock = DockStyle.Top;
            note.Height = 50;
            note.Padding = new Padding(16, 4, 16, 4);
            Controls.Add(note);

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.CheckBoxes = true;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.BackColor = Theme.Panel;
            _list.ForeColor = Theme.Text;
            _list.Columns.Add("Aplicación", 260);
            _list.Columns.Add("Proceso", 170);
            _list.Columns.Add("Procesos", 80);
            _list.Columns.Add("RAM", 90);
            _list.Columns.Add("Tipo", 180);
            Controls.Add(_list);

            foreach (ResidentProcessCandidate candidate in _candidates)
            {
                ListViewItem item = new ListViewItem(candidate.DisplayName ?? candidate.ProcessName);
                item.SubItems.Add((candidate.ProcessName ?? string.Empty) + ".exe");
                item.SubItems.Add(candidate.ProcessCount.ToString());
                item.SubItems.Add(candidate.MemoryMb + " MB");
                item.SubItems.Add(candidate.Note ?? string.Empty);
                item.Tag = candidate;
                _list.Items.Add(item);
            }

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 68;
            footer.BackColor = Theme.Panel2;
            footer.Padding = new Padding(14);
            Controls.Add(footer);

            Button add = Theme.MakeButton("AÑADIR SELECCIONADOS", true);
            add.Dock = DockStyle.Right;
            add.Width = 200;
            add.Click += delegate
            {
                SelectedCandidates = _list.CheckedItems.Cast<ListViewItem>().Select(delegate(ListViewItem i) { return i.Tag as ResidentProcessCandidate; }).Where(delegate(ResidentProcessCandidate c) { return c != null; }).ToList();
                DialogResult = DialogResult.OK;
                Close();
            };
            footer.Controls.Add(add);

            Button cancel = Theme.MakeButton("CERRAR", false);
            cancel.Dock = DockStyle.Right;
            cancel.Width = 110;
            cancel.Margin = new Padding(0, 0, 10, 0);
            cancel.DialogResult = DialogResult.Cancel;
            footer.Controls.Add(cancel);
        }
    }
}
