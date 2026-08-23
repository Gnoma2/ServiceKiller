using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ServiceKillerV1.Core;
using ServiceKillerV1.Data;
using ServiceKillerV1.Models;

namespace ServiceKillerV1.UI
{
    public sealed class MainForm : Form
    {
        private readonly bool _isAdministrator;
        private readonly Logger _log;
        private readonly StateStore _store;
        private readonly TweakEngine _engine;
        private readonly StateStore _sessionStore;
        private readonly TweakEngine _sessionEngine;
        private readonly SystemMetricsReader _metricsReader;
        private readonly List<TweakDefinition> _catalog;
        private readonly CustomAppStore _customAppStore;
        private readonly List<CustomApplicationInfo> _customApplications;
        private readonly ProfileStore _profileStore;
        private readonly List<UserProfileInfo> _userProfiles;
        private readonly Dictionary<string, TweakRowControl> _rows;
        private readonly Dictionary<string, ApplicationCardVisual> _applicationCards;
        private TweakRowControl _activeDetailRow;
        private readonly Dictionary<string, CheckBox> _restoreChecks;
        private readonly Label _profileLabel;
        private readonly Label _selectionSummary;
        private readonly Label _metricsLabel;
        private readonly Label _detailTitle;
        private readonly Label _detailDescription;
        private readonly Label _detailConsequences;
        private readonly Label _detailTechnical;
        private readonly TextBox _logText;
        private readonly FlowLayoutPanel _restoreFlow;
        private readonly Button _applyButton;
        private readonly Button _previewButton;
        private Button _conservativeButton;
        private Button _balancedButton;
        private Button _aggressiveButton;
        private Button _refreshButton;
        private Button _diagnosticButton;
        private Button _persistentModeButton;
        private Button _sessionModeButton;
        private ComboBox _customProfileCombo;
        private Button _loadProfileButton;
        private Button _saveProfileButton;
        private Button _deleteProfileButton;
        private Button _analyzeProcessesButton;
        private Label _infoText;
        private Label _sessionStatusLabel;
        private string _activeUserProfileId;
        private bool _applyingPreset;
        private PresetKind _currentPreset;
        private ApplyMode _applyMode;
        private SystemMetrics _lastBefore;
        private FlowLayoutPanel _appFlow;
        private Panel _customDropZone;

        public MainForm(bool isAdministrator)
        {
            _isAdministrator = isAdministrator;
            _log = new Logger();
            _store = new StateStore(_log);
            _engine = new TweakEngine(_log, _store);
            _sessionStore = new StateStore(_log, AppPaths.SessionState, "session");
            _sessionEngine = new TweakEngine(_log, _sessionStore);
            _applyMode = ApplyMode.Persistent;
            _metricsReader = new SystemMetricsReader(_engine.Services);
            _customAppStore = new CustomAppStore(_log);
            _customApplications = _customAppStore.Load();
            _profileStore = new ProfileStore(_log);
            _userProfiles = _profileStore.Load();
            _catalog = TweakCatalog.Create();
            foreach (CustomApplicationInfo customApp in _customApplications)
            {
                _catalog.Add(CustomAppStore.ToTweak(customApp));
                _catalog.Add(CustomAppStore.ToStartupTweak(customApp));
            }
            _rows = new Dictionary<string, TweakRowControl>(StringComparer.OrdinalIgnoreCase);
            _applicationCards = new Dictionary<string, ApplicationCardVisual>(StringComparer.OrdinalIgnoreCase);
            _restoreChecks = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);

            Text = ServiceKillerV1.BuildInfo.DisplayName;
            Theme.ApplyApplicationIcon(this);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Width = 1440;
            Height = 860;
            MinimumSize = new Size(640, 440);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(11.0f, FontStyle.Regular);

            Label profileLabel;
            Label metricsLabel;
            Label detailTitle;
            Label detailDescription;
            Label detailConsequences;
            Label detailTechnical;
            TextBox logText;
            FlowLayoutPanel restoreFlow;
            Label selectionSummary;
            Button previewButton;
            Button applyButton;

            Panel header = BuildHeader(out profileLabel, out metricsLabel);
            TabControl tabs = BuildTabs(out detailTitle, out detailDescription, out detailConsequences, out detailTechnical, out logText, out restoreFlow);
            Panel footer = BuildFooter(out selectionSummary, out previewButton, out applyButton);

            _profileLabel = profileLabel;
            _metricsLabel = metricsLabel;
            _detailTitle = detailTitle;
            _detailDescription = detailDescription;
            _detailConsequences = detailConsequences;
            _detailTechnical = detailTechnical;
            _logText = logText;
            _restoreFlow = restoreFlow;
            _selectionSummary = selectionSummary;
            _previewButton = previewButton;
            _applyButton = applyButton;

            // Orden de docking deliberado: Fill primero, después barras inferior/superior.
            Controls.Add(tabs);
            Controls.Add(footer);
            Controls.Add(header);

            _log.LineWritten += OnLogLine;
            Load += OnLoaded;
            FormClosing += OnFormClosing;
        }

        private void ConfigureOptimizeSplit(SplitContainer split)
        {
            if (split == null || split.IsDisposed) return;
            int width = split.ClientSize.Width;
            if (width < 500) return;

            // El panel de detalle ocupa aproximadamente 27% con límites razonables.
            // Se calcula usando el ancho REAL ya asignado por WinForms/DPI.
            int detail = Math.Max(280, Math.Min(430, (int)(width * 0.27)));
            int distance = width - detail - split.SplitterWidth;
            int minDistance = Math.Min(620, Math.Max(280, width / 2));
            if (distance < minDistance) distance = minDistance;
            int maxDistance = Math.Max(100, width - 180);
            if (distance > maxDistance) distance = maxDistance;

            try
            {
                if (distance > 0 && distance < width) split.SplitterDistance = distance;
            }
            catch
            {
                // El layout nunca debe impedir que la aplicación arranque.
            }
        }

        private Panel BuildHeader(out Label profileLabel, out Label metricsLabel)
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 106;
            header.BackColor = Theme.Panel;
            header.Padding = new Padding(20, 10, 20, 10);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 3;
            layout.RowCount = 2;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));
            header.Controls.Add(layout);

            Label title = Theme.MakeLabel("SERVICEKILLER", 20f, true, Theme.Text);
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(title, 0, 0);

            Label version = Theme.MakeLabel(ServiceKillerV1.BuildInfo.DisplayName.Replace("ServiceKiller ", string.Empty) + " · APIs Windows · restauración reversible · diagnóstico", 11f, true, Theme.Modified);
            version.Dock = DockStyle.Fill;
            version.TextAlign = ContentAlignment.TopLeft;
            layout.Controls.Add(version, 0, 1);

            TableLayoutPanel profileStack = new TableLayoutPanel();
            profileStack.Dock = DockStyle.Fill;
            profileStack.ColumnCount = 1;
            profileStack.RowCount = 2;
            profileStack.RowStyles.Add(new RowStyle(SizeType.Percent, 58f));
            profileStack.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));
            layout.Controls.Add(profileStack, 1, 0);

            profileLabel = Theme.MakeLabel("Perfil: —", 12f, true, Theme.Text);
            profileLabel.AutoSize = false;
            profileLabel.Dock = DockStyle.Fill;
            profileLabel.TextAlign = ContentAlignment.BottomRight;
            profileStack.Controls.Add(profileLabel, 0, 0);

            _sessionStatusLabel = Theme.MakeLabel("● SESIÓN TEMPORAL ACTIVA", 10.4f, true, Theme.Low);
            _sessionStatusLabel.AutoSize = false;
            _sessionStatusLabel.Dock = DockStyle.Fill;
            _sessionStatusLabel.TextAlign = ContentAlignment.TopRight;
            _sessionStatusLabel.Visible = false;
            profileStack.Controls.Add(_sessionStatusLabel, 0, 1);

            metricsLabel = Theme.MakeLabel("Leyendo estado del sistema...", 10.7f, false, Theme.Muted);
            metricsLabel.AutoSize = false;
            metricsLabel.AutoEllipsis = true;
            metricsLabel.Dock = DockStyle.Fill;
            metricsLabel.TextAlign = ContentAlignment.TopRight;
            layout.Controls.Add(metricsLabel, 1, 1);

            TableLayoutPanel adminLine = new TableLayoutPanel();
            adminLine.Dock = DockStyle.Fill;
            adminLine.ColumnCount = 2;
            adminLine.RowCount = 1;
            adminLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68f));
            adminLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32f));
            layout.Controls.Add(adminLine, 2, 0);

            Label admin = Theme.MakeLabel(
                _isAdministrator ? "● ADMINISTRADOR" : "● SOLO LECTURA\r\nUAC al aplicar/restaurar",
                10.8f, true, _isAdministrator ? Theme.Low : Theme.Medium);
            admin.AutoSize = false;
            admin.Dock = DockStyle.Fill;
            admin.TextAlign = ContentAlignment.MiddleRight;
            adminLine.Controls.Add(admin, 0, 0);

            _diagnosticButton = Theme.MakeButton("DIAGNÓSTICO", false);
            _diagnosticButton.Dock = DockStyle.Fill;
            _diagnosticButton.Margin = new Padding(8, 4, 0, 4);
            _diagnosticButton.Font = Theme.UiFont(9.8f, FontStyle.Bold);
            _diagnosticButton.Click += delegate { ShowDiagnosticReport(); };
            ToolTip diagnosticTip = new ToolTip();
            diagnosticTip.SetToolTip(_diagnosticButton, "Genera un informe anonimizado para soporte con journals, restauración, estados actuales y fragmentos de LOG.");
            _diagnosticButton.Tag = diagnosticTip;
            adminLine.Controls.Add(_diagnosticButton, 1, 0);

            _refreshButton = Theme.MakeButton("↻ REFRESCAR ESTADO", false);
            _refreshButton.Dock = DockStyle.Fill;
            _refreshButton.Margin = new Padding(22, 2, 0, 2);
            _refreshButton.Click += delegate { ManualRefresh(); };
            layout.Controls.Add(_refreshButton, 2, 1);

            // V1.1: la cabecera se adapta al ancho en lugar de imponer un mínimo oculto.
            // A tamaños compactos se acortan solo textos auxiliares; las funciones siguen accesibles.
            header.SizeChanged += delegate
            {
                int w = header.ClientSize.Width;
                if (w < 760)
                {
                    version.Text = ServiceKillerV1.BuildInfo.DisplayName.Replace("ServiceKiller ", string.Empty) + " · Windows 11 validado";
                    admin.Text = _isAdministrator ? "● ADMIN" : "● SOLO LECTURA";
                    _refreshButton.Text = "↻";
                    _refreshButton.Margin = new Padding(6, 2, 0, 2);
                    if (_diagnosticButton != null) _diagnosticButton.Text = "i";
                }
                else if (w < 980)
                {
                    version.Text = ServiceKillerV1.BuildInfo.DisplayName.Replace("ServiceKiller ", string.Empty) + " · Windows 11 validado · icono integrado";
                    admin.Text = _isAdministrator ? "● ADMINISTRADOR" : "● SOLO LECTURA";
                    _refreshButton.Text = "↻ REFRESCAR";
                    _refreshButton.Margin = new Padding(10, 2, 0, 2);
                    if (_diagnosticButton != null) _diagnosticButton.Text = "DIAG";
                }
                else
                {
                    version.Text = ServiceKillerV1.BuildInfo.DisplayName.Replace("ServiceKiller ", string.Empty) + " · APIs Windows · restauración reversible · diagnóstico";
                    admin.Text = _isAdministrator ? "● ADMINISTRADOR" : "● SOLO LECTURA\r\nUAC al aplicar/restaurar";
                    _refreshButton.Text = "↻ REFRESCAR ESTADO";
                    _refreshButton.Margin = new Padding(22, 2, 0, 2);
                    if (_diagnosticButton != null) _diagnosticButton.Text = "DIAGNÓSTICO";
                }
            };

            return header;
        }

        private TabControl BuildTabs(out Label detailTitle, out Label detailDescription, out Label detailConsequences, out Label detailTechnical, out TextBox logText, out FlowLayoutPanel restoreFlow)
        {
            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Font = Theme.UiFont(11.5f, FontStyle.Bold);
            tabs.Padding = new Point(16, 6);
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.Multiline = true;
            tabs.SizeMode = TabSizeMode.FillToRight;
            tabs.ItemSize = new Size(118, 36);
            tabs.DrawItem += DrawTab;

            TabPage optimize = MakeTab("OPTIMIZACIÓN");
            TabPage apps = MakeTab("APLICACIONES");
            TabPage restore = MakeTab("RESTAURAR");
            TabPage log = MakeTab("LOG");
            TabPage info = MakeTab("INFO");
            tabs.TabPages.Add(optimize);
            tabs.TabPages.Add(apps);
            tabs.TabPages.Add(restore);
            tabs.TabPages.Add(log);
            tabs.TabPages.Add(info);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Vertical;
            split.SplitterWidth = 6;
            split.FixedPanel = FixedPanel.Panel2;
            split.BackColor = Theme.Border;
            // V1.02.1+: NO fijar SplitterDistance/PanelMinSize antes de que WinForms
            // haya calculado el tamaño real del control. A DPI alto el SplitContainer
            // todavía puede medir ~150 px durante el constructor y una distancia de
            // 1040 px puede abortar la creación de MainForm.
            optimize.Controls.Add(split);
            split.HandleCreated += delegate { ConfigureOptimizeSplit(split); };
            split.SizeChanged += delegate { ConfigureOptimizeSplit(split); };

            Panel presetPanel = BuildPresetPanel(split);

            FlowLayoutPanel optimizeFlow = new FlowLayoutPanel();
            optimizeFlow.Dock = DockStyle.Fill;
            optimizeFlow.FlowDirection = FlowDirection.TopDown;
            optimizeFlow.WrapContents = false;
            optimizeFlow.AutoScroll = true;
            optimizeFlow.BackColor = Theme.Back;
            optimizeFlow.Padding = new Padding(8, 2, 8, 8);
            split.Panel1.Controls.Add(optimizeFlow);
            split.Panel1.Controls.Add(presetPanel);

            optimizeFlow.Controls.Add(TweakRowControl.CreateColumnHeader());
            BuildTweakRows(optimizeFlow, delegate(TweakDefinition t) { return !t.IsApplication && !t.IsProtectedInfo; });
            BuildProtectedSection(optimizeFlow);
            ConfigureResponsiveFlow(optimizeFlow, 760);

            Panel details = BuildDetailsPanel(out detailTitle, out detailDescription, out detailConsequences, out detailTechnical);
            split.Panel2.Controls.Add(details);

            FlowLayoutPanel appFlow = new FlowLayoutPanel();
            _appFlow = appFlow;
            appFlow.Dock = DockStyle.Fill;
            appFlow.FlowDirection = FlowDirection.TopDown;
            appFlow.WrapContents = false;
            appFlow.AutoScroll = true;
            appFlow.BackColor = Theme.Back;
            appFlow.Padding = new Padding(12, 4, 12, 8);
            apps.Controls.Add(appFlow);

            Panel appTools = new Panel();
            appTools.Dock = DockStyle.Top;
            appTools.Height = 42;
            appTools.BackColor = Theme.Panel2;
            appTools.Padding = new Padding(12, 4, 12, 4);
            apps.Controls.Add(appTools);

            _analyzeProcessesButton = Theme.MakeButton("ANALIZAR PROCESOS RESIDENTES", false);
            _analyzeProcessesButton.Dock = DockStyle.Left;
            _analyzeProcessesButton.Width = 245;
            _analyzeProcessesButton.Click += delegate { AnalyzeResidentProcesses(); };
            appTools.Controls.Add(_analyzeProcessesButton);

            Label analyzerNote = Theme.MakeLabel("Busca aplicaciones activas que podrías añadir a Mis aplicaciones. Solo propone: no cierra nada.", 10.5f, false, Theme.Muted);
            analyzerNote.Dock = DockStyle.Fill;
            analyzerNote.TextAlign = ContentAlignment.MiddleLeft;
            analyzerNote.Padding = new Padding(16, 0, 0, 0);
            appTools.Controls.Add(analyzerNote);

            BuildApplicationCards(appFlow);
            ConfigureResponsiveFlow(appFlow, 920);

            restoreFlow = new FlowLayoutPanel();
            restoreFlow.Dock = DockStyle.Fill;
            restoreFlow.FlowDirection = FlowDirection.TopDown;
            restoreFlow.WrapContents = false;
            restoreFlow.AutoScroll = true;
            restoreFlow.BackColor = Theme.Back;
            restoreFlow.Padding = new Padding(18, 16, 18, 100);
            restore.Controls.Add(restoreFlow);
            ConfigureResponsiveFlow(restoreFlow, 820);

            Panel restoreFooter = new Panel();
            restoreFooter.Dock = DockStyle.Bottom;
            restoreFooter.Height = 72;
            restoreFooter.BackColor = Theme.Panel;
            restoreFooter.Padding = new Padding(18, 16, 18, 16);
            restore.Controls.Add(restoreFooter);

            Button restoreAll = Theme.MakeButton(_isAdministrator ? "RESTAURAR TODO PENDIENTE" : "RESTAURAR TODO PENDIENTE (ADMIN)", true);
            restoreAll.Width = _isAdministrator ? 250 : 300;
            restoreAll.Dock = DockStyle.Right;
            restoreAll.Click += delegate { RestoreAll(); };
            restoreFooter.Controls.Add(restoreAll);

            Panel restoreSpacer = new Panel();
            restoreSpacer.Dock = DockStyle.Right;
            restoreSpacer.Width = 10;
            restoreFooter.Controls.Add(restoreSpacer);

            Button restoreSelected = Theme.MakeButton(_isAdministrator ? "RESTAURAR SELECCIONADOS" : "RESTAURAR SELECCIONADOS (ADMIN)", false);
            restoreSelected.Width = _isAdministrator ? 230 : 270;
            restoreSelected.Dock = DockStyle.Right;
            restoreSelected.Click += delegate { RestoreSelected(); };
            restoreFooter.Controls.Add(restoreSelected);

            logText = new TextBox();
            logText.Dock = DockStyle.Fill;
            logText.Multiline = true;
            logText.ReadOnly = true;
            logText.ScrollBars = ScrollBars.Both;
            logText.WordWrap = false;
            logText.BackColor = Color.FromArgb(16, 18, 21);
            logText.ForeColor = Theme.Text;
            logText.BorderStyle = BorderStyle.None;
            logText.Font = new Font("Consolas", 9f, FontStyle.Regular);
            log.Controls.Add(logText);

            BuildInfo(info);
            return tabs;
        }

        private Panel BuildPresetPanel(SplitContainer split)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Top;
            panel.Height = 248;
            panel.BackColor = Theme.Panel2;
            panel.Padding = new Padding(14, 10, 14, 10);
            // V1.1.1: no forzar scroll horizontal cuando el ancho disponible es suficiente.
            // El scroll se activa dinámicamente solo al reducir realmente la ventana.
            panel.AutoScroll = false;
            panel.AutoScrollMinSize = Size.Empty;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Location = new Point(panel.Padding.Left, panel.Padding.Top);
            layout.Height = 204;
            layout.Width = 920;
            layout.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            layout.ColumnCount = 5;
            layout.RowCount = 6;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            panel.Controls.Add(layout);
            panel.SizeChanged += delegate { ConfigurePresetViewport(panel, layout); };
            panel.HandleCreated += delegate { ConfigurePresetViewport(panel, layout); };

            Label title = Theme.MakeLabel("PERFIL", 10.5f, true, Theme.Muted);
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(title, 0, 0);
            layout.SetColumnSpan(title, 3);

            _conservativeButton = Theme.MakeButton("CONSERVADOR", false);
            _conservativeButton.Dock = DockStyle.Fill;
            _conservativeButton.Margin = new Padding(0, 2, 8, 2);
            _conservativeButton.Click += delegate { ApplyPreset(PresetKind.Conservative); };
            layout.Controls.Add(_conservativeButton, 0, 1);

            _balancedButton = Theme.MakeButton("EQUILIBRADO", false);
            _balancedButton.Dock = DockStyle.Fill;
            _balancedButton.Margin = new Padding(0, 2, 8, 2);
            _balancedButton.Click += delegate { ApplyPreset(PresetKind.Balanced); };
            layout.Controls.Add(_balancedButton, 1, 1);

            _aggressiveButton = Theme.MakeButton("AGRESIVO", false);
            _aggressiveButton.Dock = DockStyle.Fill;
            _aggressiveButton.Margin = new Padding(0, 2, 8, 2);
            _aggressiveButton.Click += delegate { ApplyPreset(PresetKind.Aggressive); };
            layout.Controls.Add(_aggressiveButton, 2, 1);

            Label note = Theme.MakeLabel("Los perfiles solo preseleccionan. Nada cambia hasta pulsar APLICAR.", 10.3f, false, Theme.Muted);
            note.AutoSize = false;
            note.Dock = DockStyle.Fill;
            note.TextAlign = ContentAlignment.MiddleLeft;
            note.AutoEllipsis = true;
            note.Margin = new Padding(12, 2, 8, 2);
            layout.Controls.Add(note, 3, 1);

            Button toggleDetail = Theme.MakeButton("OCULTAR DETALLE", false);
            toggleDetail.Dock = DockStyle.Fill;
            toggleDetail.Margin = new Padding(6, 2, 0, 2);
            toggleDetail.Click += delegate
            {
                split.Panel2Collapsed = !split.Panel2Collapsed;
                toggleDetail.Text = split.Panel2Collapsed ? "MOSTRAR DETALLE" : "OCULTAR DETALLE";
            };
            layout.Controls.Add(toggleDetail, 4, 1);

            Label modeTitle = Theme.MakeLabel("MODO DE APLICACIÓN", 10.5f, true, Theme.Muted);
            modeTitle.Dock = DockStyle.Fill;
            modeTitle.TextAlign = ContentAlignment.BottomLeft;
            layout.Controls.Add(modeTitle, 0, 2);
            layout.SetColumnSpan(modeTitle, 3);

            _persistentModeButton = Theme.MakeButton("PERSISTENTE (POR DEFECTO)", true);
            _persistentModeButton.Dock = DockStyle.Fill;
            _persistentModeButton.Margin = new Padding(0, 2, 8, 0);
            _persistentModeButton.Click += delegate { SetApplyMode(ApplyMode.Persistent); };
            layout.Controls.Add(_persistentModeButton, 0, 3);
            layout.SetColumnSpan(_persistentModeButton, 2);

            _sessionModeButton = Theme.MakeButton("TEMPORAL · RESTAURAR AL REINICIAR", false);
            _sessionModeButton.Dock = DockStyle.Fill;
            _sessionModeButton.Margin = new Padding(0, 2, 8, 0);
            _sessionModeButton.Click += delegate { SetApplyMode(ApplyMode.UntilRestart); };
            layout.Controls.Add(_sessionModeButton, 2, 3);
            layout.SetColumnSpan(_sessionModeButton, 2);

            Label sessionNote = Theme.MakeLabel("Hyper-V y cambios de inicio automático no se aplican en modo temporal.", 9.7f, false, Theme.Medium);
            sessionNote.AutoSize = false;
            sessionNote.Dock = DockStyle.Fill;
            sessionNote.TextAlign = ContentAlignment.MiddleLeft;
            sessionNote.Margin = new Padding(6, 2, 0, 0);
            layout.Controls.Add(sessionNote, 4, 3);

            Label customTitle = Theme.MakeLabel("MIS PERFILES", 10.5f, true, Theme.Muted);
            customTitle.Dock = DockStyle.Fill;
            customTitle.TextAlign = ContentAlignment.BottomLeft;
            layout.Controls.Add(customTitle, 0, 4);
            layout.SetColumnSpan(customTitle, 5);

            FlowLayoutPanel profileTools = new FlowLayoutPanel();
            profileTools.Dock = DockStyle.Fill;
            profileTools.FlowDirection = FlowDirection.LeftToRight;
            profileTools.WrapContents = false;
            profileTools.Margin = new Padding(0, 2, 0, 0);
            profileTools.BackColor = Theme.Panel2;
            layout.Controls.Add(profileTools, 0, 5);
            layout.SetColumnSpan(profileTools, 5);

            _customProfileCombo = new ComboBox();
            _customProfileCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _customProfileCombo.Width = 220;
            _customProfileCombo.Height = 34;
            _customProfileCombo.BackColor = Theme.Panel;
            _customProfileCombo.ForeColor = Theme.Text;
            _customProfileCombo.FlatStyle = FlatStyle.Flat;
            _customProfileCombo.Margin = new Padding(0, 5, 8, 0);
            profileTools.Controls.Add(_customProfileCombo);

            _loadProfileButton = Theme.MakeButton("CARGAR", false);
            _loadProfileButton.Width = 92;
            _loadProfileButton.Margin = new Padding(0, 2, 8, 0);
            _loadProfileButton.Click += delegate { LoadSelectedUserProfile(); };
            profileTools.Controls.Add(_loadProfileButton);

            _saveProfileButton = Theme.MakeButton("GUARDAR ACTUAL", false);
            _saveProfileButton.Width = 132;
            _saveProfileButton.Margin = new Padding(0, 2, 8, 0);
            _saveProfileButton.Click += delegate { SaveCurrentAsUserProfile(); };
            profileTools.Controls.Add(_saveProfileButton);

            _deleteProfileButton = Theme.MakeButton("ELIMINAR", false);
            _deleteProfileButton.Width = 98;
            _deleteProfileButton.Margin = new Padding(0, 2, 8, 0);
            _deleteProfileButton.Click += delegate { DeleteSelectedUserProfile(); };
            profileTools.Controls.Add(_deleteProfileButton);

            RefreshUserProfileCombo();
            return panel;
        }

        private static void ConfigurePresetViewport(Panel panel, TableLayoutPanel layout)
        {
            if (panel == null || layout == null || panel.IsDisposed || layout.IsDisposed) return;

            const int workingWidth = 920;
            int available = panel.ClientSize.Width - panel.Padding.Left - panel.Padding.Right;
            if (available < 1) return;

            if (available >= workingWidth)
            {
                // En una ventana normal/maximizada todo el bloque cabe: sin barra horizontal.
                panel.AutoScroll = false;
                panel.AutoScrollMinSize = Size.Empty;
                panel.AutoScrollPosition = Point.Empty;
                layout.Location = new Point(panel.Padding.Left, panel.Padding.Top);
                layout.Width = available;
            }
            else
            {
                // En modo compacto conservamos una superficie legible y dejamos que el usuario
                // se desplace horizontalmente en lugar de comprimir/solapar controles.
                panel.AutoScroll = true;
                panel.AutoScrollMinSize = new Size(workingWidth + panel.Padding.Left + panel.Padding.Right, 0);
                layout.Location = new Point(panel.Padding.Left, panel.Padding.Top);
                layout.Width = workingWidth;
            }
        }

        private Panel BuildDetailsPanel(out Label title, out Label description, out Label consequences, out Label technical)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Theme.Panel;
            panel.Padding = new Padding(16);

            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.AutoScroll = true;
            flow.BackColor = Theme.Panel;
            flow.Padding = new Padding(4);
            panel.Controls.Add(flow);

            Label header = Theme.MakeLabel("DETALLE", 10.5f, true, Theme.Muted);
            header.Height = 28;
            flow.Controls.Add(header);

            title = Theme.MakeLabel("Selecciona una opción", 15f, true, Theme.Text);
            title.AutoSize = false;
            title.Height = 54;
            title.AutoEllipsis = true;
            flow.Controls.Add(title);

            Label d1 = Theme.MakeLabel("QUÉ HACE", 8f, true, Theme.Modified);
            d1.Height = 26;
            flow.Controls.Add(d1);

            description = MakeWrappedLabel("Haz clic en cualquier fila para ver su explicación.", 0, 0, 320, 100);
            description.Height = 100;
            flow.Controls.Add(description);

            Label d2 = Theme.MakeLabel("CONSECUENCIAS", 8f, true, Theme.Medium);
            d2.Height = 26;
            flow.Controls.Add(d2);

            consequences = MakeWrappedLabel("", 0, 0, 320, 120);
            consequences.Height = 120;
            flow.Controls.Add(consequences);

            Label d3 = Theme.MakeLabel("ESTADO / COMPONENTES", 8f, true, Theme.Modified);
            d3.Height = 26;
            flow.Controls.Add(d3);

            technical = MakeWrappedLabel("", 0, 0, 320, 250);
            technical.Height = 250;
            technical.Font = new Font("Consolas", 8.4f, FontStyle.Regular);
            flow.Controls.Add(technical);

            Label rule = MakeWrappedLabel("REGLA: si un servicio ya está Manual + Parado y el tweak está marcado como 'sin ganancia útil', no se fuerza a Disabled.", 0, 0, 320, 90);
            rule.Height = 90;
            rule.ForeColor = Theme.Muted;
            flow.Controls.Add(rule);

            ConfigureResponsiveFlow(flow, 280);
            return panel;
        }

        private Panel BuildFooter(out Label summary, out Button preview, out Button apply)
        {
            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 76;
            footer.BackColor = Theme.Panel;
            footer.Padding = new Padding(18, 16, 18, 16);

            summary = Theme.MakeLabel("0 cambios seleccionados", 11.5f, true, Theme.Text);
            summary.AutoSize = false;
            summary.TextAlign = ContentAlignment.MiddleLeft;
            summary.Dock = DockStyle.Left;
            summary.Width = 540;
            footer.Controls.Add(summary);

            apply = Theme.MakeButton(_isAdministrator ? "APLICAR CAMBIOS" : "APLICAR (PEDIR ADMIN)", true);
            apply.Width = _isAdministrator ? 190 : 220;
            apply.Dock = DockStyle.Right;
            apply.Click += delegate { ApplySelected(); };
            footer.Controls.Add(apply);

            Panel spacer = new Panel();
            spacer.Dock = DockStyle.Right;
            spacer.Width = 10;
            footer.Controls.Add(spacer);

            preview = Theme.MakeButton("VER CAMBIOS", false);
            preview.Width = 150;
            preview.Dock = DockStyle.Right;
            preview.Click += delegate { PreviewSelected(); };
            footer.Controls.Add(preview);

            // V1.1: el pie no debe impedir reducir la ventana.
            // Copias locales para no capturar parámetros out dentro del delegado.
            Label summaryLocal = summary;
            Button previewLocal = preview;
            Button applyLocal = apply;
            Panel spacerLocal = spacer;
            footer.SizeChanged += delegate
            {
                int w = footer.ClientSize.Width;
                if (w < 760)
                {
                    previewLocal.Width = 118;
                    previewLocal.Text = "VER";
                    applyLocal.Width = 170;
                    applyLocal.Text = _isAdministrator ? "APLICAR" : "APLICAR (ADMIN)";
                }
                else
                {
                    previewLocal.Width = 150;
                    previewLocal.Text = "VER CAMBIOS";
                    applyLocal.Width = _isAdministrator ? 190 : 220;
                    applyLocal.Text = _isAdministrator ? "APLICAR CAMBIOS" : "APLICAR (PEDIR ADMIN)";
                }

                int occupied = previewLocal.Width + applyLocal.Width + spacerLocal.Width + footer.Padding.Left + footer.Padding.Right + 18;
                summaryLocal.Width = Math.Max(80, w - occupied);
                summaryLocal.AutoEllipsis = w < 900;
            };

            return footer;
        }

        private void BuildTweakRows(FlowLayoutPanel flow, Predicate<TweakDefinition> predicate)
        {
            IEnumerable<IGrouping<string, TweakDefinition>> groups = _catalog.Where(delegate(TweakDefinition t) { return predicate(t); }).GroupBy(delegate(TweakDefinition t) { return t.Category; });
            foreach (IGrouping<string, TweakDefinition> group in groups)
            {
                AddSectionHeader(flow, group.Key);
                foreach (TweakDefinition tweak in group)
                    AddTweakRow(flow, tweak);
            }
        }

        private void BuildProtectedSection(FlowLayoutPanel flow)
        {
            AddSectionHeader(flow, "PROTEGIDO / FUERA DE ALCANCE V1");
            foreach (TweakDefinition tweak in _catalog.Where(delegate(TweakDefinition t) { return t.IsProtectedInfo; }))
                AddTweakRow(flow, tweak);
        }

        private void BuildApplicationCards(FlowLayoutPanel flow)
        {
            // V1.1: APLICACIONES usa cabeceras semánticas igual que OPTIMIZACIÓN.
            // Las aplicaciones integradas muestran una columna final para Cerrar ahora.
            flow.Controls.Add(TweakRowControl.CreateApplicationColumnHeader("ACCIÓN", false));

            IEnumerable<IGrouping<string, TweakDefinition>> groups = _catalog
                .Where(delegate(TweakDefinition t) { return t.IsApplication && !t.IsCustomApplication; })
                .GroupBy(delegate(TweakDefinition t) { return t.Category; });

            foreach (IGrouping<string, TweakDefinition> group in groups)
            {
                Panel card = new Panel();
                card.Width = 900;
                int rowsHeight = group.Sum(delegate(TweakDefinition t) { return t.ChangeKind == ChangeKind.Temporary ? TweakRowControl.ResourceRowHeight : TweakRowControl.StandardRowHeight; });
                card.Height = 28 + rowsHeight;
                card.BackColor = Theme.Panel2;
                card.Margin = new Padding(0, 0, 0, 4);
                card.Padding = new Padding(8, 20, 8, 4);
                flow.Controls.Add(card);

                Label name = Theme.MakeLabel(group.Key.ToUpperInvariant(), 10.2f, true, Theme.Text);
                name.Location = new Point(12, 2);
                card.Controls.Add(name);

                Panel rowsPanel = new Panel();
                rowsPanel.Dock = DockStyle.Fill;
                rowsPanel.BackColor = Theme.Panel;
                card.Controls.Add(rowsPanel);

                ApplicationCardVisual visual = new ApplicationCardVisual();
                visual.Card = card;
                visual.Title = name;
                visual.OriginalTitle = group.Key.ToUpperInvariant();
                visual.RowsPanel = rowsPanel;
                visual.TweakIds = group.Select(delegate(TweakDefinition t) { return t.Id; }).ToList();
                _applicationCards[group.Key] = visual;

                foreach (TweakDefinition tweak in group.Reverse())
                {
                    TweakRowControl row = CreateRow(tweak);
                    row.Dock = DockStyle.Top;
                    rowsPanel.Controls.Add(row);
                }
            }

            AddSectionHeader(flow, "MIS APLICACIONES");

            Label explanation = MakeWrappedLabel(
                "Arrastra aquí accesos directos (.lnk) o ejecutables (.exe). ServiceKiller identifica el proceso, guarda la aplicación y comprueba si sigue instalada. Crea una acción para cerrarla durante el boost y otra independiente para quitar su inicio automático de forma reversible cuando encuentre una entrada compatible. También muestra procesos y RAM aproximada.",
                0, 0, 900, 50);
            explanation.Height = 42;
            explanation.ForeColor = Theme.Muted;
            explanation.Margin = new Padding(0, 0, 0, 2);
            flow.Controls.Add(explanation);

            // Las aplicaciones personalizadas reservan más ancho al final porque tienen
            // Cerrar + Quitar; su propia cabecera mantiene la alineación correcta.
            flow.Controls.Add(TweakRowControl.CreateApplicationColumnHeader("APLICACIÓN", true));

            foreach (TweakDefinition tweak in _catalog.Where(delegate(TweakDefinition t) { return t.IsCustomApplication; }).OrderBy(delegate(TweakDefinition t) { return t.Name; }))
                AddCustomApplicationRow(flow, tweak, false);

            _customDropZone = CreateCustomDropZone();
            flow.Controls.Add(_customDropZone);
        }

        private void AddCustomApplicationRow(FlowLayoutPanel flow, TweakDefinition tweak, bool insertBeforeDropZone)
        {
            TweakRowControl row = CreateRow(tweak);
            row.Margin = new Padding(0, 0, 0, 1);
            flow.Controls.Add(row);

            if (insertBeforeDropZone && _customDropZone != null && flow.Controls.Contains(_customDropZone))
            {
                int dropIndex = flow.Controls.GetChildIndex(_customDropZone);
                flow.Controls.SetChildIndex(row, Math.Max(0, dropIndex));
            }

            if (_currentPreset == PresetKind.Aggressive && tweak.Aggressive)
                row.Selected = true;
        }

        private Panel CreateCustomDropZone()
        {
            Panel panel = new Panel();
            panel.Width = 900;
            panel.Height = 82;
            panel.BackColor = Theme.Panel2;
            panel.Margin = new Padding(0, 4, 0, 10);
            panel.AllowDrop = true;

            Label title = Theme.MakeLabel("ARRASTRA AQUÍ UN ACCESO DIRECTO O .EXE", 11f, true, Theme.Text);
            title.AutoSize = false;
            title.TextAlign = ContentAlignment.MiddleCenter;
            title.Dock = DockStyle.Top;
            title.Height = 26;
            title.AllowDrop = true;
            panel.Controls.Add(title);

            Label sub = Theme.MakeLabel("Se analizará antes de guardarlo. Añadir o quitar aplicaciones no necesita permisos de administrador.", 10.7f, false, Theme.Muted);
            sub.AutoSize = false;
            sub.TextAlign = ContentAlignment.MiddleCenter;
            sub.Dock = DockStyle.Top;
            sub.Height = 20;
            sub.AllowDrop = true;
            panel.Controls.Add(sub);

            Button browse = Theme.MakeButton("+ AÑADIR APLICACIÓN", false);
            browse.Width = 190;
            browse.Height = 26;
            browse.Left = (panel.Width - browse.Width) / 2;
            browse.Top = 60;
            browse.Anchor = AnchorStyles.Top;
            browse.Click += delegate { BrowseCustomApplications(); };
            browse.AllowDrop = true;
            panel.Controls.Add(browse);
            panel.SizeChanged += delegate { browse.Left = Math.Max(8, (panel.ClientSize.Width - browse.Width) / 2); };

            DragEventHandler dragEnter = delegate(object sender, DragEventArgs e)
            {
                string[] files = e.Data == null ? null : e.Data.GetData(DataFormats.FileDrop) as string[];
                bool supported = files != null && files.Length > 0 && files.All(delegate(string path)
                {
                    string ext = Path.GetExtension(path);
                    return string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase) || string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase);
                });
                e.Effect = supported ? DragDropEffects.Copy : DragDropEffects.None;
            };

            DragEventHandler dragDrop = delegate(object sender, DragEventArgs e)
            {
                string[] files = e.Data == null ? null : e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || files.Length == 0) return;
                foreach (string path in files) AddCustomApplicationFromPath(path);
            };

            panel.DragEnter += dragEnter;
            panel.DragDrop += dragDrop;
            title.DragEnter += dragEnter;
            title.DragDrop += dragDrop;
            sub.DragEnter += dragEnter;
            sub.DragDrop += dragDrop;
            browse.DragEnter += dragEnter;
            browse.DragDrop += dragDrop;

            panel.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Theme.Accent, 1f))
                {
                    pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    Rectangle r = panel.ClientRectangle;
                    r.Inflate(-3, -3);
                    e.Graphics.DrawRectangle(pen, r);
                }
            };
            return panel;
        }

        private void AnalyzeResidentProcesses()
        {
            SetBusy(true, "Analizando procesos residentes...");
            try
            {
                List<ResidentProcessCandidate> candidates = _engine.Processes.DiscoverResidentCandidates();
                candidates = candidates.Where(delegate(ResidentProcessCandidate c)
                {
                    if (c == null || string.IsNullOrWhiteSpace(c.ProcessName)) return false;
                    if (FindBuiltInProcessCoverage(c.ProcessName) != null) return false;
                    CustomAppDetectionResult probe = new CustomAppDetectionResult();
                    probe.ProcessName = c.ProcessName;
                    probe.ProcessExecutablePath = c.ExecutablePath;
                    return !IsDuplicateCustomApplication(probe);
                }).ToList();

                SetBusy(false, null);
                if (candidates.Count == 0)
                {
                    MessageBox.Show(this, "No se encontraron procesos residentes nuevos que ServiceKiller pueda proponer ahora mismo.", "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using (ProcessAnalyzerForm form = new ProcessAnalyzerForm(candidates))
                {
                    if (form.ShowDialog(this) != DialogResult.OK || form.SelectedCandidates.Count == 0) return;
                    int added = 0;
                    foreach (ResidentProcessCandidate candidate in form.SelectedCandidates)
                        if (AddResidentCandidate(candidate)) added++;
                    if (added > 0)
                    {
                        RebuildCustomApplicationRows();
                        RefreshSystemView(null);
                        MessageBox.Show(this, added + " aplicación(es) añadida(s) a Mis aplicaciones.", "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo completar el análisis de procesos:\r\n" + ex.Message, "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { SetBusy(false, null); }
        }

        private bool AddResidentCandidate(ResidentProcessCandidate candidate)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.ProcessName) || string.IsNullOrWhiteSpace(candidate.ExecutablePath)) return false;
            CustomAppDetectionResult probe = new CustomAppDetectionResult();
            probe.ProcessName = candidate.ProcessName;
            probe.ProcessExecutablePath = candidate.ExecutablePath;
            if (FindBuiltInProcessCoverage(candidate.ProcessName) != null || IsDuplicateCustomApplication(probe)) return false;

            CustomApplicationInfo info = new CustomApplicationInfo();
            info.Id = Guid.NewGuid().ToString("N");
            info.DisplayName = string.IsNullOrWhiteSpace(candidate.DisplayName) ? candidate.ProcessName : candidate.DisplayName;
            info.SourcePath = candidate.ExecutablePath;
            info.LaunchTargetPath = candidate.ExecutablePath;
            info.ProcessExecutablePath = candidate.ExecutablePath;
            info.ProcessName = candidate.ProcessName;
            info.ShortcutArguments = string.Empty;
            info.DetectionNote = "Añadida desde el analizador de procesos residentes de ServiceKiller.";
            info.AddedUtc = DateTime.UtcNow;
            info.IncludeInAggressive = true;
            _customApplications.Add(info);
            try { _customAppStore.Save(_customApplications); }
            catch
            {
                _customApplications.Remove(info);
                throw;
            }
            _catalog.Add(CustomAppStore.ToTweak(info));
            _catalog.Add(CustomAppStore.ToStartupTweak(info));
            _log.Info("Aplicación añadida desde analizador: " + info.DisplayName + " -> " + info.ProcessName + ".exe");
            return true;
        }

        private void RebuildCustomApplicationRows()
        {
            if (_appFlow == null) return;
            // En V1.1, para mantener sencillo y seguro el layout dinámico, las nuevas
            // filas se añaden justo antes de la zona drag & drop sin reconstruir las integradas.
            foreach (TweakDefinition tweak in _catalog.Where(delegate(TweakDefinition t) { return t.IsCustomApplication && !_rows.ContainsKey(t.Id); }).OrderBy(delegate(TweakDefinition t) { return t.Name; }))
                AddCustomApplicationRow(_appFlow, tweak, true);
            ResizeFlowChildren(_appFlow, 820);
        }

        private void BrowseCustomApplications()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Añadir aplicación a ServiceKiller";
                dialog.Filter = "Aplicaciones y accesos directos (*.exe;*.lnk)|*.exe;*.lnk|Ejecutables (*.exe)|*.exe|Accesos directos (*.lnk)|*.lnk";
                dialog.Multiselect = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                foreach (string file in dialog.FileNames) AddCustomApplicationFromPath(file);
            }
        }

        private void AddCustomApplicationFromPath(string path)
        {
            CustomAppDetectionResult detected = ShortcutResolver.Detect(path);
            if (!detected.Success)
            {
                MessageBox.Show(this, detected.Error, "ServiceKiller - no se pudo añadir", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            TweakDefinition builtInCoverage = FindBuiltInProcessCoverage(detected.ProcessName);
            if (builtInCoverage != null)
            {
                MessageBox.Show(this,
                    detected.DisplayName + " no necesita añadirse: el proceso " + detected.ProcessName + ".exe ya está cubierto por la opción incluida «" + builtInCoverage.Name + "».",
                    "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (IsDuplicateCustomApplication(detected))
            {
                MessageBox.Show(this, detected.DisplayName + " ya está añadida a Mis aplicaciones.", "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string target = string.IsNullOrWhiteSpace(detected.LaunchTargetPath) ? "—" : detected.LaunchTargetPath;
            string processPath = string.IsNullOrWhiteSpace(detected.ProcessExecutablePath) ? "Se identificará por nombre de proceso" : detected.ProcessExecutablePath;
            string running = detected.RunningInstances > 0 ? detected.RunningInstances + " instancia(s) detectada(s) ahora" : "No está ejecutándose ahora";
            string message =
                "Aplicación detectada:\r\n\r\n" +
                "Nombre: " + detected.DisplayName + "\r\n" +
                "Proceso a cerrar: " + detected.ProcessName + ".exe\r\n" +
                "Destino del acceso directo: " + target + "\r\n" +
                "Ruta de proceso: " + processPath + "\r\n" +
                "Estado: " + running + "\r\n\r\n" +
                detected.DetectionNote + "\r\n\r\n" +
                "Se incluirá por defecto en AGRESIVO. ¿Añadirla a ServiceKiller?";

            if (MessageBox.Show(this, message, "ServiceKiller - añadir aplicación", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            CustomApplicationInfo info = new CustomApplicationInfo();
            info.Id = Guid.NewGuid().ToString("N");
            info.DisplayName = detected.DisplayName;
            info.SourcePath = detected.SourcePath;
            info.LaunchTargetPath = detected.LaunchTargetPath;
            info.ProcessExecutablePath = detected.ProcessExecutablePath;
            info.ProcessName = detected.ProcessName;
            info.ShortcutArguments = detected.ShortcutArguments;
            info.DetectionNote = detected.DetectionNote;
            info.AddedUtc = DateTime.UtcNow;
            info.IncludeInAggressive = true;

            List<CustomApplicationInfo> next = new List<CustomApplicationInfo>(_customApplications);
            next.Add(info);
            try
            {
                _customAppStore.Save(next);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo guardar la aplicación:\r\n" + ex.Message, "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _customApplications.Add(info);
            TweakDefinition tweak = CustomAppStore.ToTweak(info);
            TweakDefinition startupTweak = CustomAppStore.ToStartupTweak(info);
            _catalog.Add(tweak);
            _catalog.Add(startupTweak);
            AddCustomApplicationRow(_appFlow, tweak, true);
            AddCustomApplicationRow(_appFlow, startupTweak, true);
            ResizeFlowChildren(_appFlow, 820);
            RefreshSystemView(null);
            _log.Info("Aplicación personalizada añadida: " + info.DisplayName + " -> " + info.ProcessName + ".exe");
        }

        private TweakDefinition FindBuiltInProcessCoverage(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return null;
            foreach (TweakDefinition tweak in _catalog.Where(delegate(TweakDefinition t) { return t.IsApplication && !t.IsCustomApplication; }))
            {
                foreach (string exact in tweak.ProcessNames)
                    if (string.Equals(exact, processName, StringComparison.OrdinalIgnoreCase)) return tweak;
                foreach (string prefix in tweak.ProcessPrefixes)
                    if (processName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return tweak;
            }
            return null;
        }

        private bool IsDuplicateCustomApplication(CustomAppDetectionResult detected)
        {
            foreach (CustomApplicationInfo existing in _customApplications)
            {
                if (!string.Equals(existing.ProcessName, detected.ProcessName, StringComparison.OrdinalIgnoreCase)) continue;

                string a = existing.ProcessExecutablePath ?? string.Empty;
                string b = detected.ProcessExecutablePath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return true;
                try
                {
                    if (string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch
                {
                    if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        private void OnRemoveCustomApp(object sender, EventArgs e)
        {
            TweakRowControl row = sender as TweakRowControl;
            if (row == null || !row.Definition.IsCustomApplication) return;

            if (MessageBox.Show(this,
                "Quitar " + row.Definition.Name + " de ServiceKiller?\r\n\r\nEsto solo elimina la entrada de Mis aplicaciones. No desinstala ni modifica la aplicación.",
                "ServiceKiller - quitar aplicación", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            string customId = row.Definition.CustomApplicationId;
            List<CustomApplicationInfo> next = _customApplications.Where(delegate(CustomApplicationInfo a)
            {
                return !string.Equals(a.Id, customId, StringComparison.OrdinalIgnoreCase);
            }).ToList();

            try
            {
                _customAppStore.Save(next);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo actualizar Mis aplicaciones:\r\n" + ex.Message, "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _customApplications.RemoveAll(delegate(CustomApplicationInfo a) { return string.Equals(a.Id, customId, StringComparison.OrdinalIgnoreCase); });
            List<TweakRowControl> rowsToRemove = _rows.Values.Where(delegate(TweakRowControl r) { return string.Equals(r.Definition.CustomApplicationId, customId, StringComparison.OrdinalIgnoreCase); }).ToList();
            _catalog.RemoveAll(delegate(TweakDefinition t) { return string.Equals(t.CustomApplicationId, customId, StringComparison.OrdinalIgnoreCase); });
            foreach (TweakRowControl removeRow in rowsToRemove)
            {
                _rows.Remove(removeRow.Definition.Id);
                if (object.ReferenceEquals(_activeDetailRow, removeRow)) _activeDetailRow = null;
                if (_appFlow != null && _appFlow.Controls.Contains(removeRow)) _appFlow.Controls.Remove(removeRow);
                removeRow.Dispose();
            }
            if (_activeDetailRow == null)
            {
                _detailTitle.Text = "Selecciona una opción";
                _detailDescription.Text = "Haz clic en cualquier fila para ver su explicación.";
                _detailConsequences.Text = string.Empty;
                _detailTechnical.Text = string.Empty;
            }
            ResizeFlowChildren(_appFlow, 820);
            RefreshSystemView(null);
            _log.Info("Aplicación personalizada eliminada de ServiceKiller: " + customId);
        }

        private void AddSectionHeader(FlowLayoutPanel flow, string text)
        {
            Label header = Theme.MakeLabel(text.ToUpperInvariant(), 9f, true, Theme.Muted);
            header.Width = 760;
            header.Height = 18;
            header.Padding = new Padding(4, 2, 0, 0);
            header.Margin = new Padding(0, 2, 0, 1);
            flow.Controls.Add(header);
        }

        private void AddTweakRow(FlowLayoutPanel flow, TweakDefinition tweak)
        {
            TweakRowControl row = CreateRow(tweak);
            row.Width = 760;
            flow.Controls.Add(row);
        }

        private TweakRowControl CreateRow(TweakDefinition tweak)
        {
            TweakRowControl row = new TweakRowControl(tweak);
            row.Width = 760;
            row.SelectionChanged += OnRowSelectionChanged;
            row.RowActivated += OnRowActivated;
            row.RunNowClicked += OnRunNow;
            row.RemoveClicked += OnRemoveCustomApp;
            _rows[tweak.Id] = row;
            return row;
        }

        private void BuildInfo(TabPage page)
        {
            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.AutoScroll = true;
            flow.BackColor = Theme.Back;
            flow.Padding = new Padding(24, 22, 24, 34);
            page.Controls.Add(flow);

            Label title = Theme.MakeLabel("INFORMACIÓN DE SERVICEKILLER", 16f, true, Theme.Text);
            title.Height = 44;
            title.Margin = new Padding(0, 0, 0, 8);
            flow.Controls.Add(title);

            _infoText = MakeWrappedLabel("Leyendo información...", 0, 0, 1000, 720);
            _infoText.Height = 720;
            _infoText.ForeColor = Theme.Text;
            _infoText.Margin = new Padding(0);
            flow.Controls.Add(_infoText);

            ConfigureResponsiveFlow(flow, 820);
        }

        private void RefreshInfoPanel()
        {
            if (_infoText == null || _infoText.IsDisposed) return;

            int activeBackups = 0;
            int sessionBackups = 0;
            try { activeBackups = _engine.GetActiveBackups().Count; } catch { }
            try { sessionBackups = _sessionEngine.GetActiveBackups().Count; } catch { }

            int customCount = _customApplications.Count;
            int installedApps = 0;
            int runningApps = 0;
            int notInstalledApps = 0;
            int notVerifiableApps = 0;
            HashSet<string> seenFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (TweakRowControl row in _rows.Values)
            {
                if (!row.Definition.IsApplication) continue;
                string key = row.Definition.IsCustomApplication ? row.Definition.CustomApplicationId : row.Definition.Category;
                if (!seenFamilies.Add(key)) continue;

                ApplicationInstallState state = row.ApplicationInstallState;
                if (state == ApplicationInstallState.InstalledRunning) { installedApps++; runningApps++; }
                else if (state == ApplicationInstallState.InstalledClosed) installedApps++;
                else if (state == ApplicationInstallState.NotInstalled) notInstalledApps++;
                else if (state == ApplicationInstallState.NotVerifiable) notVerifiableApps++;
            }

            string privilegeMode = _isAdministrator ? "Administrador" : "Solo lectura (UAC únicamente al aplicar/restaurar)";
            string applyMode = _applyMode == ApplyMode.UntilRestart ? "TEMPORAL HASTA REINICIO" : "PERSISTENTE";
            string lastBoost = ReadLastBoostSummary();
            string journal = activeBackups > 0 ? "Sí · " + activeBackups + " cambio(s) persistente(s) pendiente(s) de restaurar" : "No hay cambios persistentes pendientes";
            string sessionJournal = sessionBackups > 0 ? "ACTIVO · " + sessionBackups + " cambio(s) con auto-restauración pendiente" : "No hay sesión temporal pendiente";

            _infoText.Text =
                "CÓMO FUNCIONA" + Environment.NewLine +
                "1. Al abrirse, ServiceKiller analiza el estado actual del equipo. No modifica ni cierra nada automáticamente." + Environment.NewLine +
                "2. Conservador, Equilibrado y Agresivo solo preseleccionan casillas; puedes cambiarlas antes de aplicar." + Environment.NewLine +
                "3. Puedes elegir PERSISTENTE o TEMPORAL HASTA REINICIO antes de pulsar APLICAR." + Environment.NewLine +
                "4. VER CAMBIOS muestra una simulación. APLICAR guarda primero el estado necesario y después ejecuta." + Environment.NewLine +
                "5. RESTAURAR permite volver manualmente al estado guardado; el modo temporal además se restaura solo en el siguiente logon tras reiniciar/cerrar sesión." + Environment.NewLine +
                "6. Puedes guardar cualquier combinación de Windows + aplicaciones como perfil personalizado y reutilizarla cuando quieras." + Environment.NewLine + Environment.NewLine +

                "MODOS DE APLICACIÓN" + Environment.NewLine +
                "PERSISTENTE: es el comportamiento normal. El cambio permanece tras reiniciar hasta que lo restaures." + Environment.NewLine +
                "TEMPORAL HASTA REINICIO: ServiceKiller guarda un journal separado, programa primero una tarea de auto-restauración y después aplica el boost. Al próximo inicio de sesión la tarea se ejecuta elevada de forma automática y recupera el estado anterior." + Environment.NewLine +
                "En modo temporal, Hyper-V/BCD y los cambios que solo afectan al inicio automático se excluyen: necesitan precisamente un reinicio/logon para producir su efecto y no pueden cumplir la filosofía 'boost ahora, limpio al reiniciar'." + Environment.NewLine +
                "Si una opción ya estaba aplicada de forma PERSISTENTE, el modo temporal la respeta y NO la convierte en temporal." + Environment.NewLine + Environment.NewLine +

                "TIPOS DE CAMBIO" + Environment.NewLine +
                "PERSISTENTE: permanece tras reiniciar; normalmente no necesitas volver a aplicarlo otro día." + Environment.NewLine +
                "TEMPORAL: cierra procesos o aplica una acción de sesión; puede volver al abrir la aplicación o reiniciar." + Environment.NewLine +
                "HASTA REINICIO: indica que un cambio normalmente persistente se ha seleccionado para auto-restaurarse al terminar la sesión/reiniciar." + Environment.NewLine +
                "REINICIO: el cambio se configura ahora, pero necesita reiniciar Windows para completar su efecto; no se usa en el modo temporal." + Environment.NewLine + Environment.NewLine +

                "CÓMO LEER LAS COLUMNAS" + Environment.NewLine +
                "BENEFICIO ESPERADO: estimación cualitativa de reducción potencial de actividad en segundo plano; no son FPS garantizados." + Environment.NewLine +
                "IMPACTO FUNCIONAL: cuánto puedes notar la pérdida de la función desactivada." + Environment.NewLine +
                "MODIFICADO POR SERVICEKILLER: SÍ indica cambio persistente; SÍ · SESIÓN indica cambio temporal pendiente de auto-restauración." + Environment.NewLine + Environment.NewLine +

                "APLICACIONES" + Environment.NewLine +
                "ServiceKiller distingue INSTALADO · EJECUTÁNDOSE, INSTALADO · CERRADO, NO INSTALADO y NO VERIFICABLE." + Environment.NewLine +
                "Una aplicación NO INSTALADA se muestra atenuada y sin checkbox: permanece guardada para que vuelva a estar disponible automáticamente si la reinstalas." + Environment.NewLine +
                "En las aplicaciones activas se muestra el número de procesos asociados y su RAM aproximada. El cálculo incluye descendientes del árbol aunque usen nombres de helper distintos." + Environment.NewLine +
                "Al cerrar una aplicación, ServiceKiller identifica las raíces necesarias y termina sus descendientes directamente desde .NET, sin invocar una herramienta externa adicional." + Environment.NewLine +
                "ANALIZAR PROCESOS RESIDENTES propone aplicaciones activas con su RAM y número de procesos; nunca añade ni cierra nada sin tu selección." + Environment.NewLine +
                "Cada aplicación personalizada dispone además de una acción reversible para quitar su inicio automático detectado en Run/RunOnce y carpetas Inicio." + Environment.NewLine +
                "Mis aplicaciones guardadas: " + customCount + " · Instaladas detectadas: " + installedApps + " · Ejecutándose: " + runningApps +
                " · No instaladas: " + notInstalledApps + " · No verificables: " + notVerifiableApps + Environment.NewLine + Environment.NewLine +

                "SEGURIDAD Y REVERSIBILIDAD" + Environment.NewLine +
                "Bluetooth, Defender, SmartScreen, Firewall, Windows Update/BITS/Update Medic/Delivery Optimization, audio, micrófono/cámara y servicios base de red no se ofrecen como tweaks." + Environment.NewLine +
                "Los journals se escriben antes del cambio real. Si una restauración no termina correctamente, el journal se conserva para poder reintentarla." + Environment.NewLine +
                "Después de APLICAR, ServiceKiller registra servicios, procesos, RAM usada, RAM disponible y tiempo real de ejecución. También añade al LOG un bloque comparativo ANTES / DESPUÉS / DIFERENCIA. Son métricas puntuales, no un benchmark de FPS." + Environment.NewLine +
                "Persistentes: " + journal + Environment.NewLine +
                "Sesión temporal: " + sessionJournal + Environment.NewLine +
                "Perfiles personalizados: " + _userProfiles.Count + Environment.NewLine + Environment.NewLine +

                "ÚLTIMO BOOST" + Environment.NewLine +
                lastBoost + Environment.NewLine + Environment.NewLine +

                "ESTADO DE ESTA SESIÓN" + Environment.NewLine +
                "Versión: " + ServiceKillerV1.BuildInfo.DisplayName + Environment.NewLine +
                "Privilegios de la GUI: " + privilegeMode + Environment.NewLine +
                "Modo de aplicación seleccionado: " + applyMode + Environment.NewLine +
                "Sistema: " + WindowsCompatibility.FriendlyName + (Environment.Is64BitOperatingSystem ? " · 64 bits" : " · 32 bits") + Environment.NewLine +
                "Compatibilidad: " + WindowsCompatibility.CompatibilitySummary + Environment.NewLine +
                ".NET: " + Environment.Version + Environment.NewLine +
                "Usuario: " + Environment.UserName + " · Equipo: " + Environment.MachineName + Environment.NewLine +
                "DPI actual: " + GetDpiPercent() + "%" + Environment.NewLine + Environment.NewLine +

                "DATOS INTERNOS" + Environment.NewLine +
                "Journal persistente: " + AppPaths.ActiveState + Environment.NewLine +
                "Journal temporal: " + AppPaths.SessionState + Environment.NewLine +
                "Restaurador temporal: " + AppPaths.SessionRestoreRoot + Environment.NewLine +
                "Tarea temporal: " + AppPaths.SessionTaskName + Environment.NewLine +
                "Log elevado: " + AppPaths.LogFile + Environment.NewLine +
                "Datos de usuario: " + AppPaths.UserRoot + Environment.NewLine +
                "Mis aplicaciones: " + AppPaths.CustomApps + Environment.NewLine +
                "Mis perfiles: " + AppPaths.Profiles + Environment.NewLine +
                "Último boost: " + AppPaths.LastBoostSummary + Environment.NewLine +
                "Última verificación de restauración: " + AppPaths.LastSessionRestoreReport + Environment.NewLine +
                "Botón DIAGNÓSTICO: genera un informe anonimizado con estos estados, journals y fragmentos de LOG para soporte/verificación. La anonimización es automática y conviene revisarla antes de publicar el texto." + Environment.NewLine +
                "Estas rutas se muestran solo como información; la pestaña LOG ya ofrece la consulta normal de registros.";
        }

        private int GetDpiPercent()
        {
            try
            {
                using (Graphics g = CreateGraphics())
                    return (int)Math.Round(g.DpiX / 96.0 * 100.0);
            }
            catch { return 100; }
        }

        private async void ShowDiagnosticReport()
        {
            if (_diagnosticButton != null) _diagnosticButton.Enabled = false;
            SetBusy(true, "Generando diagnóstico...");
            try
            {
                string report = await Task.Run(delegate
                {
                    DiagnosticReportBuilder builder = new DiagnosticReportBuilder(_log);
                    return builder.Build(ServiceKillerV1.BuildInfo.DisplayName, _isAdministrator, _applyMode);
                });
                using (DiagnosticForm form = new DiagnosticForm(report))
                    form.ShowDialog(this);
            }
            catch (Exception ex)
            {
                _log.Warn("No se pudo generar el diagnóstico: " + ex.Message);
                MessageBox.Show(this, "No se pudo generar el diagnóstico completo:\r\n" + ex.Message, "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                SetBusy(false, null);
                if (_diagnosticButton != null) _diagnosticButton.Enabled = true;
            }
        }

        private void ManualRefresh()
        {
            if (_store.SafetyLocked) return;
            SetBusy(true, "Actualizando estado del sistema...");
            try
            {
                RefreshSystemView(null);
                _log.Info("Estado refrescado manualmente.");
            }
            catch (Exception ex)
            {
                _log.Warn("No se pudo completar el refresco manual: " + ex.Message);
                MessageBox.Show(this, "No se pudo actualizar todo el estado:\r\n" + ex.Message, "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private void RefreshSystemView(SystemMetrics before)
        {
            RefreshAllStates();
            RefreshRestoreTab();
            RefreshMetrics(before);
            RefreshSessionIndicator();
            RefreshInfoPanel();
        }

        private void OnLoaded(object sender, EventArgs e)
        {
            try { AppPaths.EnsureUser(); } catch { }
            RestoreWindowPlacement();
            _log.Info("===== Inicio " + ServiceKillerV1.BuildInfo.DisplayName + " · " + (_isAdministrator ? "Administrador" : "Solo lectura") + " =====");
            LoadExistingLog();

            // Fuerza una lectura temprana del journal. Si está dañado y no hay recuperación,
            // StateStore activa un bloqueo de seguridad para impedir sobrescribir el baseline.
            _store.Load();
            if (_store.SafetyLocked)
            {
                _applyButton.Enabled = false;
                _previewButton.Enabled = true;
                _profileLabel.Text = "BLOQUEO DE SEGURIDAD";
                _metricsLabel.Text = "Journal de restauración dañado";
                MessageBox.Show(this, _store.SafetyMessage + "\r\n\r\nRevisa la pestaña LOG y la carpeta Backups antes de continuar.", "ServiceKiller - protección de backup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (_isAdministrator)
            {
                try
                {
                    List<string> repaired = _engine.RepairKnownStaleJournalEntries();
                    if (repaired.Count > 0)
                        _log.Info("Autorreparación de journal completada: " + repaired.Count + " entrada(s) corregida(s).");
                }
                catch (Exception repairEx)
                {
                    _log.Warn("No se pudo completar la autorreparación preventiva del journal: " + repairEx.Message);
                }
            }

            _sessionStore.Load();
            if (_sessionStore.SafetyLocked && _sessionModeButton != null)
            {
                _sessionModeButton.Enabled = false;
                MessageBox.Show(this, _sessionStore.SafetyMessage + "\r\n\r\nEl modo PERSISTENTE sigue disponible, pero el modo TEMPORAL queda bloqueado hasta revisar el journal de sesión.", "ServiceKiller - journal temporal", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            StyleApplyModeButtons();
            ApplyPreset(PresetKind.Conservative);
            RefreshSystemView(null);
        }

        private void ApplyPreset(PresetKind preset)
        {
            _activeUserProfileId = null;
            _applyingPreset = true;
            foreach (KeyValuePair<string, TweakRowControl> pair in _rows)
            {
                TweakDefinition tweak = pair.Value.Definition;
                pair.Value.SetApplyMode(_applyMode);
                if (tweak.IsProtectedInfo) continue;
                bool selected = tweak.IsSelectedByPreset(preset);
                if (_applyMode == ApplyMode.UntilRestart && !tweak.SupportsUntilRestartMode()) selected = false;
                pair.Value.Selected = selected;
            }
            _applyingPreset = false;
            _currentPreset = preset;
            _profileLabel.Text = "Perfil: " + PresetText(preset);
            UpdatePresetButtons();
            UpdateSelectionSummary();
        }

        private void RefreshUserProfileCombo()
        {
            if (_customProfileCombo == null) return;
            string previousId = _activeUserProfileId;
            _customProfileCombo.BeginUpdate();
            try
            {
                _customProfileCombo.Items.Clear();
                foreach (UserProfileInfo profile in _userProfiles.OrderBy(delegate(UserProfileInfo p) { return p.Name; }, StringComparer.CurrentCultureIgnoreCase))
                    _customProfileCombo.Items.Add(profile);

                UserProfileInfo selected = _userProfiles.FirstOrDefault(delegate(UserProfileInfo p) { return string.Equals(p.Id, previousId, StringComparison.OrdinalIgnoreCase); });
                if (selected != null) _customProfileCombo.SelectedItem = selected;
                else if (_customProfileCombo.Items.Count > 0) _customProfileCombo.SelectedIndex = 0;
            }
            finally { _customProfileCombo.EndUpdate(); }

            bool hasAny = _customProfileCombo.Items.Count > 0;
            if (_loadProfileButton != null) _loadProfileButton.Enabled = hasAny;
            if (_deleteProfileButton != null) _deleteProfileButton.Enabled = hasAny;
        }

        private UserProfileInfo SelectedUserProfile()
        {
            return _customProfileCombo == null ? null : _customProfileCombo.SelectedItem as UserProfileInfo;
        }

        private void LoadSelectedUserProfile()
        {
            UserProfileInfo profile = SelectedUserProfile();
            if (profile == null) return;
            ApplyUserProfile(profile);
        }

        private void ApplyUserProfile(UserProfileInfo profile)
        {
            if (profile == null) return;
            _applyMode = profile.ApplyMode;
            StyleApplyModeButtons();
            HashSet<string> ids = new HashSet<string>(profile.TweakIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            _applyingPreset = true;
            foreach (TweakRowControl row in _rows.Values)
            {
                row.SetApplyMode(_applyMode);
                bool selected = !row.Definition.IsProtectedInfo && ids.Contains(row.Definition.Id);
                if (_applyMode == ApplyMode.UntilRestart && !row.Definition.SupportsUntilRestartMode()) selected = false;
                row.Selected = selected;
            }
            _applyingPreset = false;

            _currentPreset = PresetKind.Custom;
            _activeUserProfileId = profile.Id;
            if (_customProfileCombo != null) _customProfileCombo.SelectedItem = profile;
            _profileLabel.Text = "Perfil: " + profile.Name.ToUpperInvariant();
            UpdatePresetButtons();
            UpdateSelectionSummary();
            if (_activeDetailRow != null) OnRowActivated(_activeDetailRow, EventArgs.Empty);
        }

        private void SaveCurrentAsUserProfile()
        {
            using (TextPromptForm prompt = new TextPromptForm("Guardar perfil", "Nombre del perfil personalizado:", CurrentProfileDisplayName() == "PERSONALIZADO" ? string.Empty : CurrentProfileDisplayName()))
            {
                if (prompt.ShowDialog(this) != DialogResult.OK) return;
                string name = prompt.Value;
                if (string.IsNullOrWhiteSpace(name)) return;

                UserProfileInfo profile = _userProfiles.FirstOrDefault(delegate(UserProfileInfo p) { return string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase); });
                if (profile != null)
                {
                    if (MessageBox.Show(this, "Ya existe un perfil llamado «" + profile.Name + "». ¿Sobrescribirlo con la selección actual?", "ServiceKiller", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                }
                else
                {
                    profile = new UserProfileInfo();
                    profile.Id = Guid.NewGuid().ToString("N");
                    profile.Name = name;
                    profile.CreatedUtc = DateTime.UtcNow;
                    _userProfiles.Add(profile);
                }

                profile.Name = name;
                profile.ApplyMode = _applyMode;
                profile.TweakIds = _rows.Values.Where(delegate(TweakRowControl r) { return !r.Definition.IsProtectedInfo && r.Selected; }).Select(delegate(TweakRowControl r) { return r.Definition.Id; }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                profile.UpdatedUtc = DateTime.UtcNow;

                try { _profileStore.Save(_userProfiles); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "No se pudo guardar el perfil:\r\n" + ex.Message, "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _activeUserProfileId = profile.Id;
                RefreshUserProfileCombo();
                ApplyUserProfile(profile);
                _log.Info("Perfil personalizado guardado: " + profile.Name + " (" + profile.TweakIds.Count + " acciones, modo " + profile.ApplyMode + ")");
            }
        }

        private void DeleteSelectedUserProfile()
        {
            UserProfileInfo profile = SelectedUserProfile();
            if (profile == null) return;
            if (MessageBox.Show(this, "Eliminar el perfil «" + profile.Name + "»?\r\n\r\nNo se modifica Windows ni las aplicaciones; solo se borra esta configuración de ServiceKiller.", "ServiceKiller", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            _userProfiles.Remove(profile);
            try { _profileStore.Save(_userProfiles); }
            catch (Exception ex)
            {
                _userProfiles.Add(profile);
                MessageBox.Show(this, "No se pudo eliminar el perfil:\r\n" + ex.Message, "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (string.Equals(_activeUserProfileId, profile.Id, StringComparison.OrdinalIgnoreCase)) _activeUserProfileId = null;
            RefreshUserProfileCombo();
            _log.Info("Perfil personalizado eliminado: " + profile.Name);
        }

        private string CurrentProfileDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(_activeUserProfileId))
            {
                UserProfileInfo p = _userProfiles.FirstOrDefault(delegate(UserProfileInfo x) { return string.Equals(x.Id, _activeUserProfileId, StringComparison.OrdinalIgnoreCase); });
                if (p != null) return p.Name;
            }
            return PresetText(_currentPreset);
        }

        private void SetApplyMode(ApplyMode mode)
        {
            if (_applyMode == mode) return;

            if (mode == ApplyMode.UntilRestart)
            {
                int persistentActive = _engine.GetActiveBackups().Count;
                if (persistentActive > 0)
                {
                    MessageBox.Show(this,
                        "Hay " + persistentActive + " cambio(s) PERSISTENTE(S) ya activos.\r\n\r\n" +
                        "El modo TEMPORAL HASTA REINICIO no los deshará ni los convertirá en temporales. " +
                        "Solo auto-restaurará los cambios nuevos que aplique en esta sesión.\r\n\r\n" +
                        "Si quieres que todo el boost sea 100% temporal, restaura primero esos cambios persistentes desde la pestaña RESTAURAR.",
                        "ServiceKiller - modo temporal", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }

            PresetKind previousPreset = _currentPreset;
            _applyMode = mode;
            StyleApplyModeButtons();

            // Perfil y modo son conceptos independientes. Cambiar PERSISTENTE/TEMPORAL
            // no convierte por sí solo un preset en PERSONALIZADO. Si había un preset
            // conocido, se recalcula bajo las reglas del nuevo modo.
            if (previousPreset != PresetKind.Custom)
            {
                ApplyPreset(previousPreset);
            }
            else
            {
                _activeUserProfileId = null;
                _applyingPreset = true;
                foreach (TweakRowControl row in _rows.Values) row.SetApplyMode(mode);
                _applyingPreset = false;
                _currentPreset = PresetKind.Custom;
                _profileLabel.Text = "Perfil: PERSONALIZADO";
                UpdatePresetButtons();
                UpdateSelectionSummary();
            }

            if (_activeDetailRow != null) OnRowActivated(_activeDetailRow, EventArgs.Empty);
        }

        private void StyleApplyModeButtons()
        {
            StylePresetButton(_persistentModeButton, _applyMode == ApplyMode.Persistent);
            StylePresetButton(_sessionModeButton, _applyMode == ApplyMode.UntilRestart);
        }

        private void OnRowSelectionChanged(object sender, EventArgs e)
        {
            if (!_applyingPreset)
            {
                _activeUserProfileId = null;
                _currentPreset = PresetKind.Custom;
                _profileLabel.Text = "Perfil: PERSONALIZADO";
                UpdatePresetButtons();
            }
            UpdateSelectionSummary();
        }

        private void OnRowActivated(object sender, EventArgs e)
        {
            TweakRowControl row = sender as TweakRowControl;
            if (row == null) return;

            if (_activeDetailRow != null && !object.ReferenceEquals(_activeDetailRow, row))
                _activeDetailRow.SetDetailActive(false);

            _activeDetailRow = row;
            _activeDetailRow.SetDetailActive(true);

            TweakDefinition tweak = row.Definition;
            HashSet<string> persistentApplied = _engine.GetAppliedIds();
            HashSet<string> sessionApplied = _sessionEngine.GetAppliedIds();
            HashSet<string> allApplied = new HashSet<string>(persistentApplied, StringComparer.OrdinalIgnoreCase);
            allApplied.UnionWith(sessionApplied);
            TweakRuntimeState state = _engine.GetRuntimeState(tweak, allApplied);
            state.IsSessionApplied = sessionApplied.Contains(tweak.Id);
            _detailTitle.Text = tweak.Name;
            _detailDescription.Text = tweak.Description;
            _detailConsequences.Text = tweak.Consequences;
            string extra = string.Empty;
            if (tweak.IsCustomApplication)
            {
                extra = Environment.NewLine + Environment.NewLine +
                        "Origen añadido: " + (string.IsNullOrWhiteSpace(tweak.CustomSourcePath) ? "—" : tweak.CustomSourcePath) + Environment.NewLine +
                        "Destino/launcher: " + (string.IsNullOrWhiteSpace(tweak.CustomLaunchTargetPath) ? "—" : tweak.CustomLaunchTargetPath) + Environment.NewLine +
                        "Proceso objetivo: " + (string.IsNullOrWhiteSpace(tweak.CustomProcessName) ? "—" : tweak.CustomProcessName + ".exe") + Environment.NewLine +
                        "Detección: " + (string.IsNullOrWhiteSpace(tweak.CustomDetectionNote) ? "—" : tweak.CustomDetectionNote);
            }

            string appResources = string.Empty;
            if (tweak.IsApplication && tweak.ChangeKind == ChangeKind.Temporary && state.IsApplicationRunning)
            {
                appResources = state.ApplicationProcessCount > 0
                    ? "Procesos asociados: " + state.ApplicationProcessCount + " (raíces: " + state.ApplicationRootProcessCount + ")" + Environment.NewLine +
                      "RAM asociada aproximada: " + FormatMb(state.ApplicationMemoryMb) + Environment.NewLine
                    : "Residencia/servicio activo sin RAM atribuible a un proceso concreto." + Environment.NewLine;
            }

            _detailTechnical.Text = "Estado: " + state.Summary + Environment.NewLine +
                                    "Tipo de cambio: " + EffectiveChangeText(tweak) + Environment.NewLine +
                                    "Beneficio esperado: " + BenefitText(tweak.PerformanceBenefit) + Environment.NewLine +
                                    "Impacto funcional: " + ImpactText(tweak.Impact) + Environment.NewLine +
                                    "Modificado por ServiceKiller: " + (state.IsAppliedByServiceKiller ? (state.IsSessionApplied ? "Sí · SESIÓN TEMPORAL" : "Sí · PERSISTENTE") : "No") + Environment.NewLine +
                                    appResources +
                                    (_applyMode == ApplyMode.UntilRestart && !tweak.SupportsUntilRestartMode() && !tweak.IsProtectedInfo ? "Modo temporal: esta opción se excluye porque solo tendría efecto después de reiniciar/iniciar sesión." + Environment.NewLine : string.Empty) + Environment.NewLine +
                                    state.Details + extra;
        }

        private async void OnRunNow(object sender, EventArgs e)
        {
            TweakRowControl row = sender as TweakRowControl;
            if (row == null) return;
            if (!row.ActionAvailable)
            {
                MessageBox.Show(this, "Esta aplicación no está instalada actualmente. ServiceKiller conserva la entrada, pero no hay ninguna acción que ejecutar.", "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            TweakDefinition tweak = row.Definition;
            string privilegeNote = _isAdministrator ? "" : "\r\n\r\nWindows pedirá permisos de administrador solo después de confirmar.";
            DialogResult confirm = MessageBox.Show(this, "Cerrar ahora: " + tweak.Name + "?" + privilegeNote, "ServiceKiller", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            SetBusy(true, "Cerrando " + tweak.Name + "...");
            try
            {
                if (_isAdministrator)
                {
                    await Task.Run(delegate { _engine.Apply(new TweakDefinition[] { tweak }); });
                }
                else
                {
                    ElevatedActionResult elevated = await Task.Run(delegate { return ElevationManager.Run("apply", new string[] { tweak.Id }); });
                    if (elevated.Cancelled)
                    {
                        MessageBox.Show(this, elevated.Message, "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    if (!elevated.Success)
                    {
                        MessageBox.Show(this, elevated.Message, "ServiceKiller - operación elevada", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                LoadExistingLog();
                RefreshSystemView(null);
            }
            catch (Exception ex)
            {
                _log.Error("Error en cierre inmediato: " + ex.Message);
                MessageBox.Show(this, "No se pudo completar el cierre:\r\n" + ex.Message, "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private void PreviewSelected()
        {
            List<TweakDefinition> selected = GetSelectedTweaks();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "No hay ninguna acción seleccionada.", "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string preview = BuildPreview(selected);
            using (PreviewForm form = new PreviewForm(_isAdministrator ? "CAMBIOS PENDIENTES" : "SIMULACIÓN DE CAMBIOS", preview, "Cerrar"))
            {
                form.ShowDialog(this);
            }
        }

        private async void ApplySelected()
        {
            List<TweakDefinition> selected = GetSelectedTweaks();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "No hay ninguna acción seleccionada.", "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string preview = BuildPreview(selected);
            if (!_isAdministrator)
                preview += "\r\n\r\n" + new string('─', 78) + "\r\nMODO SOLO LECTURA: al confirmar, Windows solicitará UAC. Hasta ese momento no se modifica nada.";

            using (PreviewForm form = new PreviewForm("CONFIRMAR CAMBIOS", preview, _isAdministrator ? "APLICAR" : "PEDIR ADMIN Y APLICAR"))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
            }

            _lastBefore = _metricsReader.Read();
            SetBusy(true, _isAdministrator ? "Aplicando cambios..." : "Esperando autorización UAC...");
            try
            {
                ApplyResult result = null;
                ElevatedActionResult elevated = null;

                if (_isAdministrator)
                {
                    Stopwatch boostTimer = Stopwatch.StartNew();
                    if (_applyMode == ApplyMode.UntilRestart)
                    {
                        SessionApplyCoordinator coordinator = new SessionApplyCoordinator(_log);
                        result = await Task.Run(delegate { return coordinator.Apply(selected); });
                    }
                    else
                    {
                        result = await Task.Run(delegate { return _engine.Apply(selected); });
                    }
                    boostTimer.Stop();
                    if (result != null) result.DurationMilliseconds = boostTimer.ElapsedMilliseconds;
                }
                else
                {
                    List<string> ids = selected.Select(delegate(TweakDefinition t) { return t.Id; }).ToList();
                    string operation = _applyMode == ApplyMode.UntilRestart ? "apply-session" : "apply";
                    elevated = await Task.Run(delegate { return ElevationManager.Run(operation, ids); });
                    if (elevated.Cancelled)
                    {
                        MessageBox.Show(this, elevated.Message, "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    // Un APPLY puede devolver incidencias parciales y aun así aportar
                    // estadísticas útiles. Solo tratamos como fallo fatal una respuesta
                    // sin cabecera estructurada de V1.03.
                    if (!elevated.Success && elevated.SelectedActions == 0)
                    {
                        LoadExistingLog();
                        RefreshSystemView(null);
                        MessageBox.Show(this, elevated.Message, "ServiceKiller - operación elevada", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                LoadExistingLog();
                // Damos tiempo a que procesos/servicios terminen de liberar memoria antes de medir.
                await Task.Delay(1500);
                SystemMetrics afterMetrics = _metricsReader.Read();
                RefreshSystemView(_lastBefore);

                BoostSummaryData summary = new BoostSummaryData();
                summary.Profile = CurrentProfileDisplayName();
                summary.Mode = _applyMode == ApplyMode.UntilRestart ? "TEMPORAL HASTA REINICIO" : "PERSISTENTE";
                summary.Before = _lastBefore;
                summary.After = afterMetrics;

                string detailText;
                if (_isAdministrator)
                {
                    ApplyResult r = result ?? new ApplyResult();
                    summary.SelectedActions = r.SelectedActions;
                    summary.AppliedActions = r.AppliedActions;
                    summary.NoChangeActions = r.NoChangeActions;
                    summary.SkippedActions = r.SkippedActions;
                    summary.ErrorActions = r.ErrorActions;
                    summary.PersistentChanges = r.PersistentChanges;
                    summary.TemporaryActions = r.TemporaryActions;
                    summary.ProcessesClosed = r.ProcessesClosed;
                    summary.ServicesStopped = r.ServicesStopped;
                    summary.WindowsServicesStopped = r.WindowsServicesStopped;
                    summary.DurationMilliseconds = r.DurationMilliseconds;
                    summary.RestartRequired = _applyMode == ApplyMode.Persistent && r.RestartRequired;
                    detailText = string.Join(Environment.NewLine, r.Messages.ToArray());
                }
                else
                {
                    ElevatedActionResult r = elevated ?? new ElevatedActionResult();
                    summary.SelectedActions = r.SelectedActions > 0 ? r.SelectedActions : selected.Count;
                    summary.AppliedActions = r.AppliedActions;
                    summary.NoChangeActions = r.NoChangeActions;
                    summary.SkippedActions = r.SkippedActions;
                    summary.ErrorActions = r.ErrorActions;
                    summary.PersistentChanges = r.PersistentChanges;
                    summary.TemporaryActions = r.TemporaryActions;
                    summary.ProcessesClosed = r.ProcessesClosed;
                    summary.ServicesStopped = r.ServicesStopped;
                    summary.WindowsServicesStopped = r.WindowsServicesStopped;
                    summary.DurationMilliseconds = r.DurationMilliseconds;
                    summary.RestartRequired = _applyMode == ApplyMode.Persistent && r.RestartRequired;
                    detailText = r.Message ?? string.Empty;
                }

                if (_applyMode == ApplyMode.UntilRestart)
                {
                    summary.RestartStatus = "NO NECESARIO";
                }
                else if (summary.RestartRequired)
                {
                    summary.RestartStatus = "NECESARIO";
                }
                else if (summary.PersistentChanges > 0)
                {
                    summary.RestartStatus = "RECOMENDADO";
                }
                else
                {
                    summary.RestartStatus = "NO";
                }

                string notes = "Acciones temporales ejecutadas: " + summary.TemporaryActions + Environment.NewLine;
                if (_applyMode == ApplyMode.UntilRestart)
                {
                    notes += "No es necesario reiniciar. Si reinicias o cierras sesión, ServiceKiller restaurará automáticamente los cambios temporales." + Environment.NewLine;
                }
                else if (summary.RestartRequired)
                {
                    notes += "REINICIO NECESARIO. Uno o más cambios requieren reiniciar Windows para aplicarse completamente." + Environment.NewLine;
                }
                else if (summary.PersistentChanges > 0)
                {
                    notes += "REINICIO RECOMENDADO. Reiniciar Windows permite aplicar el estado optimizado desde el arranque y puede liberar procesos, servicios y memoria adicionales." + Environment.NewLine;
                }
                notes += "Las métricas de RAM/procesos/servicios son una fotografía antes/después y pueden fluctuar por actividad normal de Windows." + Environment.NewLine + Environment.NewLine;
                summary.DetailText = notes + detailText;

                string boostLog = BuildBoostLogBlock(summary);
                _log.Info(boostLog);
                SaveLastBoostSummary(summary);
                LoadExistingLog();
                RefreshInfoPanel();

                SetBusy(false, null);
                using (BoostSummaryForm form = new BoostSummaryForm(summary))
                    form.ShowDialog(this);
            }
            catch (Exception ex)
            {
                _log.Error("Error no controlado aplicando selección: " + ex.Message);
                MessageBox.Show(this, "La operación se interrumpió:\r\n" + ex.Message + "\r\n\r\nRevisa LOG y RESTAURAR antes de continuar.", "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Error);
                RefreshRestoreTab();
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private async void RestoreSessionNow()
        {
            List<TweakBackup> backups = _sessionEngine.GetActiveBackups();
            if (backups.Count == 0)
            {
                MessageBox.Show(this, "No hay una sesión temporal pendiente de restauración.", "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string text = "Se restaurarán AHORA " + backups.Count + " cambio(s) de la sesión temporal.\r\n\r\n" +
                          string.Join("\r\n", backups.Select(delegate(TweakBackup b) { return "• " + b.TweakName; }).ToArray()) +
                          "\r\n\r\nLa tarea automática del próximo reinicio/logon se eliminará si la restauración termina correctamente.";
            if (!_isAdministrator) text += "\r\n\r\nWindows solicitará UAC para realizar la restauración.";

            using (PreviewForm form = new PreviewForm("RESTAURAR SESIÓN TEMPORAL", text, _isAdministrator ? "RESTAURAR AHORA" : "PEDIR ADMIN Y RESTAURAR"))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
            }

            SystemMetrics restoreBefore = _metricsReader.Read();
            Stopwatch restoreTimer = Stopwatch.StartNew();
            SetBusy(true, _isAdministrator ? "Restaurando sesión temporal..." : "Esperando autorización UAC...");
            try
            {
                string message;
                if (_isAdministrator)
                {
                    SessionApplyCoordinator coordinator = new SessionApplyCoordinator(_log);
                    List<string> messages = await Task.Run(delegate { return coordinator.RestoreNow(); });
                    message = string.Join(Environment.NewLine, messages.ToArray());
                }
                else
                {
                    ElevatedActionResult elevated = await Task.Run(delegate { return ElevationManager.Run("restore-session-now", new string[0]); });
                    if (elevated.Cancelled)
                    {
                        MessageBox.Show(this, elevated.Message, "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    if (!elevated.Success)
                    {
                        LoadExistingLog();
                        RefreshSystemView(null);
                        MessageBox.Show(this, elevated.Message, "ServiceKiller - restauración temporal", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    message = elevated.Message;
                }

                restoreTimer.Stop();
                await Task.Delay(1000);
                SystemMetrics restoreAfter = _metricsReader.Read();
                _log.Info(BuildRestoreLogBlock("SESIÓN TEMPORAL", backups.Count, message, restoreBefore, restoreAfter, restoreTimer.ElapsedMilliseconds));
                LoadExistingLog();
                RefreshSystemView(null);
                MessageBox.Show(this, message, "ServiceKiller - sesión temporal", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _log.Error("Error restaurando sesión temporal: " + ex.Message);
                MessageBox.Show(this, "La restauración temporal se interrumpió:\r\n" + ex.Message + "\r\n\r\nEl journal y la tarea automática se conservan para poder reintentar.", "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private async void RestoreSelected()
        {
            List<string> ids = new List<string>();
            foreach (KeyValuePair<string, CheckBox> item in _restoreChecks)
                if (item.Value.Checked) ids.Add(item.Key);
            if (ids.Count == 0)
            {
                MessageBox.Show(this, "Selecciona al menos un cambio para restaurar.", "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            await RestoreIds(ids);
        }

        private async void RestoreAll()
        {
            List<TweakBackup> sessionBackups = _sessionEngine.GetActiveBackups();
            List<TweakBackup> persistentBackups = _engine.GetActiveBackups();
            int requested = sessionBackups.Count + persistentBackups.Count;
            if (requested == 0)
            {
                MessageBox.Show(this, "No hay ningún cambio pendiente realizado por ServiceKiller.", "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string textPreview = "RESTAURACIÓN GLOBAL DE SERVICEKILLER\r\n\r\n" +
                                 "Se restaurarán TODOS los cambios todavía registrados, aunque procedan de otro perfil o de una versión anterior.\r\n\r\n";
            if (sessionBackups.Count > 0)
            {
                textPreview += "SESIÓN TEMPORAL (" + sessionBackups.Count + ")\r\n";
                foreach (TweakBackup backup in sessionBackups) textPreview += "• " + backup.TweakName + "\r\n";
                textPreview += "\r\n";
            }
            if (persistentBackups.Count > 0)
            {
                textPreview += "PERSISTENTES (" + persistentBackups.Count + ")\r\n";
                foreach (TweakBackup backup in persistentBackups) textPreview += "• " + backup.TweakName + "\r\n";
            }
            textPreview += "\r\nCada entrada solo se elimina del journal después de verificar que el estado real coincide con el backup.";
            if (!_isAdministrator) textPreview += "\r\n\r\nWindows solicitará UAC una sola vez para completar la restauración global.";

            using (PreviewForm form = new PreviewForm("RESTAURAR TODO PENDIENTE", textPreview, _isAdministrator ? "RESTAURAR TODO" : "PEDIR ADMIN Y RESTAURAR TODO"))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
            }

            SystemMetrics restoreBefore = _metricsReader.Read();
            Stopwatch restoreTimer = Stopwatch.StartNew();
            SetBusy(true, _isAdministrator ? "Restaurando todo lo pendiente..." : "Esperando autorización UAC...");
            try
            {
                string message;
                if (_isAdministrator)
                {
                    List<string> messages = new List<string>();
                    if (sessionBackups.Count > 0)
                    {
                        messages.Add("=== SESIÓN TEMPORAL ===");
                        SessionApplyCoordinator coordinator = new SessionApplyCoordinator(_log);
                        messages.AddRange(await Task.Run(delegate { return coordinator.RestoreNow(); }));
                    }

                    List<TweakBackup> persistentNow = _engine.GetActiveBackups();
                    if (persistentNow.Count > 0)
                    {
                        if (messages.Count > 0) messages.Add(string.Empty);
                        messages.Add("=== CAMBIOS PERSISTENTES ===");
                        List<string> ids = persistentNow.Select(delegate(TweakBackup b) { return b.TweakId; }).ToList();
                        messages.AddRange(await Task.Run(delegate { return _engine.Restore(ids); }));
                    }
                    message = string.Join(Environment.NewLine, messages.ToArray());
                }
                else
                {
                    ElevatedActionResult elevated = await Task.Run(delegate { return ElevationManager.Run("restore-all", new string[0]); });
                    if (elevated.Cancelled)
                    {
                        MessageBox.Show(this, elevated.Message, "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    if (!elevated.Success)
                    {
                        LoadExistingLog();
                        RefreshSystemView(null);
                        MessageBox.Show(this, elevated.Message, "ServiceKiller - restauración global", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    message = elevated.Message;
                }

                restoreTimer.Stop();
                await Task.Delay(1000);
                SystemMetrics restoreAfter = _metricsReader.Read();
                int remainingSession = _sessionEngine.GetActiveBackups().Count;
                int remainingPersistent = _engine.GetActiveBackups().Count;
                int remaining = remainingSession + remainingPersistent;
                message += Environment.NewLine + Environment.NewLine +
                           "ESTADO FINAL: " + (remaining == 0 ? "SIN CAMBIOS PENDIENTES" : remaining + " cambio(s) todavía pendiente(s)") +
                           " (persistentes: " + remainingPersistent + " · sesión: " + remainingSession + ").";

                _log.Info(BuildRestoreLogBlock("RESTAURACIÓN GLOBAL", requested, message, restoreBefore, restoreAfter, restoreTimer.ElapsedMilliseconds));
                LoadExistingLog();
                RefreshSystemView(null);
                MessageBox.Show(this, message + "\r\n\r\nSi restauraste Hyper-V/Sandbox, reinicia Windows.", "ServiceKiller - restauración global", MessageBoxButtons.OK, remaining == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                _log.Error("Error no controlado en restauración global: " + ex.Message);
                MessageBox.Show(this, "La restauración global se interrumpió:\r\n" + ex.Message + "\r\n\r\nLos journals se conservan para poder reintentar.", "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Error);
                RefreshRestoreTab();
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private async Task RestoreIds(List<string> ids)
        {
            string textPreview = "Se restaurarán " + ids.Count + " cambio(s) al estado exacto guardado antes de modificarlos.\r\n\r\n";
            foreach (string id in ids)
            {
                TweakBackup backup = _engine.GetActiveBackups().FirstOrDefault(delegate(TweakBackup b) { return string.Equals(b.TweakId, id, StringComparison.OrdinalIgnoreCase); });
                if (backup != null) textPreview += "• " + backup.TweakName + "\r\n";
            }
            textPreview += "\r\nLos cierres temporales de aplicaciones no requieren restauración; basta con volver a abrirlas.";
            if (!_isAdministrator)
                textPreview += "\r\n\r\nMODO SOLO LECTURA: al confirmar, Windows solicitará UAC para ejecutar la restauración.";

            using (PreviewForm form = new PreviewForm("RESTAURAR", textPreview, _isAdministrator ? "RESTAURAR" : "PEDIR ADMIN Y RESTAURAR"))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
            }

            SystemMetrics restoreBefore = _metricsReader.Read();
            Stopwatch restoreTimer = Stopwatch.StartNew();
            SetBusy(true, _isAdministrator ? "Restaurando..." : "Esperando autorización UAC...");
            try
            {
                string message;
                if (_isAdministrator)
                {
                    List<string> messages = await Task.Run(delegate { return _engine.Restore(ids); });
                    message = string.Join(Environment.NewLine, messages.ToArray());
                }
                else
                {
                    ElevatedActionResult elevated = await Task.Run(delegate { return ElevationManager.Run("restore", ids); });
                    if (elevated.Cancelled)
                    {
                        MessageBox.Show(this, elevated.Message, "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    if (!elevated.Success)
                    {
                        LoadExistingLog();
                        RefreshSystemView(null);
                        MessageBox.Show(this, elevated.Message, "ServiceKiller - restauración elevada", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    message = elevated.Message;
                }

                restoreTimer.Stop();
                await Task.Delay(1000);
                SystemMetrics restoreAfter = _metricsReader.Read();
                int remainingPersistent = _engine.GetActiveBackups().Count;
                if (remainingPersistent > 0)
                    message += Environment.NewLine + Environment.NewLine + "ATENCIÓN: quedan " + remainingPersistent + " cambio(s) persistente(s) pendientes. Usa RESTAURAR TODO PENDIENTE si quieres devolver todo ServiceKiller a su estado guardado.";
                else
                    message += Environment.NewLine + Environment.NewLine + "Estado final: no quedan cambios persistentes pendientes.";
                _log.Info(BuildRestoreLogBlock("CAMBIOS PERSISTENTES", ids.Count, message, restoreBefore, restoreAfter, restoreTimer.ElapsedMilliseconds));
                LoadExistingLog();
                RefreshSystemView(null);
                MessageBox.Show(this, message + "\r\n\r\nSi restauraste Hyper-V/Sandbox, reinicia Windows.", "ServiceKiller", MessageBoxButtons.OK, remainingPersistent > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _log.Error("Error no controlado restaurando: " + ex.Message);
                MessageBox.Show(this, "La restauración se interrumpió:\r\n" + ex.Message + "\r\n\r\nEl journal se conserva para poder reintentar.", "ServiceKiller", MessageBoxButtons.OK, MessageBoxIcon.Error);
                RefreshRestoreTab();
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private string BuildPreview(List<TweakDefinition> selected)
        {
            bool sessionMode = _applyMode == ApplyMode.UntilRestart;
            int originalPersistent = selected.Count(delegate(TweakDefinition t) { return t.ChangeKind == ChangeKind.Persistent; });
            int temporary = selected.Count(delegate(TweakDefinition t) { return t.ChangeKind == ChangeKind.Temporary; });
            int restart = selected.Count(delegate(TweakDefinition t) { return t.ChangeKind == ChangeKind.RestartRequired; });
            int untilRestart = sessionMode ? selected.Count(delegate(TweakDefinition t) { return t.ChangeKind != ChangeKind.Temporary && t.SupportsUntilRestartMode(); }) : 0;

            string text = "PERFIL: " + CurrentProfileDisplayName() + Environment.NewLine +
                          "MODO: " + (sessionMode ? "TEMPORAL · RESTAURAR AUTOMÁTICAMENTE AL REINICIAR/CERRAR SESIÓN" : "PERSISTENTE (POR DEFECTO)") + Environment.NewLine;
            if (sessionMode)
                text += "ACCIONES: " + selected.Count + "   |   Hasta reinicio: " + untilRestart + "   |   Cierres temporales: " + temporary + Environment.NewLine;
            else
                text += "ACCIONES: " + selected.Count + "   |   Persistentes: " + originalPersistent + "   Temporales: " + temporary + "   Reinicio: " + restart + Environment.NewLine;
            text += new string('─', 78) + Environment.NewLine + Environment.NewLine;

            HashSet<string> persistentAlready = sessionMode ? _engine.GetAppliedIds() : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TweakDefinition tweak in selected)
            {
                text += tweak.Name.ToUpperInvariant() + Environment.NewLine;
                text += "  Tipo de cambio efectivo: " + EffectiveChangeText(tweak) + Environment.NewLine;
                text += "  Beneficio esperado: " + BenefitText(tweak.PerformanceBenefit) + " · Impacto funcional: " + ImpactText(tweak.Impact) + Environment.NewLine;
                string detail = _engine.Preview(tweak);
                if (!string.IsNullOrEmpty(detail)) text += "  " + detail.Replace(Environment.NewLine, Environment.NewLine + "  ") + Environment.NewLine;
                if (sessionMode && tweak.ChangeKind != ChangeKind.Temporary)
                {
                    if (persistentAlready.Contains(tweak.Id))
                        text += "  ATENCIÓN: este cambio ya está activo de forma PERSISTENTE y no será restaurado por el modo temporal." + Environment.NewLine;
                    else
                        text += "  Restauración: el estado original de esta sesión se guardará y se recuperará automáticamente en el próximo logon tras reiniciar/cerrar sesión." + Environment.NewLine;
                }
                text += "  Consecuencia: " + tweak.Consequences + Environment.NewLine + Environment.NewLine;
            }

            text += new string('─', 78) + Environment.NewLine;
            if (sessionMode)
            {
                text += "MODO TEMPORAL: ServiceKiller usa un journal SEPARADO de los cambios persistentes y programa una tarea de restauración antes de modificar Windows." + Environment.NewLine;
                text += "Hyper-V/BCD y cambios que solo afectan al inicio automático se excluyen porque no pueden aportar efecto útil ahora y quedar restaurados en el mismo reinicio." + Environment.NewLine;
                text += "Si ya existen cambios persistentes de ServiceKiller, se respetan: el modo temporal NO los convierte ni los revierte automáticamente." + Environment.NewLine;
            }
            else
            {
                text += "Antes de cualquier cambio persistente se guarda el estado original." + Environment.NewLine;
            }
            text += "BENEFICIO ESPERADO es una estimación cualitativa de actividad en segundo plano; no son FPS garantizados." + Environment.NewLine;
            text += "IMPACTO FUNCIONAL indica cuánto puedes notar la pérdida de esa función." + Environment.NewLine;
            text += "Bluetooth, seguridad, actualización, audio/cámara y red base permanecen protegidos.";
            if (!_isAdministrator)
                text += Environment.NewLine + Environment.NewLine + "MODO SOLO LECTURA: esta vista es una simulación. No se ha modificado el sistema.";
            return text;
        }

        private List<TweakDefinition> GetSelectedTweaks()
        {
            List<TweakDefinition> selected = new List<TweakDefinition>();
            foreach (TweakRowControl row in _rows.Values)
                if (!row.Definition.IsProtectedInfo && row.SelectableForCurrentMode && row.Selected) selected.Add(row.Definition);
            return selected.OrderBy(delegate(TweakDefinition t) { return t.IsApplication ? 1 : 0; })
                .ThenBy(delegate(TweakDefinition t) { return t.Category; })
                // V1.1.2.5: si una aplicación tiene acción de startup y cierre, respaldamos primero
                // el estado de arranque y después cerramos la residencia. Es especialmente importante
                // para servicios auxiliares como reWASDService: el backup refleja el estado pre-boost.
                .ThenBy(delegate(TweakDefinition t) { return t.IsApplication && (t.IsStartupOnlyAction || t.IsCustomStartupAction) ? 0 : 1; })
                .ToList();
        }

        private void UpdateSelectionSummary()
        {
            List<TweakDefinition> selected = GetSelectedTweaks();
            int temporary = selected.Count(delegate(TweakDefinition t) { return t.ChangeKind == ChangeKind.Temporary; });
            string prefix = _isAdministrator ? "" : "SOLO LECTURA · ";

            if (_applyMode == ApplyMode.UntilRestart)
            {
                int untilRestart = selected.Count(delegate(TweakDefinition t) { return t.ChangeKind != ChangeKind.Temporary && t.SupportsUntilRestartMode(); });
                string restoreNote = untilRestart > 0 ? "auto-restauración al aplicar" : "sin cambios de Windows que restaurar";
                _selectionSummary.Text = prefix + selected.Count + " acciones seleccionadas   ·   " + untilRestart + " hasta reinicio   ·   " + temporary + " cierres temporales   ·   " + restoreNote;
            }
            else
            {
                int persistent = selected.Count(delegate(TweakDefinition t) { return t.ChangeKind == ChangeKind.Persistent; });
                int restart = selected.Count(delegate(TweakDefinition t) { return t.ChangeKind == ChangeKind.RestartRequired; });
                _selectionSummary.Text = prefix + selected.Count + " acciones seleccionadas   ·   " + persistent + " persistentes   ·   " + temporary + " temporales   ·   " + restart + " con reinicio";
            }
        }

        private void RefreshAllStates()
        {
            HashSet<string> persistent = _engine.GetAppliedIds();
            HashSet<string> session = _sessionEngine.GetAppliedIds();
            HashSet<string> applied = new HashSet<string>(persistent, StringComparer.OrdinalIgnoreCase);
            applied.UnionWith(session);
            foreach (TweakRowControl row in _rows.Values)
            {
                try
                {
                    TweakRuntimeState state = _engine.GetRuntimeState(row.Definition, applied);
                    state.IsSessionApplied = session.Contains(row.Definition.Id);
                    row.SetApplyMode(_applyMode);
                    row.UpdateState(state);
                }
                catch (Exception ex)
                {
                    row.UpdateState(new TweakRuntimeState { Summary = "Error al leer", Details = ex.Message, IsAppliedByServiceKiller = applied.Contains(row.Definition.Id), IsSessionApplied = session.Contains(row.Definition.Id), IsActionAvailable = true });
                }
            }
            RefreshApplicationCardVisuals();
            UpdateSelectionSummary();
        }

        private void RefreshApplicationCardVisuals()
        {
            foreach (ApplicationCardVisual visual in _applicationCards.Values)
            {
                bool hasRows = false;
                bool anyAvailable = false;
                foreach (string id in visual.TweakIds)
                {
                    TweakRowControl row;
                    if (!_rows.TryGetValue(id, out row)) continue;
                    hasRows = true;
                    if (row.ActionAvailable) { anyAvailable = true; break; }
                }

                bool unavailable = hasRows && !anyAvailable;
                visual.Card.BackColor = unavailable ? Theme.DisabledPanel : Theme.Panel2;
                visual.RowsPanel.BackColor = unavailable ? Theme.DisabledPanel : Theme.Panel;
                visual.Title.ForeColor = unavailable ? Theme.DisabledText : Theme.Text;
                visual.Title.Text = unavailable
                    ? visual.OriginalTitle + "   ·   NO INSTALADO"
                    : visual.OriginalTitle;
            }
        }

        private void RefreshRestoreTab()
        {
            _restoreFlow.SuspendLayout();
            _restoreFlow.Controls.Clear();
            _restoreChecks.Clear();

            List<TweakBackup> sessionBackups = _sessionEngine.GetActiveBackups();
            if (sessionBackups.Count > 0)
            {
                Label sessionHeading = Theme.MakeLabel("SESIÓN TEMPORAL ACTIVA", 13f, true, Theme.Low);
                sessionHeading.Width = 1100;
                sessionHeading.Height = 20;
                _restoreFlow.Controls.Add(sessionHeading);

                Panel sessionPanel = new Panel();
                sessionPanel.Width = 900;
                sessionPanel.Height = 54;
                sessionPanel.BackColor = Theme.SelectedRow;
                sessionPanel.Margin = new Padding(0, 0, 0, 8);
                _restoreFlow.Controls.Add(sessionPanel);

                TableLayoutPanel sessionLayout = new TableLayoutPanel();
                sessionLayout.Dock = DockStyle.Fill;
                sessionLayout.Padding = new Padding(10, 4, 10, 4);
                sessionLayout.ColumnCount = 2;
                sessionLayout.RowCount = 2;
                sessionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72f));
                sessionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
                sessionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
                sessionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
                sessionPanel.Controls.Add(sessionLayout);

                Label sessionText = Theme.MakeLabel(sessionBackups.Count + " cambio(s) de Windows se restaurarán automáticamente en el próximo inicio de sesión tras reiniciar/cerrar sesión.", 9.2f, true, Theme.Text);
                sessionText.AutoSize = false;
                sessionText.Dock = DockStyle.Fill;
                sessionText.TextAlign = ContentAlignment.BottomLeft;
                sessionLayout.Controls.Add(sessionText, 0, 0);

                Label taskText = Theme.MakeLabel("Journal: session-state.json · Auto-restauración programada", 8.4f, false, Theme.Muted);
                taskText.AutoSize = false;
                taskText.Dock = DockStyle.Fill;
                taskText.TextAlign = ContentAlignment.TopLeft;
                sessionLayout.Controls.Add(taskText, 0, 1);

                Button restoreSession = Theme.MakeButton(_isAdministrator ? "RESTAURAR SESIÓN AHORA" : "RESTAURAR SESIÓN (ADMIN)", false);
                restoreSession.Dock = DockStyle.Fill;
                restoreSession.Margin = new Padding(8, 3, 0, 3);
                restoreSession.Click += delegate { RestoreSessionNow(); };
                sessionLayout.Controls.Add(restoreSession, 1, 0);
                sessionLayout.SetRowSpan(restoreSession, 2);
            }

            Label heading = Theme.MakeLabel("CAMBIOS PERSISTENTES ACTIVOS · " + _engine.GetActiveBackups().Count, 13f, true, Theme.Text);
            heading.Width = 1100;
            heading.Height = 22;
            _restoreFlow.Controls.Add(heading);

            Label restoreScopeNote = Theme.MakeLabel("RESTAURAR TODO PENDIENTE incluye journals heredados de perfiles y versiones anteriores; RESTAURAR SELECCIONADOS es una restauración parcial.", 9.1f, false, Theme.Muted);
            restoreScopeNote.Width = 1000;
            restoreScopeNote.Height = 28;
            _restoreFlow.Controls.Add(restoreScopeNote);

            List<TweakBackup> backups = _engine.GetActiveBackups();
            if (backups.Count == 0)
            {
                Label none = Theme.MakeLabel("ServiceKiller no tiene cambios persistentes pendientes de restaurar.", 10f, false, Theme.Muted);
                none.Width = 1000;
                none.Height = 28;
                _restoreFlow.Controls.Add(none);
                _restoreFlow.ResumeLayout();
                ResizeFlowChildren(_restoreFlow, 820);
                return;
            }

            foreach (TweakBackup backup in backups.OrderByDescending(delegate(TweakBackup b) { return b.AppliedUtc; }))
            {
                Panel row = new Panel();
                row.Width = 900;
                row.Height = 36;
                row.BackColor = Theme.Panel;
                row.Margin = new Padding(0, 0, 0, 1);
                _restoreFlow.Controls.Add(row);

                TableLayoutPanel layout = new TableLayoutPanel();
                layout.Dock = DockStyle.Fill;
                layout.Padding = new Padding(8, 1, 10, 1);
                layout.ColumnCount = 3;
                layout.RowCount = 2;
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38f));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 78f));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
                row.Controls.Add(layout);

                CheckBox check = new CheckBox();
                check.Dock = DockStyle.Fill;
                check.Margin = new Padding(0);
                layout.Controls.Add(check, 0, 0);
                layout.SetRowSpan(check, 2);
                _restoreChecks[backup.TweakId] = check;

                Label name = Theme.MakeLabel(backup.TweakName, 11.5f, true, Theme.Text);
                name.AutoSize = false;
                name.Dock = DockStyle.Fill;
                name.TextAlign = ContentAlignment.BottomLeft;
                name.AutoEllipsis = true;
                layout.Controls.Add(name, 1, 0);

                int components = backup.Services.Count + backup.RegistryValues.Count + backup.StartupEntries.Count + backup.BootValues.Count;
                Label meta = Theme.MakeLabel("Aplicado: " + backup.AppliedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + " · " + components + " componente(s) respaldados", 10.5f, false, Theme.Muted);
                meta.AutoSize = false;
                meta.Dock = DockStyle.Fill;
                meta.TextAlign = ContentAlignment.TopLeft;
                meta.AutoEllipsis = true;
                layout.Controls.Add(meta, 1, 1);

                Label badge = Theme.MakeLabel("RESTAURABLE", 8.5f, true, Theme.Modified);
                badge.AutoSize = false;
                badge.Dock = DockStyle.Fill;
                badge.TextAlign = ContentAlignment.MiddleRight;
                layout.Controls.Add(badge, 2, 0);
                layout.SetRowSpan(badge, 2);
            }
            _restoreFlow.ResumeLayout();
            ResizeFlowChildren(_restoreFlow, 820);
        }

        private void RefreshSessionIndicator()
        {
            if (_sessionStatusLabel == null) return;
            int count = 0;
            try { count = _sessionEngine.GetActiveBackups().Count; } catch { }
            _sessionStatusLabel.Visible = count > 0;
            _sessionStatusLabel.Text = count > 0
                ? "● SESIÓN TEMPORAL ACTIVA · " + count + " cambio(s) pendiente(s)"
                : "● SESIÓN TEMPORAL ACTIVA";
        }

        private void RefreshMetrics(SystemMetrics before)
        {
            SystemMetrics now = _metricsReader.Read();
            int dpiPercent = (int)Math.Round(DeviceDpi * 100.0 / 96.0);
            string baseText = "DPI: " + dpiPercent + "%   |   Servicios: " + now.RunningServices + "   Procesos: " + now.Processes +
                              "   RAM usada: " + FormatMb(now.UsedMemoryMb) +
                              "   Disponible: " + FormatMb(now.AvailableMemoryMb) +
                              "   Total: " + FormatMb(now.TotalMemoryMb);
            if (before != null)
            {
                int ds = now.RunningServices - before.RunningServices;
                int dp = now.Processes - before.Processes;
                long dm = now.UsedMemoryMb - before.UsedMemoryMb;
                long da = now.AvailableMemoryMb - before.AvailableMemoryMb;
                baseText += "   |   Δ " + Signed(ds) + " srv, " + Signed(dp) + " proc, usada " + SignedMb(dm) + ", disp. " + SignedMb(da);
            }
            _metricsLabel.Text = baseText;
        }

        private void SetBusy(bool busy, string text)
        {
            UseWaitCursor = busy;
            bool activeJournalSafe = !_store.SafetyLocked && (_applyMode != ApplyMode.UntilRestart || !_sessionStore.SafetyLocked);
            _applyButton.Enabled = !busy && activeJournalSafe;
            _previewButton.Enabled = !busy;
            if (_refreshButton != null) _refreshButton.Enabled = !busy;
            if (_diagnosticButton != null) _diagnosticButton.Enabled = !busy;
            if (busy && !string.IsNullOrEmpty(text)) _selectionSummary.Text = text;
            if (!busy) UpdateSelectionSummary();
        }

        private void LoadExistingLog()
        {
            List<string> blocks = new List<string>();
            try
            {
                if (System.IO.File.Exists(AppPaths.LogFile))
                    blocks.Add("===== LOG DE CAMBIOS ELEVADOS / MÁQUINA =====\r\n" + System.IO.File.ReadAllText(AppPaths.LogFile));
            }
            catch
            {
                blocks.Add("===== LOG DE CAMBIOS ELEVADOS / MÁQUINA =====\r\n(No accesible con el token actual)");
            }

            try
            {
                if (System.IO.File.Exists(AppPaths.UserLogFile))
                    blocks.Add("===== LOG DE SESIÓN / USUARIO =====\r\n" + System.IO.File.ReadAllText(AppPaths.UserLogFile));
            }
            catch { }

            _logText.Text = string.Join("\r\n\r\n", blocks.ToArray());
            _logText.SelectionStart = _logText.TextLength;
            _logText.ScrollToCaret();
        }

        private void OnLogLine(string line)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(OnLogLine), line); } catch { }
                return;
            }
            _logText.AppendText(line + Environment.NewLine);
        }

        private static void ConfigureResponsiveFlow(FlowLayoutPanel flow, int minimumWidth)
        {
            // V1.1: un ancho menor ya no bloquea la ventana. Se conserva una superficie
            // interna legible y AutoScroll proporciona desplazamiento horizontal.
            flow.AutoScroll = true;
            flow.AutoScrollMinSize = new Size(minimumWidth, 0);
            flow.SizeChanged += delegate { ResizeFlowChildren(flow, minimumWidth); };
            flow.Layout += delegate { ResizeFlowChildren(flow, minimumWidth); };
            ResizeFlowChildren(flow, minimumWidth);
        }

        private static void ResizeFlowChildren(FlowLayoutPanel flow, int minimumWidth)
        {
            if (flow == null || flow.IsDisposed) return;
            int scrollbarReserve = SystemInformation.VerticalScrollBarWidth + 12;
            int available = flow.ClientSize.Width - flow.Padding.Left - flow.Padding.Right - scrollbarReserve;
            int width = Math.Max(minimumWidth, available);
            foreach (Control control in flow.Controls)
            {
                if (control.Width != width) control.Width = width;
            }
        }

        private void RestoreWindowPlacement()
        {
            Rectangle working = Screen.FromControl(this).WorkingArea;
            bool restored = false;
            try
            {
                string uiPath = File.Exists(AppPaths.UiState) ? AppPaths.UiState : AppPaths.LegacyUiState;
                if (File.Exists(uiPath))
                {
                    string[] parts = File.ReadAllText(uiPath).Trim().Split('|');
                    if (parts.Length >= 5)
                    {
                        int x, y, w, h;
                        bool max;
                        if (int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y) &&
                            int.TryParse(parts[2], out w) && int.TryParse(parts[3], out h) &&
                            bool.TryParse(parts[4], out max) && w >= 640 && h >= 440)
                        {
                            Rectangle candidate = new Rectangle(x, y, w, h);
                            bool visible = Screen.AllScreens.Any(delegate(Screen screen)
                            {
                                Rectangle intersection = Rectangle.Intersect(screen.WorkingArea, candidate);
                                return intersection.Width >= 200 && intersection.Height >= 120;
                            });
                            if (visible)
                            {
                                StartPosition = FormStartPosition.Manual;
                                Bounds = candidate;
                                if (max) WindowState = FormWindowState.Maximized;
                                restored = true;
                            }
                        }
                    }
                }
            }
            catch { }

            if (!restored)
            {
                int width = Math.Max(1180, (int)(working.Width * 0.72));
                int height = Math.Max(720, (int)(working.Height * 0.86));
                width = Math.Min(width, working.Width - 40);
                height = Math.Min(height, working.Height - 40);
                StartPosition = FormStartPosition.Manual;
                SetBounds(working.Left + (working.Width - width) / 2,
                          working.Top + (working.Height - height) / 2, width, height);
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                AppPaths.EnsureUser();
                Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                bool maximized = WindowState == FormWindowState.Maximized;
                string text = bounds.X + "|" + bounds.Y + "|" + bounds.Width + "|" + bounds.Height + "|" + maximized;
                File.WriteAllText(AppPaths.UiState, text);
            }
            catch { }
        }

        private void UpdatePresetButtons()
        {
            StylePresetButton(_conservativeButton, _currentPreset == PresetKind.Conservative);
            StylePresetButton(_balancedButton, _currentPreset == PresetKind.Balanced);
            StylePresetButton(_aggressiveButton, _currentPreset == PresetKind.Aggressive);
        }

        private static void StylePresetButton(Button button, bool selected)
        {
            if (button == null) return;
            button.BackColor = selected ? Theme.Accent : Theme.Panel2;
            button.FlatAppearance.BorderSize = selected ? 0 : 1;
            button.FlatAppearance.BorderColor = Theme.Border;
        }

        private static void DrawTab(object sender, DrawItemEventArgs e)
        {
            TabControl tabs = sender as TabControl;
            if (tabs == null || e.Index < 0 || e.Index >= tabs.TabPages.Count) return;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Rectangle rect = e.Bounds;
            Color back = selected ? Theme.Accent : Theme.Panel2;
            using (SolidBrush brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, rect);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, rect, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private static TabPage MakeTab(string text)
        {
            TabPage page = new TabPage(text);
            page.BackColor = Theme.Back;
            page.ForeColor = Theme.Text;
            page.Padding = new Padding(0);
            return page;
        }

        private static Label MakeWrappedLabel(string text, int x, int y, int width, int height)
        {
            Label label = Theme.MakeLabel(text, 9f, false, Theme.Text);
            label.AutoSize = false;
            label.SetBounds(x, y, width, height);
            return label;
        }

        private static string PresetText(PresetKind preset)
        {
            if (preset == PresetKind.Conservative) return "CONSERVADOR";
            if (preset == PresetKind.Balanced) return "EQUILIBRADO";
            if (preset == PresetKind.Aggressive) return "AGRESIVO";
            return "PERSONALIZADO";
        }

        private string EffectiveChangeText(TweakDefinition tweak)
        {
            if (_applyMode == ApplyMode.UntilRestart && tweak != null && tweak.ChangeKind != ChangeKind.Temporary)
                return tweak.SupportsUntilRestartMode() ? "TEMPORAL HASTA REINICIO" : "NO APLICA EN MODO TEMPORAL";
            return tweak == null ? "—" : ChangeText(tweak.ChangeKind);
        }

        private static string ChangeText(ChangeKind kind)
        {
            if (kind == ChangeKind.Temporary) return "TEMPORAL";
            if (kind == ChangeKind.RestartRequired) return "REQUIERE REINICIO";
            return "PERSISTENTE";
        }

        private static string BenefitText(PerformanceBenefitLevel benefit)
        {
            if (benefit == PerformanceBenefitLevel.None) return "NULO";
            if (benefit == PerformanceBenefitLevel.VeryLow) return "MUY BAJO";
            if (benefit == PerformanceBenefitLevel.Low) return "BAJO";
            if (benefit == PerformanceBenefitLevel.Medium) return "MEDIO";
            return "ALTO";
        }

        private static string ImpactText(ImpactLevel impact)
        {
            if (impact == ImpactLevel.Low) return "BAJO";
            if (impact == ImpactLevel.Medium) return "MEDIO";
            return "ALTO";
        }

        private string BuildBoostLogBlock(BoostSummaryData summary)
        {
            SystemMetrics before = summary.Before ?? new SystemMetrics();
            SystemMetrics after = summary.After ?? new SystemMetrics();
            StringBuilder b = new StringBuilder();
            b.AppendLine("================ RESUMEN DEL BOOST ================");
            b.AppendLine("Fecha: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            b.AppendLine("Perfil: " + (string.IsNullOrWhiteSpace(summary.Profile) ? "—" : summary.Profile));
            b.AppendLine("Modo: " + (string.IsNullOrWhiteSpace(summary.Mode) ? "—" : summary.Mode));
            b.AppendLine();
            b.AppendLine("ANTES");
            b.AppendLine("Servicios ejecutándose: " + before.RunningServices);
            b.AppendLine("Procesos:                " + before.Processes);
            b.AppendLine("RAM usada:               " + FormatMb(before.UsedMemoryMb));
            b.AppendLine("RAM disponible:          " + FormatMb(before.AvailableMemoryMb));
            b.AppendLine("RAM total:               " + FormatMb(before.TotalMemoryMb));
            b.AppendLine();
            b.AppendLine("DESPUÉS");
            b.AppendLine("Servicios ejecutándose: " + after.RunningServices);
            b.AppendLine("Procesos:                " + after.Processes);
            b.AppendLine("RAM usada:               " + FormatMb(after.UsedMemoryMb));
            b.AppendLine("RAM disponible:          " + FormatMb(after.AvailableMemoryMb));
            b.AppendLine("RAM total:               " + FormatMb(after.TotalMemoryMb));
            b.AppendLine();
            b.AppendLine("DIFERENCIA");
            b.AppendLine("Servicios:               " + Signed(after.RunningServices - before.RunningServices));
            b.AppendLine("Procesos:                " + Signed(after.Processes - before.Processes));
            b.AppendLine("RAM usada:               " + SignedMb(after.UsedMemoryMb - before.UsedMemoryMb));
            b.AppendLine("RAM disponible:          " + SignedMb(after.AvailableMemoryMb - before.AvailableMemoryMb));
            b.AppendLine();
            b.AppendLine("RESULTADO");
            b.AppendLine("Acciones seleccionadas:  " + summary.SelectedActions);
            b.AppendLine("Cambios aplicados:       " + summary.AppliedActions);
            b.AppendLine("Sin cambio necesario:    " + summary.NoChangeActions);
            b.AppendLine("Omitidas:                " + summary.SkippedActions);
            b.AppendLine("Errores:                 " + summary.ErrorActions);
            b.AppendLine("Procesos cerrados:       " + summary.ProcessesClosed);
            b.AppendLine("Servicios Windows detenidos:         " + summary.WindowsServicesStopped);
            b.AppendLine("Servicios residentes apps detenidos: " + summary.ServicesStopped);
            b.AppendLine("Cambios con journal:     " + summary.PersistentChanges);
            b.AppendLine("Acciones temporales:     " + summary.TemporaryActions);
            b.AppendLine("Tiempo del boost:        " + FormatDuration(summary.DurationMilliseconds));
            b.AppendLine("Reinicio:                " + (string.IsNullOrWhiteSpace(summary.RestartStatus) ? (summary.RestartRequired ? "NECESARIO" : "NO") : summary.RestartStatus));
            b.Append("====================================================");
            return b.ToString();
        }

        private void SaveLastBoostSummary(BoostSummaryData summary)
        {
            try
            {
                AppPaths.EnsureUser();
                SystemMetrics before = summary.Before ?? new SystemMetrics();
                SystemMetrics after = summary.After ?? new SystemMetrics();
                StringBuilder b = new StringBuilder();
                b.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " · " + (summary.Profile ?? "—") + " · " + (summary.Mode ?? "—"));
                b.AppendLine("Resultado: " + summary.AppliedActions + " aplicada(s), " + summary.NoChangeActions + " sin cambio, " + summary.SkippedActions + " omitida(s), " + summary.ErrorActions + " error(es)");
                b.AppendLine("Procesos: " + before.Processes + " → " + after.Processes + " (" + Signed(after.Processes - before.Processes) + ")");
                b.AppendLine("Servicios: " + before.RunningServices + " → " + after.RunningServices + " (" + Signed(after.RunningServices - before.RunningServices) + ")");
                b.AppendLine("RAM usada: " + FormatMb(before.UsedMemoryMb) + " → " + FormatMb(after.UsedMemoryMb) + " (" + SignedMb(after.UsedMemoryMb - before.UsedMemoryMb) + ")");
                b.AppendLine("RAM disponible: " + FormatMb(before.AvailableMemoryMb) + " → " + FormatMb(after.AvailableMemoryMb) + " (" + SignedMb(after.AvailableMemoryMb - before.AvailableMemoryMb) + ")");
                b.AppendLine("Servicios Windows detenidos: " + summary.WindowsServicesStopped);
                b.AppendLine("Servicios residentes apps detenidos: " + summary.ServicesStopped);
                b.Append("Tiempo del boost: " + FormatDuration(summary.DurationMilliseconds));
                File.WriteAllText(AppPaths.LastBoostSummary, b.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        private static string ReadLastBoostSummary()
        {
            try
            {
                if (File.Exists(AppPaths.LastBoostSummary))
                {
                    string text = File.ReadAllText(AppPaths.LastBoostSummary, Encoding.UTF8).Trim();
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
            }
            catch { }
            return "Todavía no hay ningún boost registrado por esta versión/usuario.";
        }

        private string BuildRestoreLogBlock(string title, int requested, string messages, SystemMetrics before, SystemMetrics after, long durationMilliseconds)
        {
            int errors = CountOccurrences(messages, ": ERROR -");
            int restored = CountOccurrences(messages, ": restaurado");
            if (restored + errors == 0 && requested > 0 && errors == 0) restored = requested;
            int pending = Math.Max(0, requested - restored - errors);

            StringBuilder b = new StringBuilder();
            b.AppendLine("============= RESUMEN DE RESTAURACIÓN =============");
            b.AppendLine("Tipo: " + title);
            b.AppendLine("Fecha: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            b.AppendLine("Solicitados:              " + requested);
            b.AppendLine("Restaurados:              " + restored);
            b.AppendLine("Pendientes/no confirmados:" + " " + pending);
            b.AppendLine("Errores:                  " + errors);
            b.AppendLine("Tiempo total observado:   " + FormatDuration(durationMilliseconds));
            b.AppendLine();
            b.AppendLine("ANTES");
            b.AppendLine("Servicios:       " + before.RunningServices + " · Procesos: " + before.Processes);
            b.AppendLine("RAM usada:       " + FormatMb(before.UsedMemoryMb) + " · Disponible: " + FormatMb(before.AvailableMemoryMb));
            b.AppendLine("DESPUÉS");
            b.AppendLine("Servicios:       " + after.RunningServices + " · Procesos: " + after.Processes);
            b.AppendLine("RAM usada:       " + FormatMb(after.UsedMemoryMb) + " · Disponible: " + FormatMb(after.AvailableMemoryMb));
            b.AppendLine("DIFERENCIA");
            b.AppendLine("Servicios: " + Signed(after.RunningServices - before.RunningServices) + " · Procesos: " + Signed(after.Processes - before.Processes));
            b.AppendLine("RAM usada: " + SignedMb(after.UsedMemoryMb - before.UsedMemoryMb) + " · RAM disponible: " + SignedMb(after.AvailableMemoryMb - before.AvailableMemoryMb));
            b.Append("====================================================");
            return b.ToString();
        }

        private static int CountOccurrences(string text, string token)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token)) return 0;
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(token, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                index += token.Length;
            }
            return count;
        }

        private static string FormatDuration(long milliseconds)
        {
            if (milliseconds < 0) milliseconds = 0;
            if (milliseconds < 1000) return milliseconds + " ms";
            return (milliseconds / 1000.0).ToString("0.0") + " s";
        }

        private static string FormatMb(long mb)
        {
            if (mb >= 1024) return (mb / 1024.0).ToString("0.0") + " GB";
            return mb + " MB";
        }

        private static string Signed(int value)
        {
            return value > 0 ? "+" + value : value.ToString();
        }

        private static string SignedMb(long value)
        {
            string text = FormatMb(Math.Abs(value));
            if (value > 0) return "+" + text;
            if (value < 0) return "-" + text;
            return "0 MB";
        }


        private sealed class ApplicationCardVisual
        {
            public Panel Card { get; set; }
            public Label Title { get; set; }
            public Panel RowsPanel { get; set; }
            public string OriginalTitle { get; set; }
            public List<string> TweakIds { get; set; }
        }

        private static void OpenExplorer(string path)
        {
            try
            {
                if (System.IO.File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch { }
        }
    }
}
