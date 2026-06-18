using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using FileSender.Models;
using FileSender.Network;

namespace FileSender
{
    public sealed class MainForm : Form
    {
        private enum ExistingItemMode
        {
            Ask,
            CopyMissing,
            RecopyAll
        }

        private static readonly Color AppBackground = Color.FromArgb(246, 248, 251);
        private static readonly Color PanelBackground = Color.White;
        private static readonly Color PrimaryColor = Color.FromArgb(31, 94, 184);
        private static readonly Color PrimarySelectedColor = Color.FromArgb(219, 234, 254);
        private static readonly Color CompleteProgressColor = Color.FromArgb(56, 189, 248);
        private static readonly Color BorderColor = Color.FromArgb(211, 218, 230);
        private static readonly Color TextColor = Color.FromArgb(31, 41, 55);
        private static readonly Color MutedTextColor = Color.FromArgb(91, 101, 117);
        private static readonly Color FolderRowColor = Color.FromArgb(255, 248, 225);
        private static readonly Color FolderTextColor = Color.FromArgb(120, 72, 18);
        private static readonly Color FileRowColor = Color.White;
        private static readonly Color ParentRowColor = Color.FromArgb(239, 244, 250);
        private static readonly Color ParentTextColor = Color.FromArgb(67, 80, 98);

        private ComboBox _modeCombo;
        private ComboBox _scopeCombo;
        private ComboBox _ipText;
        private TextBox _portText;
        private TextBox _keyText;
        private Button _connectButton;
        private Button _discoverButton;
        private Label _localIpLabel;
        private TextBox _crocCodeText;
        private TextBox _crocReceiveCodeText;
        private TextBox _crocPathText;
        private TextBox _crocLogText;
        private TextBox _crocLocalPathText;
        private TextBox _crocReceiveDestinationText;
        private Label _crocStateLabel;
        private ProgressBar _crocProgressBar;
        private Label _crocProgressLabel;
        private DataGridView _crocLocalGrid;
        private Control _crocSendPanel;
        private Button _crocGenerateCodeButton;
        private Button _crocPickFileButton;
        private Button _crocPickFolderButton;
        private Button _crocLocalDrivesButton;
        private Button _crocLocalBrowseButton;
        private Button _crocLocalUpButton;
        private Button _crocUseSelectionButton;
        private Button _crocSendRoleButton;
        private Button _crocReceiveRoleButton;
        private Button _crocReceiveBrowseButton;
        private Button _crocSendButton;
        private Button _crocReceiveButton;
        private Button _crocCancelButton;
        private TextBox _localPathText;
        private TextBox _remotePathText;
        private DataGridView _localGrid;
        private DataGridView _remoteGrid;
        private Button _browseLocalButton;
        private Button _localDrivesButton;
        private Button _localUpButton;
        private Button _remoteDrivesButton;
        private Button _remoteUpButton;
        private Button _remoteBrowseButton;
        private Button _sendButton;
        private ProgressBar _progressBar;
        private Label _progressLabel;
        private ProgressBar _fileProgressBar;
        private Label _fileProgressLabel;
        private Label _statusLabel;
        private Button _localModeButton;
        private Button _remoteModeButton;
        private Button _remoteDirectModeButton;
        private Button _localSendRoleButton;
        private Panel _modeHostPanel;
        private Control _localModePanel;
        private Control _remoteModePanel;
        private Control _localConnectionPanel;
        private Control _localFilePanel;
        private Control _localProgressPanel;
        private Control _crocRolePromptPanel;
        private Control _crocSendModePanel;
        private Control _crocReceiveModePanel;

        private TransferServer _server;
        private DiscoveryService _discovery;
        private PeerConnection _connection;
        private CrocTransferService _croc;
        private RemoteFolderPickerDialog _remoteFolderPicker;
        private AppSettings _settings;
        private string _localCurrentPath;
        private string _remoteCurrentPath;
        private string _crocReceiveFolder;
        private bool _directRemoteMode;
        private int _workMode;
        private int _crocRole;
        private bool _loadingCrocLocalPath;
        private bool _crocSelectionFromGrid;
        private Point _localDragStartPoint;
        private Point _remoteDragStartPoint;
        private bool _localDragDropArmed;
        private bool _remoteDragDropArmed;
        private int _localSelectionAnchorRow = -1;
        private int _remoteSelectionAnchorRow = -1;
        private List<string> _localDragPaths;
        private List<string> _remoteDragPaths;
        private readonly Queue<QueuedSend> _sendQueue = new Queue<QueuedSend>();
        private bool _sendQueueRunning;
        private readonly List<string> _crocSelectedPaths = new List<string>();
        private const string LocalPathsDragFormat = "FileSender.LocalPaths";
        private const string RemotePathsDragFormat = "FileSender.RemotePaths";
        private ExistingItemMode _existingItemMode = ExistingItemMode.Ask;

        private sealed class QueuedSend
        {
            public List<string> Paths { get; set; }
            public string DestinationDirectory { get; set; }
            public string Name { get; set; }
        }

        public MainForm()
        {
            Text = "File Sender";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Width = 1440;
            Height = 900;
            MinimumSize = new Size(1280, 760);
            StartPosition = FormStartPosition.CenterScreen;

            _settings = AppSettings.Load();
            BuildUi();
            ApplyVisualStyle(this);
            _localCurrentPath = Directory.Exists(_settings.LocalStartFolder)
                ? _settings.LocalStartFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            LoadLocalPath(_localCurrentPath);
            LoadCrocLocalPath(_localCurrentPath);
            SetStatus("Seleccione Modo Local, Modo Remoto o Remoto Directo para comenzar.");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_connection != null) _connection.Dispose();
            if (_server != null) _server.Dispose();
            if (_discovery != null) _discovery.Dispose();
            base.OnFormClosing(e);
        }

        private void BuildUi()
        {
            BackColor = AppBackground;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            var menu = new MenuStrip();
            menu.BackColor = PanelBackground;
            menu.ForeColor = TextColor;
            var settingsItem = new ToolStripMenuItem("Configuración");
            settingsItem.Click += (s, e) => OpenSettings();
            menu.Items.Add(settingsItem);
            MainMenuStrip = menu;
            Controls.Add(menu);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(10, 34, 10, 10) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            Controls.Add(root);

            root.Controls.Add(BuildModeSelector(), 0, 0);

            _modeHostPanel = new Panel { Dock = DockStyle.Fill };
            _localModePanel = BuildLocalModePanel();
            _remoteModePanel = BuildRemoteModePanel();
            _modeHostPanel.Controls.Add(_localModePanel);
            _modeHostPanel.Controls.Add(_remoteModePanel);
            _localModePanel.Visible = false;
            _remoteModePanel.Visible = false;
            root.Controls.Add(_modeHostPanel, 0, 1);

            _statusLabel = new Label { Dock = DockStyle.Fill, Text = "Listo.", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
            root.Controls.Add(_statusLabel, 0, 2);

            _croc = new CrocTransferService();
            _croc.OutputReceived += AppendCrocLog;
            _croc.ProgressChanged += progress => BeginInvoke(new Action(() => UpdateCrocProgress(progress)));
            _croc.StateChanged += (state, message) => BeginInvoke(new Action(() => ApplyCrocState(state, message)));
            _croc.Exited += code => BeginInvoke(new Action(() =>
            {
                UpdateCrocButtons();
                if (code == 0 && _crocProgressBar != null)
                {
                    _crocProgressBar.Style = ProgressBarStyle.Continuous;
                    _crocProgressBar.Value = 100;
                    SetProgressComplete(_crocProgressBar);
                    _crocProgressLabel.Text = "Progreso remoto: 100% - transferencia finalizada.";
                }
                SetStatus(code == 0 ? "Transferencia simple finalizada." : "Transferencia simple terminó con código " + code + ".");
            }));
        }

        private void ApplyVisualStyle(Control control)
        {
            if (control is Form || control is TableLayoutPanel)
            {
                control.BackColor = AppBackground;
            }
            else if (control is Panel)
            {
                control.BackColor = PanelBackground;
            }
            else if (control is GroupBox)
            {
                control.BackColor = PanelBackground;
                control.ForeColor = TextColor;
                control.Padding = new Padding(10);
            }
            else if (control is Label label)
            {
                label.ForeColor = TextColor;
                label.BackColor = Color.Transparent;
            }
            else if (control is Button button)
            {
                StyleButton(button);
            }
            else if (control is TextBox textBox)
            {
                textBox.BorderStyle = BorderStyle.FixedSingle;
                textBox.BackColor = textBox.ReadOnly ? Color.FromArgb(249, 250, 252) : Color.White;
                textBox.ForeColor = TextColor;
            }
            else if (control is ComboBox comboBox)
            {
                comboBox.FlatStyle = FlatStyle.Flat;
                comboBox.BackColor = Color.White;
                comboBox.ForeColor = TextColor;
            }
            else if (control is ListBox listBox)
            {
                listBox.BorderStyle = BorderStyle.FixedSingle;
                listBox.BackColor = Color.White;
                listBox.ForeColor = TextColor;
            }
            else if (control is DataGridView grid)
            {
                StyleGrid(grid);
            }

            foreach (Control child in control.Controls)
            {
                ApplyVisualStyle(child);
            }
        }

        private static void StyleButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = PanelBackground;
            button.ForeColor = TextColor;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
        }

        private void ApplyModeButtonState(Button button, bool selected)
        {
            if (button == null) return;

            StyleButton(button);
            button.BackColor = selected ? PrimarySelectedColor : PanelBackground;
            button.ForeColor = selected ? PrimaryColor : TextColor;
            button.FlatAppearance.BorderColor = selected ? PrimaryColor : BorderColor;
        }

        private void StyleGrid(DataGridView grid)
        {
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.BackgroundColor = Color.White;
            grid.GridColor = BorderColor;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextColor;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(241, 245, 249);
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextColor;
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.ForeColor = TextColor;
            grid.DefaultCellStyle.SelectionBackColor = PrimarySelectedColor;
            grid.DefaultCellStyle.SelectionForeColor = TextColor;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.RowTemplate.Height = 32;
            grid.AllowUserToResizeRows = false;
        }

        private Control BuildModeSelector()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

            _localModeButton = new Button { Dock = DockStyle.Fill, Text = "Local LAN\r\nIP privada o Buscar PC", Font = new Font(Font.FontFamily, 11, FontStyle.Bold) };
            _remoteModeButton = new Button { Dock = DockStyle.Fill, Text = "Remoto sin puertos\r\nCódigo seguro", Font = new Font(Font.FontFamily, 11, FontStyle.Bold) };
            _remoteDirectModeButton = new Button { Dock = DockStyle.Fill, Text = "Remoto directo\r\nIP pública, dominio o VPN", Font = new Font(Font.FontFamily, 11, FontStyle.Bold) };
            _localModeButton.Click += (s, e) => ShowWorkMode(0);
            _remoteModeButton.Click += (s, e) => ShowWorkMode(1);
            _remoteDirectModeButton.Click += (s, e) => ShowWorkMode(2);

            panel.Controls.Add(_localModeButton, 0, 0);
            panel.Controls.Add(_remoteModeButton, 1, 0);
            panel.Controls.Add(_remoteDirectModeButton, 2, 0);
            return panel;
        }

        private void ShowWorkMode(int mode)
        {
            if (_localModePanel == null || _remoteModePanel == null) return;

            bool local = mode == 0;
            bool remoteByCode = mode == 1;
            bool remoteDirect = mode == 2;
            _workMode = mode;

            _localModePanel.Dock = DockStyle.Fill;
            _remoteModePanel.Dock = DockStyle.Fill;
            _localModePanel.Visible = !remoteByCode;
            _remoteModePanel.Visible = remoteByCode;
            ApplyModeButtonState(_localModeButton, local);
            ApplyModeButtonState(_remoteModeButton, remoteByCode);
            ApplyModeButtonState(_remoteDirectModeButton, remoteDirect);
            _directRemoteMode = remoteDirect;
            if (_scopeCombo != null) _scopeCombo.SelectedIndex = remoteDirect ? 1 : 0;
            if (_ipText != null) _ipText.Items.Clear();
            if (_discoverButton != null) _discoverButton.Enabled = local;

            if (remoteByCode)
            {
                StopServer();
                SetStatus("Modo Remoto activo: use código croc para transferir sin abrir puertos.");
                return;
            }

            StartServer();
            ShowLocalRole(true);
            if (local)
            {
                SetStatus("Modo Local activo. La app ya escucha en esta PC; conectá por IP o usá Buscar PC.");
            }
            else
            {
                SetStatus("Modo Remoto directo activo. Conectá por IP pública o dominio; el equipo que recibe debe tener el puerto TCP redirigido.");
            }
        }

        private Control BuildLocalModePanel()
        {
            var group = new GroupBox { Dock = DockStyle.Fill, Text = "Transferencia directa por IP - paneles local/remoto" };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(6) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            group.Controls.Add(layout);

            layout.Controls.Add(BuildLocalRoleSelector(), 0, 0);
            _localConnectionPanel = BuildConnectionPanel();
            _localFilePanel = BuildFilePanel();
            _localProgressPanel = BuildProgressPanel();
            layout.Controls.Add(_localConnectionPanel, 0, 1);
            layout.Controls.Add(_localFilePanel, 0, 2);
            layout.Controls.Add(_localProgressPanel, 0, 3);
            SetLocalRoleContentVisible(true);
            return group;
        }

        private Control BuildLocalRoleSelector()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1 };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _localSendRoleButton = new Button { Dock = DockStyle.Fill, Text = "Conectar o recibir archivos - esta PC escucha automáticamente", Font = new Font(Font.FontFamily, 10, FontStyle.Bold) };
            _localSendRoleButton.Click += (s, e) => ShowLocalRole(true);
            panel.Controls.Add(_localSendRoleButton, 0, 0);
            return panel;
        }

        private void SetLocalRoleContentVisible(bool visible)
        {
            if (_localConnectionPanel != null) _localConnectionPanel.Visible = visible;
            if (_localFilePanel != null) _localFilePanel.Visible = visible;
            if (_localProgressPanel != null) _localProgressPanel.Visible = visible;
        }

        private void ShowLocalRole(bool sending)
        {
            SetLocalRoleContentVisible(true);
            _localSendRoleButton.BackColor = PrimarySelectedColor;
            _modeCombo.SelectedIndex = 1;

            _connectButton.Enabled = true;
            _discoverButton.Enabled = !IsRemoteScope();
            _localFilePanel.Visible = true;
            _localProgressPanel.Visible = true;

            SetStatus(IsRemoteScope()
                ? "Ingrese IP pública o dominio del otro equipo y pulse Conectar."
                : "Ingrese la IP del otro equipo y pulse Conectar, o use Buscar PC.");
        }

        private Control BuildRemoteModePanel()
        {
            var group = new GroupBox { Dock = DockStyle.Fill, Text = "Modo Remoto (Internet) - enviar por código sin configurar router" };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 1, Padding = new Padding(8) };
            group.Controls.Add(layout);
            layout.Controls.Add(BuildSimpleInternetPanel(), 0, 0);
            return group;
        }

        private Control BuildConnectionPanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, RowCount = 1, Padding = new Padding(0, 2, 0, 0) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            _modeCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _modeCombo.Items.AddRange(new object[] { "Servidor", "Cliente" });
            _modeCombo.SelectedIndex = 0;
            _scopeCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _scopeCombo.Items.AddRange(new object[] { "Local LAN", "Remoto Internet" });
            _scopeCombo.SelectedIndex = 0;
            _scopeCombo.SelectedIndexChanged += (s, e) => ApplyScopeText();

            _ipText = new ComboBox { Dock = DockStyle.Fill, Text = "127.0.0.1", DropDownStyle = ComboBoxStyle.DropDown };
            _portText = new TextBox { Dock = DockStyle.Fill, Text = _settings.TcpPort.ToString(), Visible = false };
            _keyText = new TextBox { Dock = DockStyle.Fill, Text = _settings.SharedKey, UseSystemPasswordChar = true, Visible = false };
            _connectButton = new Button { Dock = DockStyle.Fill, Text = "Conectar" };
            _discoverButton = new Button { Dock = DockStyle.Fill, Text = "Buscar PC" };
            _localIpLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

            _connectButton.Click += async (s, e) => await ConnectTargetAsync();
            _discoverButton.Click += async (s, e) => await DiscoverAsync();
            _ipText.KeyDown += async (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                await ConnectTargetAsync();
            };

            panel.Controls.Add(new Label { Text = "Destino", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            panel.Controls.Add(_ipText, 1, 0);
            panel.Controls.Add(_connectButton, 2, 0);
            panel.Controls.Add(_discoverButton, 3, 0);
            panel.Controls.Add(new Label { Text = "Esta PC", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 4, 0);
            panel.SetColumnSpan(_localIpLabel, 3);
            panel.Controls.Add(_localIpLabel, 5, 0);

            UpdateSettingsSummary();
            return panel;
        }

        private Control BuildFilePanel()
        {
            var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0), Margin = new Padding(0) };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            split.Controls.Add(BuildLocalPanel(), 0, 0);
            split.Controls.Add(BuildRemotePanel(), 1, 0);
            return split;
        }

        private Control BuildLocalPanel()
        {
            var panel = BuildBrowserPanel("Local", out _localPathText, out _localGrid);
            _localDrivesButton = new Button { Text = "Unidades", Dock = DockStyle.Fill };
            _browseLocalButton = new Button { Text = "Elegir carpeta", Dock = DockStyle.Fill };
            _localUpButton = new Button { Text = "Subir", Dock = DockStyle.Fill };
            _sendButton = new Button { Text = "Enviar al remoto", Dock = DockStyle.Fill, Enabled = false };
            _localDrivesButton.Click += (s, e) => LoadLocalPath("");
            _browseLocalButton.Click += (s, e) => BrowseLocal();
            _localUpButton.Click += (s, e) => LoadParentLocal();
            _sendButton.Click += async (s, e) => await SendSelectedLocalAsync();
            _localPathText.Click += (s, e) => SelectPathText(_localPathText);
            _localPathText.KeyDown += (s, e) => OpenTypedLocalPath(e);

            var buttons = (TableLayoutPanel)panel.Tag;
            buttons.Controls.Add(_localDrivesButton, 0, 0);
            buttons.Controls.Add(_browseLocalButton, 1, 0);
            buttons.Controls.Add(_localUpButton, 2, 0);
            buttons.Controls.Add(_sendButton, 3, 0);

            _localGrid.AllowDrop = true;
            _localGrid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0) OpenLocalSelection();
            };
            _localGrid.KeyDown += LocalGridKeyDown;
            _localGrid.MouseDown += LocalGridMouseDown;
            _localGrid.MouseMove += LocalGridMouseMove;
            _localGrid.MouseUp += LocalGridMouseUp;
            _localGrid.DragEnter += LocalGridDragEnter;
            _localGrid.DragDrop += async (s, e) => await LocalGridDragDropAsync(e);
            return panel;
        }

        private Control BuildRemotePanel()
        {
            var panel = BuildBrowserPanel("Remoto", out _remotePathText, out _remoteGrid);
            _remoteDrivesButton = new Button { Text = "Unidades", Dock = DockStyle.Fill, Enabled = false };
            _remoteUpButton = new Button { Text = "Subir", Dock = DockStyle.Fill, Enabled = false };
            _remoteBrowseButton = new Button { Text = "Elegir carpeta", Dock = DockStyle.Fill, Enabled = false };
            _remoteDrivesButton.Click += (s, e) =>
            {
                if (_connection != null) _connection.RequestRemoteList("");
            };
            _remoteUpButton.Click += (s, e) =>
            {
                if (_connection != null) _connection.RequestRemoteList(ParentPath(_remoteCurrentPath));
            };
            _remoteBrowseButton.Click += (s, e) => BrowseRemote();
            _remotePathText.Click += (s, e) => SelectPathText(_remotePathText);
            _remotePathText.KeyDown += (s, e) => OpenTypedRemotePath(e);

            var buttons = (TableLayoutPanel)panel.Tag;
            buttons.Controls.Add(_remoteDrivesButton, 0, 0);
            buttons.Controls.Add(_remoteUpButton, 1, 0);
            buttons.Controls.Add(_remoteBrowseButton, 2, 0);

            _remoteGrid.AllowDrop = true;
            _remoteGrid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0) OpenRemoteSelection();
            };
            _remoteGrid.KeyDown += RemoteGridKeyDown;
            _remoteGrid.MouseDown += RemoteGridMouseDown;
            _remoteGrid.MouseMove += RemoteGridMouseMove;
            _remoteGrid.MouseUp += RemoteGridMouseUp;
            _remoteGrid.DragEnter += RemoteGridDragEnter;
            _remoteGrid.DragDrop += async (s, e) => await RemoteGridDragDropAsync(e);
            return panel;
        }

        private Panel BuildBrowserPanel(string title, out TextBox pathText, out DataGridView grid)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(layout);

            layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
            pathText = new TextBox { Dock = DockStyle.Fill };
            layout.Controls.Add(pathText, 0, 1);

            var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            layout.Controls.Add(buttons, 0, 2);

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                RowHeadersVisible = false
            };
            grid.ColumnHeaderMouseClick += GridColumnHeaderMouseClick;
            grid.Columns.Add("Name", "Nombre");
            grid.Columns.Add("Modified", "Modificado");
            grid.Columns.Add("Size", "Tamaño");
            grid.Columns["Name"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns["Modified"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            grid.Columns["Modified"].Width = 136;
            grid.Columns["Size"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            grid.Columns["Size"].Width = 126;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.Programmatic;
            }
            layout.Controls.Add(grid, 0, 3);

            panel.Tag = buttons;
            return panel;
        }

        private DataGridView BuildEntryGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                RowHeadersVisible = false
            };
            grid.ColumnHeaderMouseClick += GridColumnHeaderMouseClick;
            grid.Columns.Add("Name", "Nombre");
            grid.Columns.Add("Modified", "Modificado");
            grid.Columns.Add("Size", "Tamaño");
            grid.Columns["Name"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns["Modified"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            grid.Columns["Modified"].Width = 136;
            grid.Columns["Size"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            grid.Columns["Size"].Width = 126;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.Programmatic;
            }
            return grid;
        }

        private Control BuildProgressPanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            _progressBar = new ProgressBar { Dock = DockStyle.Fill };
            PrepareProgressBar(_progressBar);
            _progressLabel = new Label { Dock = DockStyle.Fill, Text = "Total: sin transferencia.", TextAlign = ContentAlignment.MiddleLeft };
            _fileProgressBar = new ProgressBar { Dock = DockStyle.Fill };
            PrepareProgressBar(_fileProgressBar);
            _fileProgressLabel = new Label { Dock = DockStyle.Fill, Text = "Archivo: sin transferencia.", TextAlign = ContentAlignment.MiddleLeft };
            panel.Controls.Add(_fileProgressBar, 0, 0);
            panel.Controls.Add(_fileProgressLabel, 0, 1);
            panel.Controls.Add(_progressBar, 0, 2);
            panel.Controls.Add(_progressLabel, 0, 3);
            return panel;
        }

        private Control BuildSimpleInternetPanel()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(2) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));

            _crocCodeText = new TextBox { Dock = DockStyle.Fill };
            _crocReceiveCodeText = new TextBox { Dock = DockStyle.Fill };
            _crocPathText = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Text = "Sin selección para enviar." };
            _crocReceiveDestinationText = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
            _crocLogText = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical };
            _crocStateLabel = new Label { Dock = DockStyle.Fill, Text = "Enlace: sin preparar", TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) };
            _crocProgressBar = new ProgressBar { Dock = DockStyle.Fill };
            PrepareProgressBar(_crocProgressBar);
            _crocProgressLabel = new Label { Dock = DockStyle.Fill, Text = "Progreso remoto: sin transferencia.", TextAlign = ContentAlignment.MiddleLeft };
            _crocGenerateCodeButton = new Button { Dock = DockStyle.Fill, Text = "Nuevo código" };
            _crocPickFileButton = new Button { Dock = DockStyle.Fill, Text = "Elegir archivo" };
            _crocPickFolderButton = new Button { Dock = DockStyle.Fill, Text = "Elegir carpeta" };
            _crocLocalDrivesButton = new Button { Dock = DockStyle.Fill, Text = "Unidades" };
            _crocLocalBrowseButton = new Button { Dock = DockStyle.Fill, Text = "Elegir carpeta" };
            _crocLocalUpButton = new Button { Dock = DockStyle.Fill, Text = "Subir" };
            _crocUseSelectionButton = new Button { Dock = DockStyle.Fill, Text = "Usar selección" };
            _crocSendRoleButton = new Button { Dock = DockStyle.Fill, Text = "Enviar" };
            _crocReceiveRoleButton = new Button { Dock = DockStyle.Fill, Text = "Recibir" };
            _crocReceiveBrowseButton = new Button { Dock = DockStyle.Fill, Text = "Elegir destino" };
            _crocSendButton = new Button { Dock = DockStyle.Fill, Text = "Enviar", Enabled = false };
            _crocReceiveButton = new Button { Dock = DockStyle.Fill, Text = "Esperar enlace", Enabled = false };
            _crocCancelButton = new Button { Dock = DockStyle.Fill, Text = "Cancelar", Enabled = false };

            _crocSendRoleButton.Click += (s, e) => SelectCrocRole(1);
            _crocReceiveRoleButton.Click += (s, e) => SelectCrocRole(2);
            _crocCodeText.TextChanged += (s, e) => UpdateCrocButtons();
            _crocReceiveCodeText.TextChanged += (s, e) => UpdateCrocButtons();
            _crocGenerateCodeButton.Click += (s, e) =>
            {
                _crocCodeText.Text = CrocTransferService.GenerateCode();
                ApplyCrocState(CrocSessionState.Idle, "Código de envío listo. Compartilo con la otra PC.");
            };
            _crocPickFileButton.Click += (s, e) => PickCrocFiles();
            _crocPickFolderButton.Click += (s, e) => PickCrocFolder();
            _crocLocalDrivesButton.Click += (s, e) => LoadCrocLocalPath("");
            _crocLocalBrowseButton.Click += (s, e) => BrowseCrocLocal();
            _crocLocalUpButton.Click += (s, e) => LoadCrocLocalPath(ParentPath(_crocLocalPathText.Text == "Este equipo" ? "" : _crocLocalPathText.Text));
            _crocUseSelectionButton.Click += (s, e) => UseCrocGridSelection();
            _crocSendButton.Click += async (s, e) => await SendWithCrocAsync();
            _crocReceiveButton.Click += async (s, e) => await ReceiveWithCrocAsync();
            _crocReceiveBrowseButton.Click += (s, e) => BrowseCrocReceiveFolder();
            _crocCancelButton.Click += (s, e) => _croc.Cancel();

            layout.Controls.Add(BuildCrocRoleSelector(), 0, 0);
            layout.Controls.Add(BuildCrocRoleContentPanel(), 0, 1);
            layout.Controls.Add(_crocStateLabel, 0, 2);
            layout.Controls.Add(BuildCrocProgressPanel(), 0, 3);
            layout.Controls.Add(_crocLogText, 0, 4);
            SelectCrocRole(0);
            UpdateCrocReceiveDestinationText();
            UpdateCrocButtons();
            return layout;
        }

        private Control BuildCrocRoleSelector()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Padding = new Padding(4, 2, 4, 2) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            panel.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Cambiar rol", TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
            panel.Controls.Add(_crocSendRoleButton, 1, 0);
            panel.Controls.Add(_crocReceiveRoleButton, 2, 0);
            panel.Controls.Add(_crocCancelButton, 4, 0);
            return panel;
        }

        private Control BuildCrocRoleContentPanel()
        {
            var host = new Panel { Dock = DockStyle.Fill };
            _crocRolePromptPanel = BuildCrocRolePromptPanel();
            _crocSendModePanel = BuildCrocSendModePanel();
            _crocReceiveModePanel = BuildCrocReceiveModePanel();
            host.Controls.Add(_crocRolePromptPanel);
            host.Controls.Add(_crocSendModePanel);
            host.Controls.Add(_crocReceiveModePanel);
            return host;
        }

        private Control BuildCrocRolePromptPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            var label = new Label { Dock = DockStyle.Top, Height = 28, Text = "Elegí Enviar archivos o Recibir archivos para iniciar.", TextAlign = ContentAlignment.MiddleLeft, ForeColor = MutedTextColor };
            panel.Controls.Add(label);
            return panel;
        }

        private Control BuildCrocSendModePanel()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(4) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(BuildCrocSendToolbar(), 0, 0);
            var group = new GroupBox { Dock = DockStyle.Fill, Text = "Local - archivos para enviar" };
            _crocSendPanel = BuildCrocSendPanel();
            group.Controls.Add(_crocSendPanel);
            layout.Controls.Add(group, 0, 1);
            return layout;
        }

        private Control BuildCrocReceiveModePanel()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(4) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(BuildCrocReceiveCodePanel(), 0, 0);
            layout.Controls.Add(BuildCrocReceiveDestinationPanel(), 1, 0);
            return layout;
        }

        private void SelectCrocRole(int role)
        {
            if (_croc != null && _croc.IsRunning) return;

            _crocRole = role;
            if (_crocRolePromptPanel != null) _crocRolePromptPanel.Visible = role == 0;
            if (_crocSendModePanel != null) _crocSendModePanel.Visible = role == 1;
            if (_crocReceiveModePanel != null) _crocReceiveModePanel.Visible = role == 2;
            if (_crocSendModePanel != null) _crocSendModePanel.Dock = DockStyle.Fill;
            if (_crocReceiveModePanel != null) _crocReceiveModePanel.Dock = DockStyle.Fill;
            if (_crocRolePromptPanel != null) _crocRolePromptPanel.Dock = DockStyle.Fill;

            ApplyModeButtonState(_crocSendRoleButton, role == 1);
            ApplyModeButtonState(_crocReceiveRoleButton, role == 2);

            if (role == 1)
            {
                ApplyCrocState(CrocSessionState.Idle, "Modo envío listo.");
                SetStatus("Remoto sin puertos: rol Enviar activo. Podés cambiar a Recibir desde la barra superior.");
            }
            else if (role == 2)
            {
                UpdateCrocReceiveDestinationText();
                ApplyCrocState(CrocSessionState.Idle, "Modo recepción listo.");
                SetStatus("Remoto sin puertos: rol Recibir activo. Podés cambiar a Enviar desde la barra superior.");
            }
            else
            {
                ApplyCrocState(CrocSessionState.Idle, "Elegí rol para iniciar una transferencia sin puertos.");
                SetStatus("Remoto sin puertos: elegí Enviar o Recibir.");
            }
            UpdateCrocButtons();
        }

        private Control BuildCrocSendToolbar()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            panel.Controls.Add(new Label { Text = "Enviar", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
            panel.Controls.Add(new Label { Text = "Código corto", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
            panel.Controls.Add(_crocCodeText, 2, 0);
            panel.Controls.Add(_crocGenerateCodeButton, 3, 0);
            panel.Controls.Add(_crocSendButton, 4, 0);
            return panel;
        }

        private Control BuildCrocReceiveCodePanel()
        {
            var group = new GroupBox { Dock = DockStyle.Fill, Text = "Código de recepción" };
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 3, Padding = new Padding(8), Height = 112 };
            group.Controls.Add(layout);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.Controls.Add(new Label { Text = "Código recibido", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            layout.Controls.Add(_crocReceiveCodeText, 0, 1);
            layout.Controls.Add(_crocReceiveButton, 0, 2);
            return group;
        }

        private Control BuildCrocReceiveDestinationPanel()
        {
            var group = new GroupBox { Dock = DockStyle.Fill, Text = "Destino de recepción" };
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 3, Padding = new Padding(8), Height = 112 };
            group.Controls.Add(layout);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.Controls.Add(new Label { Text = "Guardar en", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            layout.Controls.Add(_crocReceiveDestinationText, 0, 1);
            layout.Controls.Add(_crocReceiveBrowseButton, 0, 2);
            return group;
        }

        private Control BuildCrocProgressPanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            panel.Controls.Add(_crocProgressBar, 0, 0);
            panel.Controls.Add(_crocProgressLabel, 0, 1);
            return panel;
        }

        private Control BuildCrocSendPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            panel.Controls.Add(layout);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var selectionRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            selectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            selectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            selectionRow.Controls.Add(new Label { Text = "Selección envío", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            _crocPathText.TextAlign = HorizontalAlignment.Left;
            selectionRow.Controls.Add(_crocPathText, 1, 0);
            layout.Controls.Add(selectionRow, 0, 0);

            var buttonRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1 };
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            buttonRow.Controls.Add(_crocLocalPathText = new TextBox { Dock = DockStyle.Fill }, 0, 0);
            buttonRow.Controls.Add(_crocLocalDrivesButton, 1, 0);
            buttonRow.Controls.Add(_crocLocalBrowseButton, 2, 0);
            buttonRow.Controls.Add(_crocLocalUpButton, 3, 0);
            buttonRow.Controls.Add(_crocUseSelectionButton, 4, 0);
            buttonRow.Controls.Add(_crocPickFileButton, 5, 0);
            buttonRow.Controls.Add(_crocPickFolderButton, 6, 0);
            layout.Controls.Add(buttonRow, 0, 1);

            _crocLocalGrid = BuildEntryGrid();
            _crocLocalGrid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0) OpenCrocLocalSelection();
            };
            _crocLocalGrid.KeyDown += CrocLocalGridKeyDown;
            _crocLocalGrid.SelectionChanged += (s, e) => ApplyCrocGridSelection(false);
            layout.Controls.Add(_crocLocalGrid, 0, 2);
            return panel;
        }

        private void StartServer()
        {
            if (_server != null) return;
            int port;
            if (!TryReadPort(out port)) return;

            try
            {
                _server = new TransferServer(_keyText.Text, port);
                _server.StatusChanged += SetStatus;
                _server.ClientAccepted += connection => AttachConnection(connection);
                _server.Start();

                _discovery = new DiscoveryService(Environment.MachineName, port);
                _discovery.StartResponder();
                string localIps = GetLocalIpSummary();
                if (IsRemoteScope())
                {
                    SetStatus("Servidor remoto activo en " + localIps + ":" + port + ". Abrí/redirigí el puerto TCP hacia este equipo en el router.");
                }
                else
                {
                    SetStatus("Servidor LAN activo. Desde la otra PC conectá a " + localIps + ":" + port + ".");
                }
            }
            catch (System.Net.Sockets.SocketException)
            {
                if (_discovery != null) _discovery.Dispose();
                if (_server != null) _server.Dispose();
                _discovery = null;
                _server = null;
                SetStatus("No se pudo escuchar en el puerto " + port + ": ya está en uso. Cerrá otra instancia de File Sender o cambiá el puerto en Configuración. Igual podés conectar como cliente.");
            }
            catch (Exception ex)
            {
                if (_discovery != null) _discovery.Dispose();
                if (_server != null) _server.Dispose();
                _discovery = null;
                _server = null;
                SetStatus("No se pudo iniciar el modo local: " + ex.Message);
            }
        }

        private void StopServer()
        {
            if (_discovery != null)
            {
                _discovery.Dispose();
                _discovery = null;
            }
            if (_server != null)
            {
                _server.Dispose();
                _server = null;
            }
        }

        private async Task ConnectToIpAsync(string ip)
        {
            int port;
            if (!TryReadPort(out port)) return;
            await ConnectToIpAsync(ip, port);
        }

        private async Task ConnectToIpAsync(string ip, int port)
        {
            try
            {
                SetStatus("Conectando a " + ip + ":" + port + "...");
                PeerConnection connection = await PeerConnection.ConnectAsync(ip, port, _keyText.Text);
                AttachConnection(connection);
            }
            catch (Exception ex)
            {
                SetStatus("No se pudo conectar: " + ex.Message);
            }
        }

        private async Task DiscoverAsync()
        {
            if (IsRemoteScope())
            {
                SetStatus("La búsqueda automática usa broadcast y solo funciona en LAN. Para Internet, ingresá IP pública o dominio.");
                return;
            }

            string typedTarget = _ipText.Text;
            _ipText.Items.Clear();
            SetStatus("Buscando servidores en la red local...");
            List<DiscoveredServer> servers = await DiscoveryService.DiscoverAsync(1600);
            foreach (DiscoveredServer server in servers)
            {
                _ipText.Items.Add(server);
            }
            if (servers.Count == 1)
            {
                _ipText.SelectedItem = servers[0];
                SetStatus("PC encontrada: " + servers[0] + ". Pulsá Conectar o Enter.");
                return;
            }

            if (servers.Count > 1)
            {
                _ipText.Text = "";
                _ipText.DroppedDown = true;
                SetStatus("PCs encontradas: " + servers.Count + ". Elegí una del selector o escribí una IP.");
                return;
            }

            _ipText.Text = typedTarget;
            SetStatus("No se encontraron PCs. Verificá que la otra PC tenga File Sender abierto en Modo Local y que Firewall permita la app.");
        }

        private async Task ConnectSelectedServerAsync()
        {
            var selected = _ipText.SelectedItem as DiscoveredServer;
            if (selected == null)
            {
                await ConnectTargetAsync();
                return;
            }

            _ipText.Text = selected.Address;
            await ConnectToIpAsync(selected.Address, selected.Port);
        }

        private async Task ConnectTargetAsync()
        {
            var selected = _ipText.SelectedItem as DiscoveredServer;
            if (selected != null && string.Equals(_ipText.Text, selected.ToString(), StringComparison.Ordinal))
            {
                await ConnectSelectedServerAsync();
                return;
            }

            await ConnectToIpAsync(_ipText.Text.Trim());
        }

        private void AttachConnection(PeerConnection connection)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<PeerConnection>(AttachConnection), connection);
                return;
            }

            if (_connection != null) _connection.Dispose();
            _connection = connection;
            _connection.StatusChanged += SetStatus;
            _connection.RemoteListReceived += response => BeginInvoke(new Action(() => HandleRemoteList(response)));
            _connection.TransferDecisionRequested += RequestTransferDecision;
            _connection.ProgressChanged += progress => BeginInvoke(new Action(() => UpdateProgress(progress)));
            _connection.Connected += () => BeginInvoke(new Action(() =>
            {
                ShowExchangePanels(true);
                _connection.RequestRemoteList("");
                SetStatus("Conexión activa. Ambos equipos pueden enviar archivos o carpetas.");
            }));
            _connection.Disconnected += () => BeginInvoke(new Action(() =>
            {
                ShowExchangePanels(false);
            }));
        }

        private void ShowExchangePanels(bool connected)
        {
            ShowWorkMode(_directRemoteMode ? 2 : 0);
            SetLocalRoleContentVisible(true);
            if (_modeCombo != null) _modeCombo.SelectedIndex = connected ? 1 : _modeCombo.SelectedIndex;
            if (_localSendRoleButton != null) _localSendRoleButton.BackColor = connected ? Color.LightSteelBlue : SystemColors.Control;
            if (_sendButton != null) _sendButton.Enabled = connected;
            if (_remoteDrivesButton != null) _remoteDrivesButton.Enabled = connected;
            if (_remoteUpButton != null) _remoteUpButton.Enabled = connected;
            if (_remoteBrowseButton != null) _remoteBrowseButton.Enabled = connected;
        }

        private bool IsRemoteScope()
        {
            return _scopeCombo != null && _scopeCombo.SelectedIndex == 1;
        }

        private void OpenSettings()
        {
            using (var dialog = new SettingsDialog(_settings))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _settings = dialog.Settings;
                _settings.Save();
                _portText.Text = _settings.TcpPort.ToString();
                _keyText.Text = _settings.SharedKey;
                if (Directory.Exists(_settings.LocalStartFolder))
                {
                    LoadLocalPath(_settings.LocalStartFolder);
                }
                UpdateSettingsSummary();
                SetStatus("Configuración guardada.");
            }
        }

        private void UpdateSettingsSummary()
        {
            if (_localIpLabel != null)
            {
                _localIpLabel.Text = "IP de esta PC: " + GetLocalIpSummary();
            }
        }

        private static string GetLocalIpSummary()
        {
            List<string> addresses = GetLocalIPv4Addresses();
            return addresses.Count == 0 ? "sin IP LAN" : string.Join(", ", addresses.ToArray());
        }

        private static List<string> GetLocalIPv4Addresses()
        {
            var addresses = new List<string>();
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                IPInterfaceProperties properties = adapter.GetIPProperties();
                foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    IPAddress address = unicast.Address;
                    if (IPAddress.IsLoopback(address)) continue;
                    string text = address.ToString();
                    if (!addresses.Contains(text)) addresses.Add(text);
                }
            }
            return addresses;
        }

        private void ApplyScopeText()
        {
            if (IsRemoteScope())
            {
                _ipText.Text = "";
                SetStatus("Modo remoto: conectá usando IP pública o dominio. El servidor debe tener el puerto TCP redirigido.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_ipText.Text)) _ipText.Text = "127.0.0.1";
                SetStatus("Modo local: podés usar búsqueda LAN o IP privada del otro equipo.");
            }
        }

        private bool TryReadPort(out int port)
        {
            port = _settings != null ? _settings.TcpPort : FileTransferProtocol.DefaultTcpPort;
            if (port < 1 || port > 65535)
            {
                SetStatus("Puerto inválido en Configuración. Usá un valor entre 1 y 65535.");
                return false;
            }
            return true;
        }

        private void LoadLocalPath(string path)
        {
            try
            {
                _localCurrentPath = path;
                if (_settings != null && !string.IsNullOrEmpty(path))
                {
                    _settings.LocalStartFolder = path;
                    _settings.Save();
                }
                _localPathText.Text = string.IsNullOrEmpty(path) ? "Este equipo" : path;
                _localGrid.Rows.Clear();

                if (string.IsNullOrEmpty(path))
                {
                    foreach (DriveInfo drive in DriveInfo.GetDrives())
                    {
                        string label = drive.IsReady ? drive.VolumeLabel : "";
                        string name = string.IsNullOrWhiteSpace(label)
                            ? drive.Name
                            : drive.Name + " " + label;
                        AddRow(_localGrid, new FileSystemEntry { Name = name, FullPath = drive.Name, IsDirectory = true, LastModifiedUtc = DateTime.MinValue });
                    }
                    return;
                }

                DirectoryInfo directory = new DirectoryInfo(path);
                if (directory.Parent != null) AddRow(_localGrid, new FileSystemEntry { Name = "..", FullPath = directory.Parent.FullName, IsDirectory = true, LastModifiedUtc = directory.Parent.LastWriteTimeUtc });
                else AddRow(_localGrid, new FileSystemEntry { Name = "..", FullPath = "", IsDirectory = true, LastModifiedUtc = DateTime.MinValue });

                foreach (DirectoryInfo child in directory.GetDirectories())
                {
                    AddRow(_localGrid, new FileSystemEntry { Name = child.Name, FullPath = child.FullName, IsDirectory = true, LastModifiedUtc = child.LastWriteTimeUtc });
                }
                foreach (FileInfo file in directory.GetFiles())
                {
                    AddRow(_localGrid, new FileSystemEntry { Name = file.Name, FullPath = file.FullName, IsDirectory = false, Size = file.Length, LastModifiedUtc = file.LastWriteTimeUtc });
                }
            }
            catch (Exception ex)
            {
                SetStatus("No se pudo abrir carpeta local: " + ex.Message);
            }
        }

        private void LoadCrocLocalPath(string path)
        {
            if (_crocLocalGrid == null || _crocLocalPathText == null) return;

            try
            {
                _loadingCrocLocalPath = true;
                if (_crocSelectionFromGrid) ClearCrocPreparedSelection();
                _crocLocalPathText.Text = string.IsNullOrEmpty(path) ? "Este equipo" : path;
                UpdateCrocReceiveDestinationText();
                _crocLocalGrid.Rows.Clear();

                if (string.IsNullOrEmpty(path))
                {
                    foreach (DriveInfo drive in DriveInfo.GetDrives())
                    {
                        string label = drive.IsReady ? drive.VolumeLabel : "";
                        string name = string.IsNullOrWhiteSpace(label)
                            ? drive.Name
                            : drive.Name + " " + label;
                        AddRow(_crocLocalGrid, new FileSystemEntry { Name = name, FullPath = drive.Name, IsDirectory = true, LastModifiedUtc = DateTime.MinValue });
                    }
                    return;
                }

                DirectoryInfo directory = new DirectoryInfo(path);
                if (directory.Parent != null) AddRow(_crocLocalGrid, new FileSystemEntry { Name = "..", FullPath = directory.Parent.FullName, IsDirectory = true, LastModifiedUtc = directory.Parent.LastWriteTimeUtc });
                else AddRow(_crocLocalGrid, new FileSystemEntry { Name = "..", FullPath = "", IsDirectory = true, LastModifiedUtc = DateTime.MinValue });

                foreach (DirectoryInfo child in directory.GetDirectories())
                {
                    AddRow(_crocLocalGrid, new FileSystemEntry { Name = child.Name, FullPath = child.FullName, IsDirectory = true, LastModifiedUtc = child.LastWriteTimeUtc });
                }
                foreach (FileInfo file in directory.GetFiles())
                {
                    AddRow(_crocLocalGrid, new FileSystemEntry { Name = file.Name, FullPath = file.FullName, IsDirectory = false, Size = file.Length, LastModifiedUtc = file.LastWriteTimeUtc });
                }
            }
            catch (Exception ex)
            {
                SetStatus("No se pudo abrir carpeta para envío remoto: " + ex.Message);
            }
            finally
            {
                if (_crocLocalGrid != null) _crocLocalGrid.ClearSelection();
                _loadingCrocLocalPath = false;
                UpdateCrocButtons();
            }
        }

        private void ApplyRemoteList(ListResponse response)
        {
            _remoteCurrentPath = response.Path;
            _remotePathText.Text = string.IsNullOrEmpty(response.Path) ? "Equipo remoto" : response.Path;
            _remoteGrid.Rows.Clear();
            foreach (FileSystemEntry entry in response.Entries)
            {
                AddRow(_remoteGrid, entry);
            }
        }

        private void HandleRemoteList(ListResponse response)
        {
            if (_remoteFolderPicker != null && !_remoteFolderPicker.IsDisposed && _remoteFolderPicker.HandleListResponse(response))
            {
                return;
            }

            ApplyRemoteList(response);
        }

        private static void SelectPathText(TextBox textBox)
        {
            if (textBox == null) return;
            textBox.SelectAll();
        }

        private void OpenTypedLocalPath(KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;

            e.SuppressKeyPress = true;
            string path = NormalizeTypedPath(_localPathText.Text, "Este equipo");
            LoadLocalPath(path);
            SetStatus(string.IsNullOrWhiteSpace(path)
                ? "Mostrando unidades locales."
                : "Abriendo carpeta local: " + path);
        }

        private void OpenTypedRemotePath(KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;

            e.SuppressKeyPress = true;
            if (_connection == null || !_connection.IsConnected)
            {
                SetStatus("Primero conectá con otro equipo.");
                return;
            }

            string path = NormalizeTypedPath(_remotePathText.Text, "Equipo remoto");
            _connection.RequestRemoteList(path);
            SetStatus(string.IsNullOrWhiteSpace(path)
                ? "Mostrando unidades del equipo remoto."
                : "Abriendo carpeta remota: " + path);
        }

        private static string NormalizeTypedPath(string text, string placeholder)
        {
            string path = (text ?? "").Trim();
            return string.Equals(path, placeholder, StringComparison.OrdinalIgnoreCase) ? "" : path;
        }

        private static void AddRow(DataGridView grid, FileSystemEntry entry)
        {
            int row = grid.Rows.Add(entry.Name, entry.DisplayModified, entry.DisplaySize);
            DataGridViewRow gridRow = grid.Rows[row];
            gridRow.Tag = entry;
            ApplyEntryRowStyle(gridRow, entry);
        }

        private static void GridColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            var grid = sender as DataGridView;
            if (grid == null || e.ColumnIndex < 0) return;

            DataGridViewColumn column = grid.Columns[e.ColumnIndex];
            ListSortDirection direction = column.HeaderCell.SortGlyphDirection == SortOrder.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            foreach (DataGridViewColumn item in grid.Columns)
            {
                item.HeaderCell.SortGlyphDirection = SortOrder.None;
            }

            grid.Sort(new EntryRowComparer(column.Name, direction));
            column.HeaderCell.SortGlyphDirection = direction == ListSortDirection.Ascending
                ? SortOrder.Ascending
                : SortOrder.Descending;
        }

        private static void ApplyEntryRowStyle(DataGridViewRow row, FileSystemEntry entry)
        {
            bool isParent = entry != null && entry.Name == "..";
            bool isDirectory = entry != null && entry.IsDirectory;
            Color backColor = isParent ? ParentRowColor : isDirectory ? FolderRowColor : FileRowColor;
            Color foreColor = isParent ? ParentTextColor : isDirectory ? FolderTextColor : TextColor;

            row.DefaultCellStyle.BackColor = backColor;
            row.DefaultCellStyle.ForeColor = foreColor;
            row.DefaultCellStyle.SelectionBackColor = PrimarySelectedColor;
            row.DefaultCellStyle.SelectionForeColor = foreColor;
            row.DefaultCellStyle.Font = isDirectory
                ? new Font(row.DataGridView.Font, FontStyle.Bold)
                : row.DataGridView.Font;
            row.Cells["Size"].Style.ForeColor = isDirectory ? MutedTextColor : Color.FromArgb(71, 85, 105);
            row.Cells["Modified"].Style.ForeColor = MutedTextColor;
        }

        private sealed class EntryRowComparer : IComparer
        {
            private readonly string _columnName;
            private readonly ListSortDirection _direction;

            public EntryRowComparer(string columnName, ListSortDirection direction)
            {
                _columnName = columnName;
                _direction = direction;
            }

            public int Compare(object x, object y)
            {
                var leftRow = x as DataGridViewRow;
                var rightRow = y as DataGridViewRow;
                var left = leftRow == null ? null : leftRow.Tag as FileSystemEntry;
                var right = rightRow == null ? null : rightRow.Tag as FileSystemEntry;

                bool leftParent = left != null && left.Name == "..";
                bool rightParent = right != null && right.Name == "..";
                if (leftParent != rightParent) return leftParent ? -1 : 1;

                int result = CompareEntries(left, right, _columnName);
                if (_direction == ListSortDirection.Descending) result = -result;
                return result;
            }

            private static int CompareEntries(FileSystemEntry left, FileSystemEntry right, string columnName)
            {
                if (ReferenceEquals(left, right)) return 0;
                if (left == null) return -1;
                if (right == null) return 1;

                int result;
                if (columnName == "Size")
                {
                    result = left.Size.CompareTo(right.Size);
                }
                else if (columnName == "Modified")
                {
                    result = left.LastModifiedUtc.CompareTo(right.LastModifiedUtc);
                }
                else
                {
                    result = string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
                }

                if (result != 0) return result;
                return string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
            }
        }

        private void BrowseLocal()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = _localCurrentPath;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    LoadLocalPath(dialog.SelectedPath);
                }
            }
        }

        private void BrowseRemote()
        {
            if (_connection == null || !_connection.IsConnected)
            {
                SetStatus("Primero conectá con otro equipo.");
                return;
            }

            string suggestedPath = _remoteCurrentPath;
            FileSystemEntry selected = SelectedEntry(_remoteGrid);
            if (selected != null && selected.IsDirectory && selected.Name != "..")
            {
                suggestedPath = selected.FullPath;
            }

            using (var dialog = new RemoteFolderPickerDialog(suggestedPath, path => _connection.RequestRemoteList(path)))
            {
                dialog.Font = Font;
                _remoteFolderPicker = dialog;
                dialog.RequestPath("");

                DialogResult result = dialog.ShowDialog(this);
                _remoteFolderPicker = null;

                if (result != DialogResult.OK) return;

                string cleanPath = (dialog.SelectedPath ?? "").Trim();
                _connection.RequestRemoteList(cleanPath);
                SetStatus(string.IsNullOrWhiteSpace(cleanPath)
                ? "Abriendo unidades del equipo remoto..."
                : "Abriendo carpeta remota: " + cleanPath);
            }
        }

        private void PickCrocFiles()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Elegir archivos para enviar";
                dialog.Multiselect = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _crocSelectedPaths.Clear();
                _crocSelectedPaths.AddRange(dialog.FileNames);
                _crocSelectionFromGrid = false;
                _crocPathText.Text = string.Join("; ", _crocSelectedPaths.ToArray());
                if (string.IsNullOrWhiteSpace(_crocCodeText.Text)) _crocCodeText.Text = CrocTransferService.GenerateCode();
                ApplyCrocState(CrocSessionState.Idle, "Archivo listo para enviar. Usá o generá un código propio.");
                UpdateCrocButtons();
            }
        }

        private void PickCrocFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = _localCurrentPath;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _crocSelectedPaths.Clear();
                _crocSelectedPaths.Add(dialog.SelectedPath);
                _crocSelectionFromGrid = false;
                _crocPathText.Text = dialog.SelectedPath;
                if (string.IsNullOrWhiteSpace(_crocCodeText.Text)) _crocCodeText.Text = CrocTransferService.GenerateCode();
                ApplyCrocState(CrocSessionState.Idle, "Carpeta lista para enviar. Usá o generá un código propio.");
                UpdateCrocButtons();
            }
        }

        private void BrowseCrocLocal()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                string current = _crocLocalPathText == null ? "" : NormalizeTypedPath(_crocLocalPathText.Text, "Este equipo");
                if (Directory.Exists(current)) dialog.SelectedPath = current;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                LoadCrocLocalPath(dialog.SelectedPath);
            }
        }

        private void BrowseCrocReceiveFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                string current = CurrentCrocReceiveFolder();
                if (Directory.Exists(current)) dialog.SelectedPath = current;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _crocReceiveFolder = dialog.SelectedPath;
                UpdateCrocReceiveDestinationText();
            }
        }

        private void OpenCrocLocalSelection()
        {
            FileSystemEntry entry = SelectedEntry(_crocLocalGrid);
            if (entry != null && entry.IsDirectory) LoadCrocLocalPath(entry.FullPath);
        }

        private void CrocLocalGridKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                OpenCrocLocalSelection();
                return;
            }

            if (e.KeyCode == Keys.Back)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                string current = _crocLocalPathText == null ? "" : NormalizeTypedPath(_crocLocalPathText.Text, "Este equipo");
                LoadCrocLocalPath(ParentPath(current));
            }
        }

        private void UseCrocGridSelection()
        {
            ApplyCrocGridSelection(true);
        }

        private void ApplyCrocGridSelection(bool showEmptyMessage)
        {
            if (_loadingCrocLocalPath || _crocRole != 1) return;

            List<string> paths = SelectedCrocLocalPaths();
            if (paths.Count == 0)
            {
                ClearCrocPreparedSelection();
                if (showEmptyMessage) SetStatus("Seleccioná archivos o carpetas para enviar.");
                return;
            }

            _crocSelectedPaths.Clear();
            _crocSelectedPaths.AddRange(paths);
            _crocSelectionFromGrid = true;
            _crocPathText.Text = BuildCrocSelectionText(paths);
            if (string.IsNullOrWhiteSpace(_crocCodeText.Text)) _crocCodeText.Text = CrocTransferService.GenerateCode();
            ApplyCrocState(CrocSessionState.Idle, "Envío preparado. Compartí el código con la otra PC.");
            UpdateCrocButtons();
        }

        private void ClearCrocPreparedSelection()
        {
            _crocSelectedPaths.Clear();
            _crocSelectionFromGrid = false;
            if (_crocPathText != null) _crocPathText.Text = "Sin selección para enviar.";
            UpdateCrocButtons();
        }

        private List<string> SelectedCrocLocalPaths()
        {
            var paths = new List<string>();
            if (_crocLocalGrid == null) return paths;

            foreach (DataGridViewRow row in _crocLocalGrid.SelectedRows)
            {
                var entry = row.Tag as FileSystemEntry;
                if (entry != null && entry.Name != "..") paths.Add(entry.FullPath);
            }
            return paths;
        }

        private static string BuildCrocSelectionText(List<string> paths)
        {
            if (paths == null || paths.Count == 0) return "";
            if (paths.Count == 1) return paths[0];
            return paths[0] + " y " + (paths.Count - 1) + " más";
        }

        private async Task SendWithCrocAsync()
        {
            if (_crocSelectedPaths.Count == 0)
            {
                SetStatus("Elegí uno o más archivos o una carpeta para enviar.");
                return;
            }
            string code = CrocTransferService.NormalizeCode(_crocCodeText.Text);
            if (code.Length < 6)
            {
                SetStatus("El código corto debe tener 6 caracteres.");
                return;
            }

            try
            {
                SetCrocRunning(true);
                ResetCrocProgress(true);
                _crocLogText.Clear();
                ApplyCrocState(CrocSessionState.WaitingForPeer, "Envío activo. Esperando que la otra PC ingrese el código.");
                AppendCrocLog("Código de envío: " + CrocTransferService.FormatCodeForDisplay(code));
                await _croc.SendAsync(_crocSelectedPaths, code);
            }
            catch (Exception ex)
            {
                UpdateCrocButtons();
                SetStatus("No se pudo iniciar envío simple: " + ex.Message);
            }
        }

        private async Task ReceiveWithCrocAsync()
        {
            string code = CrocTransferService.NormalizeCode(_crocReceiveCodeText.Text);
            if (code.Length < 6)
            {
                SetStatus("Ingresá el código recibido.");
                return;
            }

            try
            {
                SetCrocRunning(true);
                ResetCrocProgress(true);
                _crocLogText.Clear();
                ApplyCrocState(CrocSessionState.WaitingForPeer, "Validando enlace. Esperando que la otra PC use el mismo código.");
                string receiveFolder = CurrentCrocReceiveFolder();
                AppendCrocLog("Destino de recepción: " + receiveFolder);
                AppendCrocLog("Esperando enlace con la otra PC...");
                await _croc.ReceiveAsync(code, receiveFolder);
                LoadCrocLocalPath(receiveFolder);
            }
            catch (Exception ex)
            {
                UpdateCrocButtons();
                SetStatus("No se pudo iniciar recepción simple: " + ex.Message);
            }
        }

        private void SetCrocRunning(bool running)
        {
            if (_crocSendRoleButton != null) _crocSendRoleButton.Enabled = !running;
            if (_crocReceiveRoleButton != null) _crocReceiveRoleButton.Enabled = !running;
            _crocGenerateCodeButton.Enabled = !running && _crocRole == 1;
            _crocPickFileButton.Enabled = !running && _crocRole == 1;
            _crocPickFolderButton.Enabled = !running && _crocRole == 1;
            if (_crocReceiveBrowseButton != null) _crocReceiveBrowseButton.Enabled = !running && _crocRole == 2;
            if (_crocLocalDrivesButton != null) _crocLocalDrivesButton.Enabled = !running;
            if (_crocLocalBrowseButton != null) _crocLocalBrowseButton.Enabled = !running;
            if (_crocLocalUpButton != null) _crocLocalUpButton.Enabled = !running;
            if (_crocUseSelectionButton != null) _crocUseSelectionButton.Enabled = !running;
            if (_crocLocalGrid != null) _crocLocalGrid.Enabled = !running;
            _crocCodeText.ReadOnly = running;
            _crocReceiveCodeText.ReadOnly = running;
            _crocCancelButton.Enabled = running;
            if (!running) UpdateCrocButtons();
            else
            {
                _crocSendButton.Enabled = false;
                _crocReceiveButton.Enabled = false;
            }
        }

        private void UpdateCrocButtons()
        {
            bool running = _croc != null && _croc.IsRunning;
            bool hasSendCode = _crocCodeText != null && CrocTransferService.NormalizeCode(_crocCodeText.Text).Length >= 6;
            bool hasReceiveCode = _crocReceiveCodeText != null && CrocTransferService.NormalizeCode(_crocReceiveCodeText.Text).Length >= 6;
            bool hasSelection = _crocSelectedPaths.Count > 0;
            if (_crocSendRoleButton != null) _crocSendRoleButton.Enabled = !running;
            if (_crocReceiveRoleButton != null) _crocReceiveRoleButton.Enabled = !running;
            _crocGenerateCodeButton.Enabled = !running && _crocRole == 1;
            _crocPickFileButton.Enabled = !running && _crocRole == 1;
            _crocPickFolderButton.Enabled = !running && _crocRole == 1;
            if (_crocReceiveBrowseButton != null) _crocReceiveBrowseButton.Enabled = !running && _crocRole == 2;
            if (_crocLocalDrivesButton != null) _crocLocalDrivesButton.Enabled = !running && _crocRole == 1;
            if (_crocLocalBrowseButton != null) _crocLocalBrowseButton.Enabled = !running && _crocRole == 1;
            if (_crocLocalUpButton != null) _crocLocalUpButton.Enabled = !running && _crocRole == 1;
            if (_crocUseSelectionButton != null) _crocUseSelectionButton.Enabled = !running && _crocRole == 1;
            if (_crocLocalGrid != null) _crocLocalGrid.Enabled = !running && _crocRole == 1;
            _crocCodeText.ReadOnly = running;
            _crocReceiveCodeText.ReadOnly = running;
            _crocSendButton.Enabled = !running && _crocRole == 1 && hasSendCode && hasSelection;
            _crocReceiveButton.Enabled = !running && _crocRole == 2 && hasReceiveCode;
            _crocCancelButton.Enabled = running;
        }

        private string CurrentCrocReceiveFolder()
        {
            if (Directory.Exists(_crocReceiveFolder)) return _crocReceiveFolder;
            string current = _crocLocalPathText == null ? "" : NormalizeTypedPath(_crocLocalPathText.Text, "Este equipo");
            if (Directory.Exists(current)) return current;
            if (Directory.Exists(_settings.ReceiveFolder)) return _settings.ReceiveFolder;
            if (Directory.Exists(_localCurrentPath)) return _localCurrentPath;
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        private void UpdateCrocReceiveDestinationText()
        {
            if (_crocReceiveDestinationText == null) return;
            _crocReceiveDestinationText.Text = CurrentCrocReceiveFolder();
        }

        private void ApplyCrocState(CrocSessionState state, string message)
        {
            if (_crocStateLabel == null) return;

            string prefix;
            Color color;
            switch (state)
            {
                case CrocSessionState.WaitingForPeer:
                    prefix = "Enlace: esperando contraparte";
                    color = Color.DarkOrange;
                    break;
                case CrocSessionState.PeerConnected:
                    prefix = "Enlace: canal habilitado";
                    color = Color.DarkGreen;
                    if (_crocProgressBar != null)
                    {
                        _crocProgressBar.Style = ProgressBarStyle.Marquee;
                    }
                    if (_crocProgressLabel != null)
                    {
                        _crocProgressLabel.Text = "Progreso remoto: canal habilitado, preparando transferencia...";
                    }
                    break;
                case CrocSessionState.Transferring:
                    prefix = "Enlace: conectado, transfiriendo";
                    color = Color.DarkGreen;
                    if (_crocProgressBar.Style == ProgressBarStyle.Marquee)
                    {
                        _crocProgressBar.Style = ProgressBarStyle.Continuous;
                    }
                    break;
                case CrocSessionState.Completed:
                    prefix = "Enlace: transferencia completa";
                    color = Color.DarkGreen;
                    _crocProgressBar.Style = ProgressBarStyle.Continuous;
                    _crocProgressBar.Value = 100;
                    SetProgressComplete(_crocProgressBar);
                    break;
                case CrocSessionState.Failed:
                    prefix = "Enlace: error";
                    color = Color.DarkRed;
                    _crocProgressBar.Style = ProgressBarStyle.Continuous;
                    break;
                case CrocSessionState.Cancelled:
                    prefix = "Enlace: cancelado";
                    color = Color.DarkRed;
                    _crocProgressBar.Style = ProgressBarStyle.Continuous;
                    break;
                default:
                    prefix = "Enlace: sin preparar";
                    color = Color.Black;
                    break;
            }
            _crocStateLabel.ForeColor = color;
            _crocStateLabel.Text = prefix + " - " + message;
        }

        private void ResetCrocProgress(bool waiting)
        {
            if (_crocProgressBar == null) return;
            _crocProgressBar.Style = waiting ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            _crocProgressBar.Value = 0;
            SetProgressActive(_crocProgressBar);
            _crocProgressLabel.Text = waiting
                ? "Progreso remoto: esperando canal habilitado..."
                : "Progreso remoto: sin transferencia.";
        }

        private void UpdateCrocProgress(CrocProgress progress)
        {
            if (_crocProgressBar == null || progress == null) return;

            if (progress.HasPercent)
            {
                _crocProgressBar.Style = ProgressBarStyle.Continuous;
                int value = Math.Max(0, Math.Min(100, progress.Percent));
                _crocProgressBar.Value = value;
                if (value >= 100)
                {
                    SetProgressComplete(_crocProgressBar);
                }
            }

            string speed = string.IsNullOrWhiteSpace(progress.Speed) ? "velocidad calculando" : progress.Speed;
            string eta = string.IsNullOrWhiteSpace(progress.Eta) ? "ETA calculando" : progress.Eta;
            string percent = progress.HasPercent ? progress.Percent + "%" : "progreso activo";
            _crocProgressLabel.Text = "Progreso remoto: " + percent + " - " + speed + " - restante " + eta;
        }

        private void AppendCrocLog(string line)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendCrocLog), line);
                return;
            }
            string displayLine = FriendlyCrocLogLine(line);
            if (string.IsNullOrWhiteSpace(displayLine)) return;
            _crocLogText.AppendText(displayLine + Environment.NewLine);
        }

        private static string FriendlyCrocLogLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return "";
            string value = line.Trim();
            string lower = value.ToLowerInvariant();

            if (lower == "connecting..." || lower.Contains("connecting"))
            {
                return "Conectando con la otra PC...";
            }
            if (lower.Contains("room") && lower.Contains("not ready"))
            {
                return "Esperando que la otra PC use el mismo código...";
            }
            if (lower.Contains("securing channel"))
            {
                return "Canal encontrado. Habilitando transferencia segura...";
            }
            if (lower.Contains("connected"))
            {
                return "Canal habilitado. Preparando transferencia...";
            }
            if (lower.Contains("sender"))
            {
                return "Canal habilitado con la PC emisora.";
            }
            if (lower.Contains("recipient"))
            {
                return "Canal habilitado con la PC receptora.";
            }
            return value;
        }

        private void LoadParentLocal()
        {
            LoadLocalPath(ParentPath(_localCurrentPath));
        }

        private static string ParentPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            DirectoryInfo directory = new DirectoryInfo(path);
            return directory.Parent == null ? path : directory.Parent.FullName;
        }

        private void OpenLocalSelection()
        {
            FileSystemEntry entry = SelectedEntry(_localGrid);
            if (entry != null && entry.IsDirectory) LoadLocalPath(entry.FullPath);
        }

        private void OpenRemoteSelection()
        {
            FileSystemEntry entry = SelectedEntry(_remoteGrid);
            if (entry != null && entry.IsDirectory && _connection != null)
            {
                _connection.RequestRemoteList(entry.FullPath);
            }
        }

        private void LocalGridKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                OpenLocalSelection();
                return;
            }

            if (e.KeyCode == Keys.Back)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                LoadParentLocal();
            }
        }

        private void RemoteGridKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                OpenRemoteSelection();
                return;
            }

            if (e.KeyCode == Keys.Back)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                if (_connection != null) _connection.RequestRemoteList(ParentPath(_remoteCurrentPath));
            }
        }

        private static FileSystemEntry SelectedEntry(DataGridView grid)
        {
            if (grid.SelectedRows.Count == 0) return null;
            return grid.SelectedRows[0].Tag as FileSystemEntry;
        }

        private List<string> SelectedLocalPaths()
        {
            var paths = new List<string>();
            foreach (DataGridViewRow row in _localGrid.SelectedRows)
            {
                var entry = row.Tag as FileSystemEntry;
                if (entry != null && entry.Name != "..") paths.Add(entry.FullPath);
            }
            return paths;
        }

        private List<string> SelectedRemotePaths()
        {
            var paths = new List<string>();
            foreach (DataGridViewRow row in _remoteGrid.SelectedRows)
            {
                var entry = row.Tag as FileSystemEntry;
                if (entry != null && entry.Name != "..") paths.Add(entry.FullPath);
            }
            return paths;
        }

        private async Task SendSelectedLocalAsync()
        {
            await SendPathsAsync(SelectedLocalPaths());
        }

        private async Task SendPathsAsync(IEnumerable<string> paths)
        {
            if (_connection == null)
            {
                SetStatus("Primero conectá con otro equipo.");
                return;
            }
            if (string.IsNullOrEmpty(_remoteCurrentPath))
            {
                SetStatus("Primero esperá o abrí una carpeta remota.");
                return;
            }

            var pathList = new List<string>();
            foreach (string path in paths ?? new string[0])
            {
                if (!string.IsNullOrWhiteSpace(path)) pathList.Add(path);
            }

            if (pathList.Count == 0)
            {
                SetStatus("Seleccioná uno o más archivos o carpetas para enviar.");
                return;
            }

            _sendQueue.Enqueue(new QueuedSend
            {
                Paths = pathList,
                DestinationDirectory = _remoteCurrentPath,
                Name = BuildQueuedSendName(pathList)
            });

            if (_sendQueueRunning)
            {
                SetStatus("Agregado a la cola: " + BuildQueuedSendName(pathList) + ". En espera: " + _sendQueue.Count + ".");
                return;
            }

            await ProcessSendQueueAsync();
        }

        private async Task ProcessSendQueueAsync()
        {
            if (_sendQueueRunning) return;
            _sendQueueRunning = true;

            try
            {
                while (_sendQueue.Count > 0)
                {
                    QueuedSend item = _sendQueue.Dequeue();
                    ResetProgress();
                    SetStatus("Enviando: " + item.Name + QueueSuffix());
                    await _connection.SendPathsAsync(item.Paths, item.DestinationDirectory);
                    _connection.RequestRemoteList(item.DestinationDirectory);
                    SetStatus("Envío finalizado: " + item.Name + QueueSuffix());
                }
            }
            catch (Exception ex)
            {
                SetStatus("Error enviando: " + ex.Message + QueueSuffix());
            }
            finally
            {
                _sendQueueRunning = false;
            }
        }

        private string QueueSuffix()
        {
            return _sendQueue.Count == 0 ? "" : " - en cola: " + _sendQueue.Count;
        }

        private static string BuildQueuedSendName(List<string> paths)
        {
            if (paths == null || paths.Count == 0) return "transferencia";
            string first = Path.GetFileName(paths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(first)) first = paths[0];
            return paths.Count == 1 ? first : first + " y " + (paths.Count - 1) + " más";
        }

        private TransferDecision RequestTransferDecision(TransferOffer offer)
        {
            if (InvokeRequired)
            {
                return (TransferDecision)Invoke(new Func<TransferOffer, TransferDecision>(RequestTransferDecision), offer);
            }

            string originalDestination = Path.Combine(
                string.IsNullOrEmpty(offer.DestinationDirectory) ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : offer.DestinationDirectory,
                offer.RelativePath);
            bool exists = File.Exists(originalDestination) || Directory.Exists(originalDestination);
            if (!exists) return _connection.CreateDecision(offer, ConflictAction.Overwrite);
            if (offer.IsDirectory) return _connection.CreateDecision(offer, ConflictAction.Overwrite);

            if (_existingItemMode == ExistingItemMode.Ask)
            {
                using (var dialog = new ExistingItemDialog(originalDestination))
                {
                    DialogResult result = dialog.ShowDialog(this);
                    if (result != DialogResult.OK) return _connection.CreateDecision(offer, ConflictAction.Skip);
                    _existingItemMode = dialog.SelectedMode;
                }
            }

            if (_existingItemMode == ExistingItemMode.CopyMissing)
            {
                return _connection.CreateDecision(offer, offer.IsDirectory ? ConflictAction.Overwrite : ConflictAction.Skip);
            }

            return _connection.CreateDecision(offer, ConflictAction.Overwrite);
        }

        private void LocalGridMouseDown(object sender, MouseEventArgs e)
        {
            PrepareGridMouseDown(_localGrid, e, out _localDragStartPoint, out _localDragDropArmed, out _localSelectionAnchorRow);
            _localDragPaths = _localDragDropArmed ? SelectedLocalPaths() : null;
        }

        private void LocalGridMouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _localDragStartPoint == Point.Empty) return;
            if (!_localDragDropArmed)
            {
                ExtendMouseSelection(_localGrid, _localSelectionAnchorRow, e.Location);
                return;
            }

            Rectangle dragBox = new Rectangle(
                _localDragStartPoint.X - SystemInformation.DragSize.Width / 2,
                _localDragStartPoint.Y - SystemInformation.DragSize.Height / 2,
                SystemInformation.DragSize.Width,
                SystemInformation.DragSize.Height);
            if (dragBox.Contains(e.Location)) return;

            List<string> paths = _localDragPaths != null && _localDragPaths.Count > 0 ? _localDragPaths : SelectedLocalPaths();
            if (paths.Count > 0)
            {
                var data = new DataObject();
                data.SetData(LocalPathsDragFormat, paths.ToArray());
                data.SetData(DataFormats.FileDrop, paths.ToArray());
                _localGrid.DoDragDrop(data, DragDropEffects.Copy);
            }
            ResetLocalMouseDrag();
        }

        private void LocalGridMouseUp(object sender, MouseEventArgs e)
        {
            ResetLocalMouseDrag();
        }

        private void RemoteGridMouseDown(object sender, MouseEventArgs e)
        {
            PrepareGridMouseDown(_remoteGrid, e, out _remoteDragStartPoint, out _remoteDragDropArmed, out _remoteSelectionAnchorRow);
            _remoteDragPaths = _remoteDragDropArmed ? SelectedRemotePaths() : null;
        }

        private void RemoteGridMouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _remoteDragStartPoint == Point.Empty) return;
            if (!_remoteDragDropArmed)
            {
                ExtendMouseSelection(_remoteGrid, _remoteSelectionAnchorRow, e.Location);
                return;
            }

            Rectangle dragBox = new Rectangle(
                _remoteDragStartPoint.X - SystemInformation.DragSize.Width / 2,
                _remoteDragStartPoint.Y - SystemInformation.DragSize.Height / 2,
                SystemInformation.DragSize.Width,
                SystemInformation.DragSize.Height);
            if (dragBox.Contains(e.Location)) return;

            List<string> paths = _remoteDragPaths != null && _remoteDragPaths.Count > 0 ? _remoteDragPaths : SelectedRemotePaths();
            if (paths.Count > 0)
            {
                var data = new DataObject();
                data.SetData(RemotePathsDragFormat, paths.ToArray());
                _remoteGrid.DoDragDrop(data, DragDropEffects.Copy);
            }
            ResetRemoteMouseDrag();
        }

        private void RemoteGridMouseUp(object sender, MouseEventArgs e)
        {
            ResetRemoteMouseDrag();
        }

        private static void PrepareGridMouseDown(DataGridView grid, MouseEventArgs e, out Point dragStartPoint, out bool dragDropArmed, out int selectionAnchorRow)
        {
            dragStartPoint = Point.Empty;
            dragDropArmed = false;
            selectionAnchorRow = -1;

            if (e.Button != MouseButtons.Left) return;

            DataGridView.HitTestInfo hit = grid.HitTest(e.X, e.Y);
            if (hit.RowIndex < 0) return;

            bool selectionModifier = (Control.ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None;
            if (selectionModifier) return;

            dragStartPoint = e.Location;
            selectionAnchorRow = hit.RowIndex;
            dragDropArmed = !selectionModifier && grid.Rows[hit.RowIndex].Selected;
        }

        private static void ExtendMouseSelection(DataGridView grid, int anchorRow, Point location)
        {
            if (anchorRow < 0) return;

            DataGridView.HitTestInfo hit = grid.HitTest(location.X, location.Y);
            if (hit.RowIndex < 0) return;

            int start = Math.Min(anchorRow, hit.RowIndex);
            int end = Math.Max(anchorRow, hit.RowIndex);
            grid.ClearSelection();
            for (int i = start; i <= end; i++)
            {
                grid.Rows[i].Selected = true;
            }
        }

        private void ResetLocalMouseDrag()
        {
            _localDragStartPoint = Point.Empty;
            _localDragDropArmed = false;
            _localSelectionAnchorRow = -1;
            _localDragPaths = null;
        }

        private void ResetRemoteMouseDrag()
        {
            _remoteDragStartPoint = Point.Empty;
            _remoteDragDropArmed = false;
            _remoteSelectionAnchorRow = -1;
            _remoteDragPaths = null;
        }

        private void LocalGridDragEnter(object sender, DragEventArgs e)
        {
            bool hasPaths = e.Data.GetDataPresent(RemotePathsDragFormat);
            bool canReceive = _connection != null && _connection.IsConnected && !string.IsNullOrEmpty(_localCurrentPath);
            e.Effect = hasPaths && canReceive ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private async Task LocalGridDragDropAsync(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(RemotePathsDragFormat)) return;
            string[] paths = (string[])e.Data.GetData(RemotePathsDragFormat);
            await RequestRemoteSendPathsAsync(paths);
        }

        private void RemoteGridDragEnter(object sender, DragEventArgs e)
        {
            bool hasPaths = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(LocalPathsDragFormat);
            bool canSend = _connection != null && _connection.IsConnected && !string.IsNullOrEmpty(_remoteCurrentPath);
            if (hasPaths && canSend)
            {
                e.Effect = DragDropEffects.Copy;
                return;
            }
            e.Effect = DragDropEffects.None;
        }

        private async Task RemoteGridDragDropAsync(DragEventArgs e)
        {
            string[] paths = null;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            }
            else if (e.Data.GetDataPresent(LocalPathsDragFormat))
            {
                paths = (string[])e.Data.GetData(LocalPathsDragFormat);
            }
            if (paths != null) await SendPathsAsync(paths);
        }

        private async Task RequestRemoteSendPathsAsync(IEnumerable<string> remotePaths)
        {
            if (_connection == null)
            {
                SetStatus("Primero conectá con otro equipo.");
                return;
            }
            if (string.IsNullOrEmpty(_localCurrentPath))
            {
                SetStatus("Abrí una carpeta local de destino.");
                return;
            }

            try
            {
                ResetProgress();
                SetStatus("Pidiendo archivos al equipo remoto...");
                _connection.RequestSendPaths(remotePaths, _localCurrentPath);
                await Task.Delay(1);
            }
            catch (Exception ex)
            {
                SetStatus("Error pidiendo archivos remotos: " + ex.Message);
            }
        }

        private void UpdateProgress(TransferProgress progress)
        {
            int filePercent = progress.IsAggregate
                ? CalculatePercent(progress.CurrentFileBytesTransferred, progress.CurrentFileTotalBytes)
                : CalculatePercent(progress.BytesTransferred, progress.TotalBytes);
            int totalPercent = progress.IsAggregate
                ? CalculateFileCountPercent(progress.CurrentFileIndex, progress.TotalFiles, filePercent)
                : filePercent;

            _progressBar.Value = totalPercent;
            _fileProgressBar.Value = filePercent;
            if (totalPercent >= 100)
            {
                SetProgressComplete(_progressBar);
                SetProgressComplete(_fileProgressBar);
                _existingItemMode = ExistingItemMode.Ask;
            }
            else
            {
                SetProgressActive(_progressBar);
                SetProgressActive(_fileProgressBar);
            }

            string speed = FileSystemEntry.FormatBytes((long)progress.BytesPerSecond) + "/s";
            string remaining = progress.EstimatedRemaining == TimeSpan.Zero ? "--:--" : progress.EstimatedRemaining.ToString(@"hh\:mm\:ss");
            if (progress.IsAggregate)
            {
                string type = progress.IsFolder ? "Carpeta" : "Archivos";
                string fileCounter = progress.TotalFiles > 0
                    ? "archivo " + progress.CurrentFileIndex + "/" + progress.TotalFiles
                    : "sin archivos";
                if (progress.TotalFiles > 0 && progress.CurrentFileIndex <= 0)
                {
                    fileCounter = "preparando " + progress.TotalFiles + " archivos";
                }

                _progressLabel.Text = string.Format("Total {0}: {1}% - {2} - {3} archivos - ETA {4}",
                    type,
                    totalPercent,
                    fileCounter,
                    progress.TotalFiles,
                    remaining);
                _fileProgressLabel.Text = string.Format("Archivo actual: {0}% - {1} ({2} de {3}) - {4}",
                    filePercent,
                    progress.FileName,
                    FileSystemEntry.FormatBytes(progress.CurrentFileBytesTransferred),
                    FileSystemEntry.FormatBytes(progress.CurrentFileTotalBytes),
                    fileCounter);
                return;
            }

            _progressLabel.Text = string.Format("Total archivo: {0}% ({1} de {2}) - {3} - ETA {4}",
                totalPercent,
                FileSystemEntry.FormatBytes(progress.BytesTransferred),
                FileSystemEntry.FormatBytes(progress.TotalBytes),
                speed,
                remaining);
            _fileProgressLabel.Text = string.Format("Archivo actual: {0}% - {1}",
                filePercent,
                progress.FileName,
                FileSystemEntry.FormatBytes(progress.BytesTransferred));
        }

        private static int CalculatePercent(long transferred, long total)
        {
            if (total <= 0) return 0;
            return (int)Math.Min(100, Math.Max(0, transferred * 100 / total));
        }

        private static int CalculateFileCountPercent(int currentFileIndex, int totalFiles, int currentFilePercent)
        {
            if (totalFiles <= 0) return 100;
            int completedFiles = Math.Max(0, currentFileIndex - 1);
            long scaled = completedFiles * 100L + Math.Max(0, Math.Min(100, currentFilePercent));
            return (int)Math.Min(100, Math.Max(0, scaled / totalFiles));
        }

        private void ResetProgress()
        {
            _existingItemMode = ExistingItemMode.Ask;
            _progressBar.Value = 0;
            _fileProgressBar.Value = 0;
            SetProgressActive(_progressBar);
            SetProgressActive(_fileProgressBar);
            _fileProgressLabel.Text = "Archivo: esperando primer archivo...";
            _progressLabel.Text = "Total: preparando transferencia...";
        }

        private static void PrepareProgressBar(ProgressBar progressBar)
        {
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.BackColor = Color.FromArgb(229, 234, 242);
            SetProgressActive(progressBar);
            progressBar.HandleCreated += (s, e) => DisableProgressTheme((ProgressBar)s);
            if (progressBar.IsHandleCreated)
            {
                DisableProgressTheme(progressBar);
            }
        }

        private static void SetProgressActive(ProgressBar progressBar)
        {
            progressBar.ForeColor = PrimaryColor;
        }

        private static void SetProgressComplete(ProgressBar progressBar)
        {
            progressBar.ForeColor = CompleteProgressColor;
        }

        private static void DisableProgressTheme(ProgressBar progressBar)
        {
            NativeMethods.SetWindowTheme(progressBar.Handle, "", "");
        }

        private void SetStatus(string text)
        {
            if (_statusLabel == null) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(SetStatus), text);
                return;
            }
            _statusLabel.Text = text;
        }

        private sealed class ExistingItemDialog : Form
        {
            public ExistingItemMode SelectedMode { get; private set; }

            public ExistingItemDialog(string path)
            {
                SelectedMode = ExistingItemMode.CopyMissing;
                Text = "Elementos repetidos";
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = false;
                MaximizeBox = false;
                ClientSize = new Size(560, 178);
                BackColor = AppBackground;

                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(12) };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                Controls.Add(layout);

                var message = new Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = TextColor,
                    Text = "Ya existe un archivo o carpeta en destino:\r\n" + path
                };
                layout.Controls.Add(message, 0, 0);

                var hint = new Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = MutedTextColor,
                    Text = "Elegí cómo aplicar esta copia. La decisión se usará para el resto de esta transferencia."
                };
                layout.Controls.Add(hint, 0, 1);

                var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
                layout.Controls.Add(buttons, 0, 2);

                var copyMissingButton = new Button { Dock = DockStyle.Fill, Text = "Copiar faltantes" };
                var recopyButton = new Button { Dock = DockStyle.Fill, Text = "Copiar todo otra vez" };
                StyleButton(copyMissingButton);
                StyleButton(recopyButton);

                copyMissingButton.Click += (s, e) =>
                {
                    SelectedMode = ExistingItemMode.CopyMissing;
                    DialogResult = DialogResult.OK;
                    Close();
                };
                recopyButton.Click += (s, e) =>
                {
                    SelectedMode = ExistingItemMode.RecopyAll;
                    DialogResult = DialogResult.OK;
                    Close();
                };

                buttons.Controls.Add(copyMissingButton, 1, 0);
                buttons.Controls.Add(recopyButton, 2, 0);
                AcceptButton = copyMissingButton;
            }
        }
    }

    internal sealed class RemoteFolderPickerDialog : Form
    {
        private const string DummyNodeName = "__loading__";
        private readonly Action<string> _requestList;
        private readonly HashSet<string> _requestedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly TreeView _tree;
        private readonly TextBox _pathText;
        private readonly Button _acceptButton;

        public string SelectedPath { get; private set; }

        public RemoteFolderPickerDialog(string initialPath, Action<string> requestList)
        {
            _requestList = requestList;
            SelectedPath = initialPath ?? "";

            Text = "Buscar carpeta";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(590, 560);
            BackColor = Color.FromArgb(246, 248, 251);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(12) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Controls.Add(layout);

            var title = new Label { Text = "Carpetas del equipo remoto", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            layout.Controls.Add(title, 0, 0);

            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                BorderStyle = BorderStyle.FixedSingle
            };
            _tree.BeforeExpand += TreeBeforeExpand;
            _tree.AfterSelect += TreeAfterSelect;
            _tree.NodeMouseDoubleClick += (s, e) => AcceptSelectedNode();
            layout.Controls.Add(_tree, 0, 1);

            _pathText = new TextBox { Dock = DockStyle.Fill, Text = SelectedPath };
            _pathText.KeyDown += PathTextKeyDown;
            layout.Controls.Add(_pathText, 0, 2);

            var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            layout.Controls.Add(buttons, 0, 3);

            _acceptButton = new Button { Text = "Aceptar", Dock = DockStyle.Fill };
            _acceptButton.Click += (s, e) => AcceptSelectedNode();
            var cancelButton = new Button { Text = "Cancelar", Dock = DockStyle.Fill, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(_acceptButton, 1, 0);
            buttons.Controls.Add(cancelButton, 2, 0);

            AcceptButton = _acceptButton;
            CancelButton = cancelButton;
        }

        public void RequestPath(string path)
        {
            string cleanPath = path ?? "";
            _requestedPaths.Add(cleanPath);
            if (_requestList != null) _requestList(cleanPath);
        }

        public bool HandleListResponse(ListResponse response)
        {
            string path = response == null ? "" : response.Path ?? "";
            if (!_requestedPaths.Contains(path)) return false;

            _requestedPaths.Remove(path);
            if (string.IsNullOrEmpty(path))
            {
                LoadRoot(response);
            }
            else
            {
                LoadChildren(path, response);
            }
            return true;
        }

        private void LoadRoot(ListResponse response)
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            foreach (FileSystemEntry entry in response.Entries)
            {
                if (!entry.IsDirectory) continue;
                _tree.Nodes.Add(CreateFolderNode(entry.Name, entry.FullPath));
            }
            _tree.EndUpdate();
            SelectInitialPathIfPossible();
        }

        private void LoadChildren(string parentPath, ListResponse response)
        {
            TreeNode parent = FindNodeByPath(parentPath);
            if (parent == null) return;

            parent.Nodes.Clear();
            foreach (FileSystemEntry entry in response.Entries)
            {
                if (!entry.IsDirectory || entry.Name == "..") continue;
                parent.Nodes.Add(CreateFolderNode(entry.Name, entry.FullPath));
            }
            parent.Expand();
            SelectInitialPathIfPossible();
        }

        private TreeNode CreateFolderNode(string name, string path)
        {
            var node = new TreeNode(name) { Tag = path };
            node.Nodes.Add(new TreeNode("Cargando...") { Name = DummyNodeName });
            return node;
        }

        private void TreeBeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            if (e.Node == null || e.Node.Nodes.Count != 1 || e.Node.Nodes[0].Name != DummyNodeName) return;
            string path = e.Node.Tag as string;
            if (path == null) return;
            RequestPath(path);
        }

        private void TreeAfterSelect(object sender, TreeViewEventArgs e)
        {
            string path = e.Node == null ? "" : e.Node.Tag as string;
            SelectedPath = path ?? "";
            _pathText.Text = SelectedPath;
        }

        private void PathTextKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            SelectedPath = (_pathText.Text ?? "").Trim();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void AcceptSelectedNode()
        {
            SelectedPath = (_pathText.Text ?? "").Trim();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void SelectInitialPathIfPossible()
        {
            if (string.IsNullOrWhiteSpace(SelectedPath)) return;
            TreeNode node = FindNodeByPath(SelectedPath);
            if (node != null) _tree.SelectedNode = node;
        }

        private TreeNode FindNodeByPath(string path)
        {
            foreach (TreeNode node in _tree.Nodes)
            {
                TreeNode found = FindNodeByPath(node, path);
                if (found != null) return found;
            }
            return null;
        }

        private static TreeNode FindNodeByPath(TreeNode node, string path)
        {
            string nodePath = node.Tag as string;
            if (string.Equals(nodePath, path, StringComparison.OrdinalIgnoreCase)) return node;
            foreach (TreeNode child in node.Nodes)
            {
                TreeNode found = FindNodeByPath(child, path);
                if (found != null) return found;
            }
            return null;
        }
    }

    internal static class NativeMethods
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        internal static extern int SetWindowTheme(IntPtr hwnd, string appName, string partList);
    }
}
