using System;
using System.Drawing;
using System.Windows.Forms;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.UI
{
    public sealed class TweakRowControl : UserControl
    {
        public const int StandardRowHeight = 24;
        public const int ResourceRowHeight = 32;
        private readonly CheckBox _check;
        private readonly Label _name;
        private readonly Label _state;
        private readonly Label _kind;
        private readonly Label _benefit;
        private readonly Label _impact;
        private readonly Label _modified;
        private readonly Button _runNow;
        private readonly Button _remove;
        private readonly TableLayoutPanel _table;
        private readonly Panel _selectionBar;
        private FlowLayoutPanel _actionsPanel;
        private bool _suppress;
        private bool _detailActive;
        private ApplicationInstallState _applicationInstallState;
        private ApplyMode _applyMode;
        private bool _modeSelectable;

        public bool ActionAvailable { get; private set; }
        public bool SelectableForCurrentMode { get { return ActionAvailable && _modeSelectable; } }
        public ApplicationInstallState ApplicationInstallState { get { return _applicationInstallState; } }

        public event EventHandler SelectionChanged;
        public event EventHandler RowActivated;
        public event EventHandler RunNowClicked;
        public event EventHandler RemoveClicked;

        public TweakDefinition Definition { get; private set; }

        public bool Selected
        {
            get { return _check.Checked; }
            set
            {
                _suppress = true;
                _check.Checked = value;
                _suppress = false;
            }
        }

        public TweakRowControl(TweakDefinition definition)
        {
            Definition = definition;
            ActionAvailable = !definition.IsProtectedInfo;
            _applyMode = ApplyMode.Persistent;
            _modeSelectable = !definition.IsProtectedInfo;
            _applicationInstallState = ApplicationInstallState.NotApplicable;
            bool showsResources = definition.IsApplication && definition.ChangeKind == ChangeKind.Temporary;
            Height = showsResources ? ResourceRowHeight : StandardRowHeight;
            MinimumSize = new Size(860, showsResources ? ResourceRowHeight : StandardRowHeight);
            BackColor = definition.IsProtectedInfo ? Theme.DisabledPanel : Theme.Panel;
            Margin = new Padding(0, 0, 0, 1);
            Cursor = definition.IsProtectedInfo ? Cursors.Default : Cursors.Hand;

            bool hasRunNow = definition.IsApplication && definition.ChangeKind == ChangeKind.Temporary;
            bool hasRemove = definition.IsCustomApplication && !definition.IsCustomStartupAction;
            // V1.1.2.11: todas las filas de APLICACIONES usan exactamente la misma
            // geometría de 8 columnas que su cabecera, aunque una fila persistente no
            // tenga botón en ACCIONES. Así los centros no cambian entre filas.
            _table = CreateTable(definition.IsApplication, definition.IsCustomApplication);
            if (definition.IsProtectedInfo && _table.ColumnStyles.Count > 0)
                _table.ColumnStyles[0].Width = 0f;
            Controls.Add(_table);

            _selectionBar = new Panel();
            _selectionBar.Dock = DockStyle.Left;
            _selectionBar.Width = 4;
            _selectionBar.BackColor = Theme.Accent;
            _selectionBar.Visible = false;
            Controls.Add(_selectionBar);
            _selectionBar.BringToFront();

            _check = new CheckBox();
            _check.Dock = DockStyle.Fill;
            _check.Margin = new Padding(1, 0, 3, 0);
            _check.Enabled = !definition.IsProtectedInfo;
            _check.Visible = !definition.IsProtectedInfo;
            _check.CheckedChanged += delegate
            {
                if (!_suppress && SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            };
            _table.Controls.Add(_check, 0, 0);

            _name = MakeCell(definition.Name, Theme.Text, true);
            _table.Controls.Add(_name, 1, 0);
            if (definition.IsCustomApplication && !string.IsNullOrWhiteSpace(definition.CustomProcessName))
            {
                ToolTip customTip = new ToolTip();
                customTip.SetToolTip(_name, "Proceso objetivo: " + definition.CustomProcessName + ".exe");
                _name.Tag = customTip;
            }

            _state = MakeCell("Leyendo...", Theme.Muted, false);
            _state.UseCompatibleTextRendering = true;
            _state.TextAlign = ContentAlignment.MiddleCenter;
            _table.Controls.Add(_state, 2, 0);

            _kind = MakeCell(KindText(definition.ChangeKind), KindColor(definition.ChangeKind), true);
            _kind.TextAlign = ContentAlignment.MiddleCenter;
            _table.Controls.Add(_kind, 3, 0);

            _benefit = MakeCell(BenefitText(definition.PerformanceBenefit), BenefitColor(definition.PerformanceBenefit), true);
            _benefit.TextAlign = ContentAlignment.MiddleCenter;
            _table.Controls.Add(_benefit, 4, 0);

            _impact = MakeCell(ImpactText(definition.Impact), ImpactColor(definition.Impact), true);
            _impact.TextAlign = ContentAlignment.MiddleCenter;
            _table.Controls.Add(_impact, 5, 0);

            _modified = MakeCell(definition.IsProtectedInfo ? "BLOQUEADO" : "NO", definition.IsProtectedInfo ? Theme.DisabledText : Theme.Muted, true);
            _modified.TextAlign = ContentAlignment.MiddleCenter;
            _table.Controls.Add(_modified, 6, 0);

            if (hasRunNow)
            {
                if (hasRemove)
                {
                    FlowLayoutPanel actions = new FlowLayoutPanel();
                    _actionsPanel = actions;
                    actions.Dock = DockStyle.Fill;
                    actions.FlowDirection = FlowDirection.LeftToRight;
                    actions.WrapContents = false;
                    actions.Padding = new Padding(3, 1, 0, 1);
                    actions.Margin = new Padding(0);
                    actions.BackColor = Theme.Panel;

                    _runNow = Theme.MakeButton("Cerrar", false);
                    _runNow.Width = 82;
                    _runNow.Height = 22;
                    _runNow.Margin = new Padding(0, 0, 6, 0);
                    _runNow.Click += delegate
                    {
                        if (RunNowClicked != null) RunNowClicked(this, EventArgs.Empty);
                    };
                    actions.Controls.Add(_runNow);

                    _remove = Theme.MakeButton("Quitar", false);
                    _remove.ForeColor = Theme.High;
                    _remove.Width = 82;
                    _remove.Height = 22;
                    _remove.Margin = new Padding(0);
                    _remove.Click += delegate
                    {
                        if (RemoveClicked != null) RemoveClicked(this, EventArgs.Empty);
                    };
                    actions.Controls.Add(_remove);
                    _table.Controls.Add(actions, 7, 0);
                }
                else
                {
                    _runNow = Theme.MakeButton("Cerrar ahora", false);
                    _runNow.Dock = DockStyle.Fill;
                    _runNow.Margin = new Padding(4, 1, 0, 1);
                    _runNow.Click += delegate
                    {
                        if (RunNowClicked != null) RunNowClicked(this, EventArgs.Empty);
                    };
                    _table.Controls.Add(_runNow, 7, 0);
                }
            }

            if (definition.IsProtectedInfo)
            {
                // V1.1.2.8: la sección protegida es puramente informativa.
                // Sin checkbox y con todas las columnas atenuadas para no sugerir interacción.
                _name.ForeColor = Theme.DisabledText;
                _state.ForeColor = Theme.DisabledText;
                _kind.ForeColor = Theme.DisabledText;
                _benefit.ForeColor = Theme.DisabledText;
                _impact.ForeColor = Theme.DisabledText;
                _modified.ForeColor = Theme.DisabledText;
                ApplyRowBackground();
            }

            HookClick(_table);
            HookClick(_name);
            HookClick(_state);
            HookClick(_kind);
            HookClick(_benefit);
            HookClick(_impact);
            HookClick(_modified);
        }

        public void SetApplyMode(ApplyMode mode)
        {
            _applyMode = mode;
            _modeSelectable = !Definition.IsProtectedInfo && (mode == ApplyMode.Persistent || Definition.SupportsUntilRestartMode());

            if (!_modeSelectable && _check.Checked)
            {
                _suppress = true;
                _check.Checked = false;
                _suppress = false;
            }

            if (mode == ApplyMode.UntilRestart && Definition.ChangeKind != ChangeKind.Temporary)
            {
                if (Definition.SupportsUntilRestartMode())
                {
                    _kind.Text = "HASTA REINICIO";
                    _kind.ForeColor = Theme.Low;
                }
                else
                {
                    _kind.Text = "NO APLICA";
                    _kind.ForeColor = Theme.DisabledText;
                }
            }
            else
            {
                _kind.Text = KindText(Definition.ChangeKind);
                _kind.ForeColor = KindColor(Definition.ChangeKind);
            }

            ApplyAvailabilityToCheck();
            if (Definition.IsProtectedInfo)
            {
                _check.Visible = false;
                _kind.ForeColor = Theme.DisabledText;
                _benefit.ForeColor = Theme.DisabledText;
                _impact.ForeColor = Theme.DisabledText;
                _modified.ForeColor = Theme.DisabledText;
                ApplyRowBackground();
            }
        }

        public void SetDetailActive(bool active)
        {
            _detailActive = active;
            ApplyRowBackground();
            _selectionBar.Visible = active && !Definition.IsProtectedInfo;
            if (_selectionBar.Visible) _selectionBar.BringToFront();
            Invalidate(true);
        }

        public static Control CreateApplicationColumnHeader(string firstColumnTitle, bool customActions)
        {
            Panel panel = new Panel();
            panel.Height = 32;
            panel.MinimumSize = new Size(860, 32);
            panel.BackColor = Theme.Panel2;
            panel.Margin = new Padding(0, 0, 0, 2);

            // Las filas de aplicaciones añaden una columna final de acciones.
            // customActions=true reserva el mismo ancho que Cerrar + Quitar.
            TableLayoutPanel table = CreateTable(true, customActions);
            table.Padding = new Padding(6, 0, 6, 0);
            table.BackColor = Theme.Panel2;
            panel.Controls.Add(table);

            Label empty = MakeHeaderCell("");
            Label first = MakeHeaderCell(string.IsNullOrWhiteSpace(firstColumnTitle) ? "ACCIÓN" : firstColumnTitle);
            first.TextAlign = ContentAlignment.MiddleLeft;
            Label state = MakeHeaderCell("ESTADO ACTUAL");
            Label kind = MakeHeaderCell("TIPO DE CAMBIO");
            Label benefit = MakeHeaderCell("BENEFICIO ESPERADO");
            Label impact = MakeHeaderCell("IMPACTO FUNCIONAL");
            Label modified = MakeHeaderCell("MODIFICADO POR SERVICEKILLER");
            Label actions = MakeHeaderCell("ACCIONES");

            table.Controls.Add(empty, 0, 0);
            table.Controls.Add(first, 1, 0);
            table.Controls.Add(state, 2, 0);
            table.Controls.Add(kind, 3, 0);
            table.Controls.Add(benefit, 4, 0);
            table.Controls.Add(impact, 5, 0);
            table.Controls.Add(modified, 6, 0);
            table.Controls.Add(actions, 7, 0);

            ToolTip tips = new ToolTip();
            tips.AutoPopDelay = 12000;
            tips.InitialDelay = 350;
            tips.ReshowDelay = 100;
            tips.SetToolTip(first, firstColumnTitle == "APLICACIÓN"
                ? "Aplicación personalizada que ServiceKiller puede cerrar al aplicar el boost."
                : "Acción que ServiceKiller puede realizar sobre la aplicación de este bloque.");
            tips.SetToolTip(state, "Indica si la aplicación está instalada y, cuando procede, si está ejecutándose. También muestra procesos asociados y RAM aproximada.");
            tips.SetToolTip(kind, "PERSISTENTE: modifica una configuración que permanece. TEMPORAL: cierre/acción de sesión. HASTA REINICIO: se auto-restaura en el siguiente logon.");
            tips.SetToolTip(benefit, "Estimación cualitativa de la reducción potencial de actividad en segundo plano. No equivale a FPS garantizados.");
            tips.SetToolTip(impact, "Cuánto puedes notar funcionalmente que la aplicación o característica quede cerrada/desactivada.");
            tips.SetToolTip(modified, "Indica si ServiceKiller ha registrado un cambio reversible para esa acción.");
            tips.SetToolTip(actions, "Acciones inmediatas disponibles, como Cerrar ahora o Quitar una aplicación personalizada de ServiceKiller.");
            panel.Tag = tips;

            return panel;
        }

        public static Control CreateColumnHeader()
        {
            Panel panel = new Panel();
            panel.Height = 32;
            panel.MinimumSize = new Size(860, 32);
            panel.BackColor = Theme.Panel2;
            panel.Margin = new Padding(0, 0, 0, 2);

            TableLayoutPanel table = CreateTable(false, false);
            table.Padding = new Padding(6, 0, 6, 0);
            table.BackColor = Theme.Panel2;
            panel.Controls.Add(table);

            Label empty = MakeHeaderCell("");
            Label function = MakeHeaderCell("FUNCIÓN");
            function.TextAlign = ContentAlignment.MiddleLeft;
            Label state = MakeHeaderCell("ESTADO ACTUAL");
            Label kind = MakeHeaderCell("TIPO DE CAMBIO");
            Label benefit = MakeHeaderCell("BENEFICIO ESPERADO");
            Label impact = MakeHeaderCell("IMPACTO FUNCIONAL");
            Label modified = MakeHeaderCell("MODIFICADO POR SERVICEKILLER");

            table.Controls.Add(empty, 0, 0);
            table.Controls.Add(function, 1, 0);
            table.Controls.Add(state, 2, 0);
            table.Controls.Add(kind, 3, 0);
            table.Controls.Add(benefit, 4, 0);
            table.Controls.Add(impact, 5, 0);
            table.Controls.Add(modified, 6, 0);

            ToolTip tips = new ToolTip();
            tips.AutoPopDelay = 12000;
            tips.InitialDelay = 350;
            tips.ReshowDelay = 100;
            tips.SetToolTip(kind, "PERSISTENTE: permanece tras reiniciar. TEMPORAL: cierre/acción de sesión. HASTA REINICIO: ServiceKiller lo auto-restaurará en el próximo logon tras reiniciar/cerrar sesión. REQUIERE REINICIO: necesita reiniciar para completar el efecto.");
            tips.SetToolTip(benefit, "Estimación cualitativa de la reducción potencial de actividad en segundo plano. No equivale a FPS garantizados ni sustituye un benchmark.");
            tips.SetToolTip(impact, "Cuánto puedes notar funcionalmente que esta característica quede desactivada: BAJO, MEDIO o ALTO.");
            tips.SetToolTip(modified, "Indica si ServiceKiller ha registrado este cambio: SÍ = journal persistente; SÍ · SESIÓN = journal temporal con auto-restauración programada.");
            panel.Tag = tips; // Mantiene vivo el ToolTip mientras exista la cabecera.

            return panel;
        }

        public void UpdateState(TweakRuntimeState state)
        {
            if (Definition.IsApplication && Definition.ChangeKind == ChangeKind.Temporary &&
                state.ApplicationInstallState == ApplicationInstallState.InstalledRunning)
            {
                string resourceLine = state.ApplicationProcessCount > 0
                    ? state.ApplicationProcessCount + " proc · " + (state.ApplicationMemoryMb > 0 ? FormatMemory(state.ApplicationMemoryMb) + " RAM" : "RAM n/d")
                    : "residencia/servicio activo";
                _state.Text = state.Summary + Environment.NewLine + resourceLine;
            }
            else
            {
                _state.Text = state.Summary;
            }
            _applicationInstallState = state.ApplicationInstallState;
            ActionAvailable = state.IsActionAvailable;

            if (state.IsAppliedByServiceKiller)
            {
                _modified.Text = state.IsSessionApplied ? "SÍ · SESIÓN" : "SÍ";
                _modified.ForeColor = state.IsSessionApplied ? Theme.Low : Theme.Modified;
            }
            else if (!Definition.IsProtectedInfo)
            {
                _modified.Text = "NO";
                _modified.ForeColor = Theme.Muted;
            }

            if (Definition.IsApplication)
                ApplyApplicationVisualState(state);
            else
            {
                ApplyAvailabilityToCheck();
                if (Definition.IsProtectedInfo)
                {
                    _check.Visible = false;
                    _name.ForeColor = Theme.DisabledText;
                    _state.ForeColor = Theme.DisabledText;
                    _kind.ForeColor = Theme.DisabledText;
                    _benefit.ForeColor = Theme.DisabledText;
                    _impact.ForeColor = Theme.DisabledText;
                    _modified.ForeColor = Theme.DisabledText;
                }
                ApplyRowBackground();
            }
        }

        private void ApplyApplicationVisualState(TweakRuntimeState state)
        {
            bool notInstalled = state.ApplicationInstallState == ApplicationInstallState.NotInstalled;
            bool notVerifiable = state.ApplicationInstallState == ApplicationInstallState.NotVerifiable;

            _check.Visible = !notInstalled;
            ApplyAvailabilityToCheck();
            if (_runNow != null)
                _runNow.Enabled = state.ApplicationInstallState == ApplicationInstallState.InstalledRunning;
            if (_remove != null) _remove.Enabled = true;

            if (notInstalled)
            {
                _name.ForeColor = Theme.DisabledText;
                _state.ForeColor = Theme.DisabledText;
                _kind.ForeColor = Theme.DisabledText;
                _benefit.ForeColor = Theme.DisabledText;
                _impact.ForeColor = Theme.DisabledText;
                _modified.ForeColor = Theme.DisabledText;
            }
            else
            {
                _name.ForeColor = Theme.Text;
                _state.ForeColor = notVerifiable ? Theme.Medium : Theme.Muted;
                if (_applyMode == ApplyMode.UntilRestart && Definition.ChangeKind != ChangeKind.Temporary)
                    _kind.ForeColor = Definition.SupportsUntilRestartMode() ? Theme.Low : Theme.DisabledText;
                else
                    _kind.ForeColor = KindColor(Definition.ChangeKind);
                _benefit.ForeColor = BenefitColor(Definition.PerformanceBenefit);
                _impact.ForeColor = ImpactColor(Definition.Impact);
                if (state.IsAppliedByServiceKiller) _modified.ForeColor = state.IsSessionApplied ? Theme.Low : Theme.Modified;
                else _modified.ForeColor = Theme.Muted;
            }

            ApplyRowBackground();
        }

        private void ApplyAvailabilityToCheck()
        {
            bool notInstalled = Definition.IsApplication && _applicationInstallState == ApplicationInstallState.NotInstalled;
            _check.Enabled = !Definition.IsProtectedInfo && !notInstalled && _modeSelectable;
        }

        private void ApplyRowBackground()
        {
            bool notInstalled = Definition.IsApplication && _applicationInstallState == ApplicationInstallState.NotInstalled;
            Color background;
            if (Definition.IsProtectedInfo) background = _detailActive ? Theme.DisabledSelectedRow : Theme.DisabledPanel;
            else if (notInstalled) background = _detailActive ? Theme.DisabledSelectedRow : Theme.DisabledPanel;
            else background = _detailActive ? Theme.SelectedRow : Theme.Panel;

            BackColor = background;
            _table.BackColor = background;
            if (_actionsPanel != null) _actionsPanel.BackColor = background;
        }

        private static TableLayoutPanel CreateTable(bool applicationLayout, bool wideActions)
        {
            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.RowCount = 1;
            table.ColumnCount = applicationLayout ? 8 : 7;
            table.Padding = new Padding(6, 0, 6, 0);
            table.BackColor = Theme.Panel;
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // V1.02.8: seis columnas semánticas, todas elásticas. Los títulos largos
            // pueden ocupar dos líneas sin sacrificar los valores de las filas.
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30f));
            if (applicationLayout)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f)); // función
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18f)); // estado actual
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14f)); // tipo de cambio
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13f)); // beneficio esperado
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13f)); // impacto funcional
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17f)); // modificado por SK
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, wideActions ? 194f : 116f));
            }
            else
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27f)); // función
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19f)); // estado actual
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14f)); // tipo de cambio
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13f)); // beneficio esperado
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 13f)); // impacto funcional
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14f)); // modificado por SK
            }
            return table;
        }

        private void HookClick(Control control)
        {
            control.Click += delegate
            {
                if (RowActivated != null) RowActivated(this, EventArgs.Empty);
            };
        }

        private static Label MakeCell(string text, Color color, bool bold)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = color;
            label.Font = Theme.UiFont(10.6f, bold ? FontStyle.Bold : FontStyle.Regular);
            label.Dock = DockStyle.Fill;
            label.Margin = new Padding(2, 0, 2, 0);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoEllipsis = true;
            return label;
        }

        private static Label MakeHeaderCell(string text)
        {
            Label label = MakeCell(text, Theme.Muted, true);
            label.Font = Theme.UiFont(9.7f, FontStyle.Bold);
            label.AutoEllipsis = false;
            label.UseCompatibleTextRendering = true;
            label.TextAlign = ContentAlignment.MiddleCenter;
            return label;
        }

        private static string ImpactText(ImpactLevel impact)
        {
            if (impact == ImpactLevel.Low) return "BAJO";
            if (impact == ImpactLevel.Medium) return "MEDIO";
            return "ALTO";
        }

        private static Color ImpactColor(ImpactLevel impact)
        {
            if (impact == ImpactLevel.Low) return Theme.Low;
            if (impact == ImpactLevel.Medium) return Theme.Medium;
            return Theme.High;
        }

        private static string BenefitText(PerformanceBenefitLevel benefit)
        {
            if (benefit == PerformanceBenefitLevel.None) return "NULO";
            if (benefit == PerformanceBenefitLevel.VeryLow) return "MUY BAJO";
            if (benefit == PerformanceBenefitLevel.Low) return "BAJO";
            if (benefit == PerformanceBenefitLevel.Medium) return "MEDIO";
            return "ALTO";
        }

        private static Color BenefitColor(PerformanceBenefitLevel benefit)
        {
            if (benefit == PerformanceBenefitLevel.None || benefit == PerformanceBenefitLevel.VeryLow) return Theme.Muted;
            if (benefit == PerformanceBenefitLevel.Low) return Theme.Modified;
            return Theme.Low;
        }


        private static string FormatMemory(long mb)
        {
            if (mb >= 1024) return (mb / 1024.0).ToString("0.0") + " GB";
            return mb + " MB";
        }

        private static string KindText(ChangeKind kind)
        {
            if (kind == ChangeKind.Temporary) return "TEMPORAL";
            if (kind == ChangeKind.RestartRequired) return "REINICIO";
            return "PERSISTENTE";
        }

        private static Color KindColor(ChangeKind kind)
        {
            if (kind == ChangeKind.Temporary) return Theme.Low;
            if (kind == ChangeKind.RestartRequired) return Theme.High;
            return Theme.Modified;
        }
    }
}
