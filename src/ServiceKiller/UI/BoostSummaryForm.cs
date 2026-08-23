using System;
using System.Drawing;
using System.Windows.Forms;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.UI
{
    public sealed class BoostSummaryData
    {
        public string Profile { get; set; }
        public string Mode { get; set; }
        public int SelectedActions { get; set; }
        public int AppliedActions { get; set; }
        public int NoChangeActions { get; set; }
        public int SkippedActions { get; set; }
        public int ErrorActions { get; set; }
        public int PersistentChanges { get; set; }
        public int TemporaryActions { get; set; }
        public int ProcessesClosed { get; set; }
        public int ServicesStopped { get; set; }
        public int WindowsServicesStopped { get; set; }
        public bool RestartRequired { get; set; }
        public string RestartStatus { get; set; }
        public long DurationMilliseconds { get; set; }
        public SystemMetrics Before { get; set; }
        public SystemMetrics After { get; set; }
        public string DetailText { get; set; }
    }

    /// <summary>
    /// V1.03: resumen objetivo de una aplicación de boost. Los valores de procesos,
    /// servicios y RAM son fotografías antes/después, no una promesa de FPS.
    /// </summary>
    public sealed class BoostSummaryForm : Form
    {
        public BoostSummaryForm(BoostSummaryData data)
        {
            if (data == null) data = new BoostSummaryData();

            Text = "ServiceKiller - resumen del boost";
            Theme.ApplyApplicationIcon(this);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Width = 860;
            Height = 720;
            MinimumSize = new Size(560, 440);
            AutoScroll = true;
            AutoScrollMinSize = new Size(560, 560);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(11f, FontStyle.Regular);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(22, 18, 22, 18);
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 168f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 168f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
            Controls.Add(root);

            Label title = Theme.MakeLabel(data.ErrorActions > 0 ? "BOOST TERMINADO CON INCIDENCIAS" : "BOOST COMPLETADO", 18f, true, data.ErrorActions > 0 ? Theme.Medium : Theme.Low);
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(title, 0, 0);

            Label mode = Theme.MakeLabel("Perfil: " + Safe(data.Profile, "—") + "   ·   Modo: " + Safe(data.Mode, "—"), 9.5f, true, Theme.Text);
            mode.Dock = DockStyle.Fill;
            mode.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(mode, 0, 1);

            Panel resultsPanel = Card();
            root.Controls.Add(resultsPanel, 0, 2);
            resultsPanel.Controls.Add(BuildResults(data));

            Panel metricsPanel = Card();
            root.Controls.Add(metricsPanel, 0, 3);
            metricsPanel.Controls.Add(BuildMetrics(data));

            TextBox detail = new TextBox();
            detail.Dock = DockStyle.Fill;
            detail.Multiline = true;
            detail.ReadOnly = true;
            detail.ScrollBars = ScrollBars.Both;
            detail.WordWrap = false;
            detail.BackColor = Color.FromArgb(16, 18, 21);
            detail.ForeColor = Theme.Text;
            detail.BorderStyle = BorderStyle.FixedSingle;
            detail.Font = new Font("Consolas", 8.8f, FontStyle.Regular);
            detail.Text = "DETALLE DE ACCIONES" + Environment.NewLine + new string('─', 76) + Environment.NewLine + (data.DetailText ?? string.Empty);
            root.Controls.Add(detail, 0, 4);

            Button close = Theme.MakeButton("CERRAR", true);
            close.Width = 150;
            close.Dock = DockStyle.Right;
            close.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.Padding = new Padding(0, 6, 0, 6);
            footer.Controls.Add(close);
            root.Controls.Add(footer, 0, 5);
        }

        private static Control BuildResults(BoostSummaryData data)
        {
            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.Padding = new Padding(14, 10, 14, 10);
            table.ColumnCount = 4;
            table.RowCount = 3;
            for (int i = 0; i < 4; i++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            for (int i = 0; i < 3; i++) table.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));

            AddMetric(table, 0, 0, "SELECCIONADAS", data.SelectedActions.ToString(), Theme.Text);
            AddMetric(table, 1, 0, "APLICADAS", data.AppliedActions.ToString(), Theme.Low);
            AddMetric(table, 2, 0, "SIN CAMBIO", data.NoChangeActions.ToString(), Theme.Muted);
            AddMetric(table, 3, 0, "OMITIDAS", data.SkippedActions.ToString(), Theme.Muted);
            AddMetric(table, 0, 1, "ERRORES", data.ErrorActions.ToString(), data.ErrorActions > 0 ? Theme.High : Theme.Low);
            AddMetric(table, 1, 1, "PROCESOS CERRADOS", data.ProcessesClosed.ToString(), Theme.Modified);
            AddMetric(table, 2, 1, "SERVICIOS WIN / APPS", data.WindowsServicesStopped + " / " + data.ServicesStopped, Theme.Modified);
            AddMetric(table, 3, 1, "TIEMPO DEL BOOST", FormatDuration(data.DurationMilliseconds), Theme.Modified);
            AddMetric(table, 0, 2, "CAMBIOS CON JOURNAL", data.PersistentChanges.ToString(), Theme.Modified);
            AddMetric(table, 1, 2, "ACCIONES TEMPORALES", data.TemporaryActions.ToString(), Theme.Modified);
            string restartStatus = Safe(data.RestartStatus, data.RestartRequired ? "NECESARIO" : "NO");
            Color restartColor = string.Equals(restartStatus, "NO", StringComparison.OrdinalIgnoreCase) ? Theme.Low : Theme.Medium;
            AddMetric(table, 2, 2, "REINICIO", restartStatus, restartColor);
            AddMetric(table, 3, 2, "RESULTADO", data.ErrorActions > 0 ? "INCIDENCIAS" : "OK", data.ErrorActions > 0 ? Theme.Medium : Theme.Low);
            return table;
        }

        private static Control BuildMetrics(BoostSummaryData data)
        {
            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.Padding = new Padding(14, 8, 14, 8);
            table.ColumnCount = 4;
            table.RowCount = 5;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            for (int i = 1; i < 5; i++) table.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));

            AddTableCell(table, "SISTEMA", 0, 0, Theme.Text, true);
            AddTableCell(table, "ANTES", 1, 0, Theme.Muted, true);
            AddTableCell(table, "DESPUÉS", 2, 0, Theme.Muted, true);
            AddTableCell(table, "CAMBIO", 3, 0, Theme.Muted, true);

            SystemMetrics before = data.Before ?? new SystemMetrics();
            SystemMetrics after = data.After ?? new SystemMetrics();
            AddComparisonRow(table, 1, "Servicios ejecutándose", before.RunningServices.ToString(), after.RunningServices.ToString(), Delta(before.RunningServices, after.RunningServices, ""));
            AddComparisonRow(table, 2, "Procesos", before.Processes.ToString(), after.Processes.ToString(), Delta(before.Processes, after.Processes, ""));
            AddComparisonRow(table, 3, "RAM usada", FormatMb(before.UsedMemoryMb), FormatMb(after.UsedMemoryMb), DeltaMb(before.UsedMemoryMb, after.UsedMemoryMb));
            string availableDelta = DeltaMbPositiveGood(before.AvailableMemoryMb, after.AvailableMemoryMb);
            AddTableCell(table, "RAM disponible", 0, 4, Theme.Text, true);
            AddTableCell(table, FormatMb(before.AvailableMemoryMb), 1, 4, Theme.Muted, false);
            AddTableCell(table, FormatMb(after.AvailableMemoryMb), 2, 4, Theme.Text, true);
            AddTableCell(table, availableDelta, 3, 4, PositiveDeltaColor(availableDelta), true);
            return table;
        }

        private static void AddMetric(TableLayoutPanel table, int col, int row, string caption, string value, Color valueColor)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Margin = new Padding(4);
            Label cap = Theme.MakeLabel(caption, 7.7f, true, Theme.Muted);
            cap.Dock = DockStyle.Top;
            cap.Height = 22;
            Label val = Theme.MakeLabel(value, 15f, true, valueColor);
            val.Dock = DockStyle.Fill;
            val.TextAlign = ContentAlignment.TopLeft;
            panel.Controls.Add(val);
            panel.Controls.Add(cap);
            table.Controls.Add(panel, col, row);
        }

        private static void AddComparisonRow(TableLayoutPanel table, int row, string name, string before, string after, string delta)
        {
            AddTableCell(table, name, 0, row, Theme.Text, true);
            AddTableCell(table, before, 1, row, Theme.Muted, false);
            AddTableCell(table, after, 2, row, Theme.Text, true);
            AddTableCell(table, delta, 3, row, DeltaColor(delta), true);
        }

        private static void AddTableCell(TableLayoutPanel table, string text, int col, int row, Color color, bool bold)
        {
            Label label = Theme.MakeLabel(text, 8.6f, bold, color);
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            table.Controls.Add(label, col, row);
        }

        private static Panel Card()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Margin = new Padding(0, 4, 0, 8);
            panel.BackColor = Theme.Panel;
            return panel;
        }

        private static string Delta(int before, int after, string suffix)
        {
            int difference = after - before;
            if (difference == 0) return "0" + suffix;
            return (difference > 0 ? "+" : "") + difference + suffix;
        }

        private static string DeltaMb(long before, long after)
        {
            long difference = after - before;
            if (difference == 0) return "0 MB";
            return (difference > 0 ? "+" : "") + difference + " MB";
        }


        private static string DeltaMbPositiveGood(long before, long after)
        {
            long difference = after - before;
            if (difference == 0) return "0 MB";
            return (difference > 0 ? "+" : "") + difference + " MB";
        }

        private static string FormatDuration(long milliseconds)
        {
            if (milliseconds < 0) milliseconds = 0;
            if (milliseconds < 1000) return milliseconds + " ms";
            return (milliseconds / 1000.0).ToString("0.0") + " s";
        }


        private static Color PositiveDeltaColor(string delta)
        {
            if (string.IsNullOrWhiteSpace(delta) || delta.StartsWith("0", StringComparison.Ordinal)) return Theme.Muted;
            return delta.StartsWith("+", StringComparison.Ordinal) ? Theme.Low : Theme.Medium;
        }

        private static Color DeltaColor(string delta)
        {
            if (string.IsNullOrWhiteSpace(delta) || delta.StartsWith("0", StringComparison.Ordinal)) return Theme.Muted;
            // En estas tres métricas un valor negativo normalmente significa menos actividad.
            return delta.StartsWith("-", StringComparison.Ordinal) ? Theme.Low : Theme.Medium;
        }

        private static string FormatMb(long mb)
        {
            if (mb >= 1024) return (mb / 1024.0).ToString("0.0") + " GB";
            return mb + " MB";
        }

        private static string Safe(string text, string fallback)
        {
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }
    }
}
