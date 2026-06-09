using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FileSender.Models;

namespace FileSender
{
    internal sealed class SettingsDialog : Form
    {
        private readonly TextBox _portText;
        private readonly TextBox _keyText;
        private readonly TextBox _receiveFolderText;
        private readonly TextBox _localFolderText;

        public AppSettings Settings { get; private set; }

        public SettingsDialog(AppSettings settings)
        {
            Settings = new AppSettings
            {
                TcpPort = settings.TcpPort,
                SharedKey = settings.SharedKey,
                ReceiveFolder = settings.ReceiveFolder,
                LocalStartFolder = settings.LocalStartFolder
            };

            Text = "Configuración";
            Width = 620;
            Height = 250;
            MinimumSize = new Size(540, 230);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 5, Padding = new Padding(10) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            for (int i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Controls.Add(layout);

            _portText = new TextBox { Dock = DockStyle.Fill, Text = Settings.TcpPort.ToString() };
            _keyText = new TextBox { Dock = DockStyle.Fill, Text = Settings.SharedKey, UseSystemPasswordChar = true };
            _receiveFolderText = new TextBox { Dock = DockStyle.Fill, Text = Settings.ReceiveFolder, ReadOnly = true };
            _localFolderText = new TextBox { Dock = DockStyle.Fill, Text = Settings.LocalStartFolder, ReadOnly = true };

            AddRow(layout, 0, "Puerto TCP local", _portText, null);
            AddRow(layout, 1, "Clave compartida LAN", _keyText, null);
            AddRow(layout, 2, "Carpeta para recibidos", _receiveFolderText, (s, e) => PickFolder(_receiveFolderText));
            AddRow(layout, 3, "Carpeta local inicial", _localFolderText, (s, e) => PickFolder(_localFolderText));

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var ok = new Button { Text = "Guardar", Width = 90, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancelar", Width = 90, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => SaveAndClose();
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            layout.SetColumnSpan(buttons, 3);
            layout.Controls.Add(buttons, 0, 4);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private static void AddRow(TableLayoutPanel layout, int row, string label, Control editor, EventHandler browse)
        {
            layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            layout.Controls.Add(editor, 1, row);
            if (browse != null)
            {
                var button = new Button { Text = "Elegir", Dock = DockStyle.Fill };
                button.Click += browse;
                layout.Controls.Add(button, 2, row);
            }
        }

        private void PickFolder(TextBox target)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (Directory.Exists(target.Text)) dialog.SelectedPath = target.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
            }
        }

        private void SaveAndClose()
        {
            int port;
            if (!int.TryParse(_portText.Text.Trim(), out port) || port < 1 || port > 65535)
            {
                MessageBox.Show(this, "El puerto debe estar entre 1 y 65535.", "Configuración", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            Settings.TcpPort = port;
            Settings.SharedKey = _keyText.Text;
            Settings.ReceiveFolder = _receiveFolderText.Text;
            Settings.LocalStartFolder = _localFolderText.Text;
        }
    }
}
