using System;
using System.Collections.Generic;
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

        private ComboBox _modeCombo;
        private ComboBox _scopeCombo;
        private TextBox _ipText;
        private TextBox _portText;
        private TextBox _keyText;
        private Button _connectButton;
        private Button _discoverButton;
        private Button _connectSelectedServerButton;
        private Label _settingsSummaryLabel;
        private Label _localIpLabel;
        private TextBox _crocCodeText;
        private TextBox _crocReceiveCodeText;
        private TextBox _crocPathText;
        private TextBox _crocLogText;
        private Label _crocStateLabel;
        private ProgressBar _crocProgressBar;
        private Label _crocProgressLabel;
        private ComboBox _crocRoleCombo;
        private Panel _crocRoleHostPanel;
        private Control _crocSendPanel;
        private Control _crocReceivePanel;
        private Button _crocGenerateCodeButton;
        private Button _crocPickFileButton;
        private Button _crocPickFolderButton;
        private Button _crocSendButton;
        private Button _crocReceiveButton;
        private Button _crocCancelButton;
        private ListBox _serversList;
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

        private TransferServer _server;
        private DiscoveryService _discovery;
        private PeerConnection _connection;
        private CrocTransferService _croc;
        private RemoteFolderPickerDialog _remoteFolderPicker;
        private AppSettings _settings;
        private string _localCurrentPath;
        private string _remoteCurrentPath;
        private bool _directRemoteMode;
        private int _workMode;
        private Point _localDragStartPoint;
        private Point _remoteDragStartPoint;
        private readonly List<string> _crocSelectedPaths = new List<string>();
        private const string LocalPathsDragFormat = "FileSender.LocalPaths";
        private const string RemotePathsDragFormat = "FileSender.RemotePaths";
        private ExistingItemMode _existingItemMode = ExistingItemMode.Ask;

        public MainForm()
        {
            Text = "File Sender";
            Width = 1280;
            Height = 760;
            MinimumSize = new Size(1100, 620);
            StartPosition = FormStartPosition.CenterScreen;

            _settings = AppSettings.Load();
            BuildUi();
            ApplyVisualStyle(this);
            _localCurrentPath = Directory.Exists(_settings.LocalStartFolder)
                ? _settings.LocalStartFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            LoadLocalPath(_localCurrentPath);
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

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(12, 38, 12, 12) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
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
            grid.RowTemplate.Height = 26;
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
            if (_serversList != null) _serversList.Items.Clear();
            if (_connectSelectedServerButton != null) _connectSelectedServerButton.Enabled = false;
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
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(8) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
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

            _localSendRoleButton = new Button { Dock = DockStyle.Fill, Text = "Conectar o recibir archivos\r\nEsta PC escucha automáticamente", Font = new Font(Font.FontFamily, 11, FontStyle.Bold) };
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
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 9, RowCount = 2 };
            for (int i = 0; i < 9; i++) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 11.11f));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            _modeCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _modeCombo.Items.AddRange(new object[] { "Servidor", "Cliente" });
            _modeCombo.SelectedIndex = 0;
            _scopeCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _scopeCombo.Items.AddRange(new object[] { "Local LAN", "Remoto Internet" });
            _scopeCombo.SelectedIndex = 0;
            _scopeCombo.SelectedIndexChanged += (s, e) => ApplyScopeText();

            _ipText = new TextBox { Dock = DockStyle.Fill, Text = "127.0.0.1" };
            _portText = new TextBox { Dock = DockStyle.Fill, Text = _settings.TcpPort.ToString(), Visible = false };
            _keyText = new TextBox { Dock = DockStyle.Fill, Text = _settings.SharedKey, UseSystemPasswordChar = true, Visible = false };
            _connectButton = new Button { Dock = DockStyle.Fill, Text = "Conectar" };
            _discoverButton = new Button { Dock = DockStyle.Fill, Text = "Buscar PC" };
            _connectSelectedServerButton = new Button { Dock = DockStyle.Fill, Text = "Conectar seleccionado", Enabled = false };
            _settingsSummaryLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            _localIpLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            _serversList = new ListBox { Dock = DockStyle.Fill };

            _connectButton.Click += async (s, e) => await ConnectToIpAsync(_ipText.Text.Trim());
            _discoverButton.Click += async (s, e) => await DiscoverAsync();
            _connectSelectedServerButton.Click += async (s, e) => await ConnectSelectedServerAsync();
            _serversList.SelectedIndexChanged += (s, e) => _connectSelectedServerButton.Enabled = _serversList.SelectedItem is DiscoveredServer;

            panel.Controls.Add(new Label { Text = "IP para conexión", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            panel.SetColumnSpan(_ipText, 2);
            panel.Controls.Add(_ipText, 1, 0);
            panel.Controls.Add(_connectButton, 7, 0);
            panel.Controls.Add(_discoverButton, 8, 0);

            panel.Controls.Add(new Label { Text = "Configuración", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            panel.SetColumnSpan(_settingsSummaryLabel, 2);
            panel.Controls.Add(_settingsSummaryLabel, 1, 1);
            panel.SetColumnSpan(_localIpLabel, 2);
            panel.Controls.Add(_localIpLabel, 3, 1);
            panel.Controls.Add(new Label { Text = "Servidores LAN", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 5, 1);
            panel.SetColumnSpan(_serversList, 2);
            panel.Controls.Add(_serversList, 6, 1);
            panel.Controls.Add(_connectSelectedServerButton, 8, 1);

            UpdateSettingsSummary();
            return panel;
        }

        private Control BuildFilePanel()
        {
            var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
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
            _localGrid.CellDoubleClick += (s, e) => OpenLocalSelection();
            _localGrid.MouseDown += LocalGridMouseDown;
            _localGrid.MouseMove += LocalGridMouseMove;
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
            _remoteGrid.CellDoubleClick += (s, e) => OpenRemoteSelection();
            _remoteGrid.MouseDown += RemoteGridMouseDown;
            _remoteGrid.MouseMove += RemoteGridMouseMove;
            _remoteGrid.DragEnter += RemoteGridDragEnter;
            _remoteGrid.DragDrop += async (s, e) => await RemoteGridDragDropAsync(e);
            return panel;
        }

        private Panel BuildBrowserPanel(string title, out TextBox pathText, out DataGridView grid)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
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
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false
            };
            grid.Columns.Add("Name", "Nombre");
            grid.Columns.Add("Size", "Tamaño");
            grid.Columns["Size"].Width = 110;
            layout.Controls.Add(grid, 0, 3);

            panel.Tag = buttons;
            return panel;
        }

        private Control BuildProgressPanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            _progressBar = new ProgressBar { Dock = DockStyle.Fill };
            PrepareProgressBar(_progressBar);
            _progressLabel = new Label { Dock = DockStyle.Fill, Text = "Total: sin transferencia.", TextAlign = ContentAlignment.MiddleLeft };
            _fileProgressBar = new ProgressBar { Dock = DockStyle.Fill };
            PrepareProgressBar(_fileProgressBar);
            _fileProgressLabel = new Label { Dock = DockStyle.Fill, Text = "Archivo: sin transferencia.", TextAlign = ContentAlignment.MiddleLeft };
            panel.Controls.Add(_progressBar, 0, 0);
            panel.Controls.Add(_progressLabel, 0, 1);
            panel.Controls.Add(_fileProgressBar, 0, 2);
            panel.Controls.Add(_fileProgressLabel, 0, 3);
            return panel;
        }

        private Control BuildSimpleInternetPanel()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(2) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _crocRoleCombo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _crocRoleCombo.Items.AddRange(new object[] { "Emisor - esta PC envía archivos", "Receptor - esta PC recibe archivos" });
            _crocRoleCombo.SelectedIndex = 0;
            _crocCodeText = new TextBox { Dock = DockStyle.Fill };
            _crocReceiveCodeText = new TextBox { Dock = DockStyle.Fill };
            _crocPathText = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
            _crocLogText = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical };
            _crocStateLabel = new Label { Dock = DockStyle.Fill, Text = "Enlace: sin preparar", TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) };
            _crocProgressBar = new ProgressBar { Dock = DockStyle.Fill };
            PrepareProgressBar(_crocProgressBar);
            _crocProgressLabel = new Label { Dock = DockStyle.Fill, Text = "Progreso remoto: sin transferencia.", TextAlign = ContentAlignment.MiddleLeft };
            _crocGenerateCodeButton = new Button { Dock = DockStyle.Fill, Text = "Generar código" };
            _crocPickFileButton = new Button { Dock = DockStyle.Fill, Text = "Elegir archivo" };
            _crocPickFolderButton = new Button { Dock = DockStyle.Fill, Text = "Elegir carpeta" };
            _crocSendButton = new Button { Dock = DockStyle.Fill, Text = "Iniciar envío", Enabled = false };
            _crocReceiveButton = new Button { Dock = DockStyle.Fill, Text = "Iniciar recepción", Enabled = false };
            _crocCancelButton = new Button { Dock = DockStyle.Fill, Text = "Cancelar", Enabled = false };

            _crocRoleCombo.SelectedIndexChanged += (s, e) => UpdateCrocRoleVisibility();
            _crocCodeText.TextChanged += (s, e) => UpdateCrocButtons();
            _crocReceiveCodeText.TextChanged += (s, e) => UpdateCrocButtons();
            _crocGenerateCodeButton.Click += (s, e) =>
            {
                _crocCodeText.Text = CrocTransferService.GenerateCode();
                ApplyCrocState(CrocSessionState.Idle, "Rol emisor listo. Compartí el código con el receptor.");
            };
            _crocPickFileButton.Click += (s, e) => PickCrocFiles();
            _crocPickFolderButton.Click += (s, e) => PickCrocFolder();
            _crocSendButton.Click += async (s, e) => await SendWithCrocAsync();
            _crocReceiveButton.Click += async (s, e) => await ReceiveWithCrocAsync();
            _crocCancelButton.Click += (s, e) => _croc.Cancel();

            layout.Controls.Add(BuildCrocRoleSelector(), 0, 0);

            _crocRoleHostPanel = new Panel { Dock = DockStyle.Fill };
            _crocSendPanel = BuildCrocSendPanel();
            _crocReceivePanel = BuildCrocReceivePanel();
            _crocRoleHostPanel.Controls.Add(_crocSendPanel);
            _crocRoleHostPanel.Controls.Add(_crocReceivePanel);
            layout.Controls.Add(_crocRoleHostPanel, 0, 1);

            layout.Controls.Add(_crocStateLabel, 0, 2);

            layout.Controls.Add(BuildCrocProgressPanel(), 0, 3);
            layout.Controls.Add(_crocLogText, 0, 4);
            UpdateCrocRoleVisibility();
            return layout;
        }

        private Control BuildCrocRoleSelector()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            panel.Controls.Add(new Label { Text = "Rol de esta PC", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            panel.Controls.Add(_crocRoleCombo, 1, 0);
            panel.Controls.Add(_crocCancelButton, 2, 0);
            return panel;
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
            var panel = new GroupBox { Dock = DockStyle.Fill, Text = "Rol Emisor - genera código y envía" };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, Padding = new Padding(4) };
            panel.Controls.Add(layout);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            layout.Controls.Add(_crocGenerateCodeButton, 0, 0);
            layout.Controls.Add(_crocPickFileButton, 1, 0);
            layout.Controls.Add(_crocPickFolderButton, 2, 0);
            layout.Controls.Add(_crocSendButton, 3, 0);
            layout.Controls.Add(new Label { Text = "Código para el receptor", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            layout.SetColumnSpan(_crocCodeText, 3);
            layout.Controls.Add(_crocCodeText, 1, 1);
            layout.Controls.Add(new Label { Text = "Archivo/carpeta", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            layout.SetColumnSpan(_crocPathText, 3);
            layout.Controls.Add(_crocPathText, 1, 2);
            return panel;
        }

        private Control BuildCrocReceivePanel()
        {
            var panel = new GroupBox { Dock = DockStyle.Fill, Text = "Rol Receptor - ingresa código y recibe" };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Padding = new Padding(4) };
            panel.Controls.Add(layout);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            layout.Controls.Add(new Label { Text = "Código del emisor", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            layout.Controls.Add(_crocReceiveCodeText, 1, 0);
            layout.SetColumnSpan(_crocReceiveButton, 2);
            layout.Controls.Add(_crocReceiveButton, 2, 0);
            var destinationLabel = new Label { Text = "Destino: carpeta de recibidos configurada", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            layout.SetColumnSpan(destinationLabel, 4);
            layout.Controls.Add(destinationLabel, 0, 1);
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

            _serversList.Items.Clear();
            SetStatus("Buscando servidores en la red local...");
            List<DiscoveredServer> servers = await DiscoveryService.DiscoverAsync(1600);
            foreach (DiscoveredServer server in servers)
            {
                _serversList.Items.Add(server);
            }
            _connectSelectedServerButton.Enabled = false;
            SetStatus(servers.Count == 0
                ? "No se encontraron servidores LAN. Verificá que la otra PC tenga File Sender abierto en Modo Local y que Firewall permita la app."
                : "Servidores LAN encontrados: " + servers.Count + ". Seleccioná uno y pulsá Conectar.");
        }

        private async Task ConnectSelectedServerAsync()
        {
            var selected = _serversList.SelectedItem as DiscoveredServer;
            if (selected == null)
            {
                SetStatus("Seleccioná un servidor LAN encontrado.");
                return;
            }

            _ipText.Text = selected.Address;
            await ConnectToIpAsync(selected.Address, selected.Port);
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
            if (_settingsSummaryLabel == null || _settings == null) return;
            _settingsSummaryLabel.Text = "Puerto " + _settings.TcpPort + " | Recibidos: " + ShortPath(_settings.ReceiveFolder);
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

        private static string ShortPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            const int max = 48;
            return path.Length <= max ? path : "..." + path.Substring(path.Length - max);
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
                        AddRow(_localGrid, new FileSystemEntry { Name = name, FullPath = drive.Name, IsDirectory = true });
                    }
                    return;
                }

                DirectoryInfo directory = new DirectoryInfo(path);
                if (directory.Parent != null) AddRow(_localGrid, new FileSystemEntry { Name = "..", FullPath = directory.Parent.FullName, IsDirectory = true });
                else AddRow(_localGrid, new FileSystemEntry { Name = "..", FullPath = "", IsDirectory = true });

                foreach (DirectoryInfo child in directory.GetDirectories())
                {
                    AddRow(_localGrid, new FileSystemEntry { Name = child.Name, FullPath = child.FullName, IsDirectory = true });
                }
                foreach (FileInfo file in directory.GetFiles())
                {
                    AddRow(_localGrid, new FileSystemEntry { Name = file.Name, FullPath = file.FullName, IsDirectory = false, Size = file.Length });
                }
            }
            catch (Exception ex)
            {
                SetStatus("No se pudo abrir carpeta local: " + ex.Message);
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
            int row = grid.Rows.Add(entry.Name, entry.DisplaySize);
            grid.Rows[row].Tag = entry;
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
                _crocPathText.Text = string.Join("; ", _crocSelectedPaths.ToArray());
                if (string.IsNullOrWhiteSpace(_crocCodeText.Text)) _crocCodeText.Text = CrocTransferService.GenerateCode();
                ApplyCrocState(CrocSessionState.Idle, "Rol emisor listo. Generá o compartí el código con el receptor.");
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
                _crocPathText.Text = dialog.SelectedPath;
                if (string.IsNullOrWhiteSpace(_crocCodeText.Text)) _crocCodeText.Text = CrocTransferService.GenerateCode();
                ApplyCrocState(CrocSessionState.Idle, "Rol emisor listo. Generá o compartí el código con el receptor.");
                UpdateCrocButtons();
            }
        }

        private async Task SendWithCrocAsync()
        {
            if (_crocSelectedPaths.Count == 0)
            {
                SetStatus("Elegí uno o más archivos o una carpeta para enviar.");
                return;
            }
            string code = _crocCodeText.Text.Trim();
            if (code.Length < 6)
            {
                SetStatus("El código debe tener al menos 6 caracteres.");
                return;
            }

            try
            {
                SetCrocRunning(true);
                ResetCrocProgress(true);
                _crocLogText.Clear();
                ApplyCrocState(CrocSessionState.WaitingForPeer, "Rol emisor activo. Esperando que el receptor ingrese el código.");
                AppendCrocLog("Código para el receptor: " + code);
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
            string code = _crocReceiveCodeText.Text.Trim();
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
                ApplyCrocState(CrocSessionState.WaitingForPeer, "Rol receptor activo. Esperando que el emisor inicie el envío.");
                string receiveFolder = Directory.Exists(_settings.ReceiveFolder) ? _settings.ReceiveFolder : _localCurrentPath;
                AppendCrocLog("Recibiendo en: " + receiveFolder);
                await _croc.ReceiveAsync(code, receiveFolder);
                LoadLocalPath(receiveFolder);
            }
            catch (Exception ex)
            {
                UpdateCrocButtons();
                SetStatus("No se pudo iniciar recepción simple: " + ex.Message);
            }
        }

        private void SetCrocRunning(bool running)
        {
            _crocRoleCombo.Enabled = !running;
            _crocGenerateCodeButton.Enabled = !running;
            _crocPickFileButton.Enabled = !running;
            _crocPickFolderButton.Enabled = !running;
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
            bool senderRole = IsCrocSenderRole();
            bool hasSendCode = _crocCodeText != null && _crocCodeText.Text.Trim().Length >= 6;
            bool hasReceiveCode = _crocReceiveCodeText != null && _crocReceiveCodeText.Text.Trim().Length >= 6;
            bool hasSelection = _crocSelectedPaths.Count > 0;
            _crocRoleCombo.Enabled = !running;
            _crocGenerateCodeButton.Enabled = !running;
            _crocPickFileButton.Enabled = !running;
            _crocPickFolderButton.Enabled = !running;
            _crocCodeText.ReadOnly = running;
            _crocReceiveCodeText.ReadOnly = running;
            _crocSendButton.Enabled = !running && senderRole && hasSendCode && hasSelection;
            _crocReceiveButton.Enabled = !running && !senderRole && hasReceiveCode;
            _crocCancelButton.Enabled = running;
        }

        private bool IsCrocSenderRole()
        {
            return _crocRoleCombo == null || _crocRoleCombo.SelectedIndex == 0;
        }

        private void UpdateCrocRoleVisibility()
        {
            if (_crocSendPanel == null || _crocReceivePanel == null) return;
            bool senderRole = IsCrocSenderRole();
            _crocSendPanel.Visible = senderRole;
            _crocReceivePanel.Visible = !senderRole;
            _crocSendPanel.Dock = DockStyle.Fill;
            _crocReceivePanel.Dock = DockStyle.Fill;
            ApplyCrocState(CrocSessionState.Idle, senderRole
                ? "Rol emisor seleccionado. Elegí archivo/carpeta y generá un código."
                : "Rol receptor seleccionado. Pegá el código del emisor.");
            UpdateCrocButtons();
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
                    prefix = "Enlace: conectado";
                    color = Color.DarkGreen;
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
                ? "Progreso remoto: esperando enlace con la otra PC..."
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
            _crocLogText.AppendText(line + Environment.NewLine);
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

            try
            {
                ResetProgress();
                SetStatus("Enviando...");
                await _connection.SendPathsAsync(paths, _remoteCurrentPath);
                _connection.RequestRemoteList(_remoteCurrentPath);
                SetStatus("Envío finalizado.");
            }
            catch (Exception ex)
            {
                SetStatus("Error enviando: " + ex.Message);
            }
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
            _localDragStartPoint = e.Button == MouseButtons.Left ? e.Location : Point.Empty;
        }

        private void LocalGridMouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _localDragStartPoint == Point.Empty) return;
            Rectangle dragBox = new Rectangle(
                _localDragStartPoint.X - SystemInformation.DragSize.Width / 2,
                _localDragStartPoint.Y - SystemInformation.DragSize.Height / 2,
                SystemInformation.DragSize.Width,
                SystemInformation.DragSize.Height);
            if (dragBox.Contains(e.Location)) return;

            List<string> paths = SelectedLocalPaths();
            if (paths.Count > 0)
            {
                var data = new DataObject();
                data.SetData(LocalPathsDragFormat, paths.ToArray());
                data.SetData(DataFormats.FileDrop, paths.ToArray());
                _localGrid.DoDragDrop(data, DragDropEffects.Copy);
            }
            _localDragStartPoint = Point.Empty;
        }

        private void RemoteGridMouseDown(object sender, MouseEventArgs e)
        {
            _remoteDragStartPoint = e.Button == MouseButtons.Left ? e.Location : Point.Empty;
        }

        private void RemoteGridMouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _remoteDragStartPoint == Point.Empty) return;
            Rectangle dragBox = new Rectangle(
                _remoteDragStartPoint.X - SystemInformation.DragSize.Width / 2,
                _remoteDragStartPoint.Y - SystemInformation.DragSize.Height / 2,
                SystemInformation.DragSize.Width,
                SystemInformation.DragSize.Height);
            if (dragBox.Contains(e.Location)) return;

            List<string> paths = SelectedRemotePaths();
            if (paths.Count > 0)
            {
                var data = new DataObject();
                data.SetData(RemotePathsDragFormat, paths.ToArray());
                _remoteGrid.DoDragDrop(data, DragDropEffects.Copy);
            }
            _remoteDragStartPoint = Point.Empty;
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

                _progressLabel.Text = string.Format("Total {0}: {1}% ({2}) - {3} archivos - ETA {4}",
                    type,
                    totalPercent,
                    fileCounter,
                    progress.TotalFiles,
                    remaining);
                _fileProgressLabel.Text = string.Format("Archivo actual: {0}% - {1} - {2} ({3} de {4})",
                    filePercent,
                    fileCounter,
                    progress.FileName,
                    FileSystemEntry.FormatBytes(progress.CurrentFileBytesTransferred),
                    FileSystemEntry.FormatBytes(progress.CurrentFileTotalBytes));
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
            _progressLabel.Text = "Total: preparando transferencia...";
            _fileProgressLabel.Text = "Archivo: esperando primer archivo...";
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
