using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Incantation.UI
{
    public class InputPanel : UserControl
    {
        private TextBox _inputBox;
        private Button _btnSend;
        private Button _btnStop;
        private ComboBox _cboWorkDir;
        private ComboBox _cboModel;
        private ListBox _attachList;
        private ToolStrip _inputToolbar;
        private Panel _inputBorder;
        private Panel _inputRow;

        private List<string> _contextFiles;
        private int _prevWorkDirIndex;

        // Events
        public event EventHandler SendRequested;
        public event EventHandler StopRequested;
        public event EventHandler<AttachEventArgs> FilesAttached;

        public InputPanel()
        {
            _contextFiles = new List<string>();
            _prevWorkDirIndex = 0;
            InitializeControls();
        }

        // ================================================================
        // Public properties
        // ================================================================

        public string InputText
        {
            get { return _inputBox.Text; }
            set { _inputBox.Text = value; }
        }

        public string SelectedWorkDir
        {
            get
            {
                if (_cboWorkDir.SelectedItem != null)
                {
                    string item = _cboWorkDir.SelectedItem.ToString();
                    if (item != "---" && item != "Browse...")
                    {
                        return item;
                    }
                }
                if (_cboWorkDir.Text.Length > 0
                    && _cboWorkDir.Text != "---"
                    && _cboWorkDir.Text != "Browse...")
                {
                    return _cboWorkDir.Text;
                }
                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
        }

        public string SelectedModel
        {
            get
            {
                if (_cboModel.SelectedItem != null)
                {
                    return _cboModel.SelectedItem.ToString();
                }
                return "(default)";
            }
        }

        public List<string> ContextFiles
        {
            get { return _contextFiles; }
        }

        public ComboBox ModelCombo
        {
            get { return _cboModel; }
        }

        public bool IsBusy
        {
            set
            {
                _btnSend.Visible = !value;
                _btnStop.Visible = value;
            }
        }

        // ================================================================
        // Public methods
        // ================================================================

        public void SetWorkDirText(string text)
        {
            _cboWorkDir.Text = text;
        }

        public void LoadWorkDirHistory(List<string> history)
        {
            if (history == null) return;
            for (int i = 0; i < history.Count; i++)
            {
                string path = history[i];
                if (!ContainsWorkDir(path))
                {
                    int insertIdx = _cboWorkDir.Items.Count - 2; // before --- and Browse...
                    if (insertIdx < 0) insertIdx = 0;
                    _cboWorkDir.Items.Insert(insertIdx, path);
                }
            }
        }

        public List<string> GetWorkDirHistory()
        {
            List<string> result = new List<string>();
            for (int i = 0; i < _cboWorkDir.Items.Count; i++)
            {
                string item = _cboWorkDir.Items[i].ToString();
                if (item != "---" && item != "Browse...")
                {
                    result.Add(item);
                }
            }
            return result;
        }

        public void ClearAttachments()
        {
            _contextFiles.Clear();
            _attachList.Items.Clear();
            UpdateAttachListVisibility();
        }

        public void FocusInput()
        {
            _inputBox.Focus();
        }

        // ================================================================
        // Control construction
        // ================================================================

        private void InitializeControls()
        {
            this.Dock = DockStyle.Fill;

            // --- Input toolbar strip (working dir, model, attach) ---
            _inputToolbar = new ToolStrip();
            _inputToolbar.GripStyle = ToolStripGripStyle.Hidden;
            _inputToolbar.Dock = DockStyle.Top;

            ToolStripLabel lblDir = new ToolStripLabel("Dir:");
            _inputToolbar.Items.Add(lblDir);

            _cboWorkDir = new ComboBox();
            _cboWorkDir.DropDownStyle = ComboBoxStyle.DropDown;
            _cboWorkDir.Font = new Font("Tahoma", 7.5f);
            _cboWorkDir.Width = 200;
            _cboWorkDir.FlatStyle = FlatStyle.Flat;
            _cboWorkDir.DrawMode = DrawMode.OwnerDrawFixed;
            _cboWorkDir.Items.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            _cboWorkDir.Items.Add("C:\\Projects");
            _cboWorkDir.Items.Add("C:\\");
            _cboWorkDir.Items.Add("---");
            _cboWorkDir.Items.Add("Browse...");
            _cboWorkDir.SelectedIndex = 0;
            _cboWorkDir.DrawItem += new DrawItemEventHandler(this.OnDrawWorkDirItem);
            _cboWorkDir.SelectedIndexChanged += new EventHandler(this.OnWorkDirChanged);
            ToolStripControlHost workDirHost = new ToolStripControlHost(_cboWorkDir);
            _inputToolbar.Items.Add(workDirHost);

            _inputToolbar.Items.Add(new ToolStripSeparator());

            ToolStripLabel lblModel = new ToolStripLabel("Model:");
            _inputToolbar.Items.Add(lblModel);

            _cboModel = new ComboBox();
            _cboModel.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboModel.Font = new Font("Tahoma", 7.5f);
            _cboModel.Width = 150;
            _cboModel.FlatStyle = FlatStyle.Flat;
            _cboModel.Items.Add("(default)");
            _cboModel.SelectedIndex = 0;
            ToolStripControlHost modelHost = new ToolStripControlHost(_cboModel);
            _inputToolbar.Items.Add(modelHost);

            _inputToolbar.Items.Add(new ToolStripSeparator());

            ToolStripButton tsbAttach = new ToolStripButton("Attach File...");
            tsbAttach.Click += new EventHandler(this.OnAttachClick);
            _inputToolbar.Items.Add(tsbAttach);

            // --- Attachment display list ---
            _attachList = new ListBox();
            _attachList.Dock = DockStyle.Bottom;
            _attachList.Height = 0;
            _attachList.Visible = false;
            _attachList.IntegralHeight = false;
            _attachList.Font = new Font("Tahoma", 7.5f);
            _attachList.DrawMode = DrawMode.OwnerDrawFixed;
            _attachList.ItemHeight = 20;
            _attachList.DrawItem += new DrawItemEventHandler(this.OnDrawAttachItem);

            ContextMenu attachCtx = new ContextMenu();
            attachCtx.MenuItems.Add(new MenuItem("Remove", new EventHandler(this.OnRemoveAttachment)));
            _attachList.ContextMenu = attachCtx;

            // --- Stop button (initially hidden, replaces Send during streaming) ---
            _btnStop = new Button();
            _btnStop.Text = "Stop";
            _btnStop.Dock = DockStyle.Right;
            _btnStop.Width = 60;
            _btnStop.Visible = false;
            _btnStop.FlatStyle = FlatStyle.Flat;
            _btnStop.FlatAppearance.BorderColor = Color.FromArgb(184, 48, 48);
            _btnStop.FlatAppearance.MouseOverBackColor = Color.FromArgb(252, 200, 200);
            _btnStop.FlatAppearance.MouseDownBackColor = Color.FromArgb(220, 120, 120);
            _btnStop.BackColor = Color.FromArgb(252, 238, 237);
            _btnStop.ForeColor = Color.FromArgb(184, 48, 48);
            _btnStop.Font = new Font("Tahoma", 8.25f, FontStyle.Bold);
            _btnStop.Click += new EventHandler(this.OnStopClick);

            // --- Send button (right side of input) ---
            _btnSend = new Button();
            _btnSend.Text = "Send";
            _btnSend.Dock = DockStyle.Right;
            _btnSend.Width = 60;
            _btnSend.FlatStyle = FlatStyle.Flat;
            _btnSend.FlatAppearance.BorderColor = Color.FromArgb(148, 170, 186);
            _btnSend.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 168, 124);
            _btnSend.FlatAppearance.MouseDownBackColor = Color.FromArgb(192, 96, 48);
            _btnSend.BackColor = Color.FromArgb(220, 228, 234);
            _btnSend.ForeColor = Color.FromArgb(40, 50, 60);
            _btnSend.Font = new Font("Tahoma", 8.25f, FontStyle.Bold);
            _btnSend.Click += new EventHandler(this.OnSendClick);

            // --- Input textbox with border ---
            _inputBox = new TextBox();
            _inputBox.Multiline = true;
            _inputBox.Dock = DockStyle.Fill;
            _inputBox.AcceptsTab = true;
            _inputBox.AcceptsReturn = true;
            _inputBox.ScrollBars = ScrollBars.Vertical;
            _inputBox.Font = new Font("Tahoma", 8.25f);
            _inputBox.BorderStyle = BorderStyle.None;
            _inputBox.KeyDown += new KeyEventHandler(this.OnInputKeyDown);

            _inputBorder = new Panel();
            _inputBorder.Dock = DockStyle.Fill;
            _inputBorder.Padding = new Padding(2);
            _inputBorder.BackColor = Color.FromArgb(148, 170, 186);
            _inputBorder.Controls.Add(_inputBox);

            // --- Input area with send/stop buttons beside textbox ---
            _inputRow = new Panel();
            _inputRow.Dock = DockStyle.Fill;
            _inputRow.Controls.Add(_inputBorder);      // Fill — textbox area
            _inputRow.Controls.Add(_btnStop);            // Right — stop button (hidden)
            _inputRow.Controls.Add(_btnSend);            // Right — send button

            // Assemble: toolbar on top, attachments, then input+send
            this.Controls.Add(_inputRow);        // Fill — docks last
            this.Controls.Add(_attachList);      // Bottom
            this.Controls.Add(_inputToolbar);    // Top — docks first
        }

        // ================================================================
        // Event handlers
        // ================================================================

        private void OnSendClick(object sender, EventArgs e)
        {
            if (SendRequested != null)
            {
                SendRequested(this, EventArgs.Empty);
            }
        }

        private void OnStopClick(object sender, EventArgs e)
        {
            if (StopRequested != null)
            {
                StopRequested(this, EventArgs.Empty);
            }
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (SendRequested != null)
                {
                    SendRequested(this, EventArgs.Empty);
                }
            }
        }

        private void OnAttachClick(object sender, EventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Title = "Attach File";
            dlg.Filter = "All files (*.*)|*.*|C# files (*.cs)|*.cs|Text files (*.txt)|*.txt";
            dlg.Multiselect = true;
            if (dlg.ShowDialog(this.FindForm()) == DialogResult.OK)
            {
                string[] files = dlg.FileNames;
                List<string> added = new List<string>();
                for (int i = 0; i < files.Length; i++)
                {
                    string filePath = files[i];
                    _contextFiles.Add(filePath);
                    _attachList.Items.Add(Path.GetFileName(filePath));
                    added.Add(filePath);
                }
                UpdateAttachListVisibility();

                if (FilesAttached != null && added.Count > 0)
                {
                    FilesAttached(this, new AttachEventArgs(added));
                }
            }
            dlg.Dispose();
        }

        private void OnRemoveAttachment(object sender, EventArgs e)
        {
            int idx = _attachList.SelectedIndex;
            if (idx >= 0 && idx < _contextFiles.Count)
            {
                _contextFiles.RemoveAt(idx);
                _attachList.Items.RemoveAt(idx);
                UpdateAttachListVisibility();
            }
        }

        private void OnDrawAttachItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _contextFiles.Count) return;

            e.DrawBackground();

            string filePath = _contextFiles[e.Index];
            string fileName = Path.GetFileName(filePath);

            try
            {
                Icon fileIcon = null;
                if (File.Exists(filePath))
                {
                    fileIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
                }
                if (fileIcon != null)
                {
                    e.Graphics.DrawIcon(fileIcon, new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 2, 16, 16));
                    fileIcon.Dispose();
                }
            }
            catch { }

            using (SolidBrush brush = new SolidBrush(e.ForeColor))
            {
                e.Graphics.DrawString(fileName, e.Font, brush, e.Bounds.X + 22, e.Bounds.Y + 3);
            }

            e.DrawFocusRectangle();
        }

        // ================================================================
        // Work dir dropdown — owner-drawn with separator and Browse...
        // ================================================================

        private void OnDrawWorkDirItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.DrawBackground();

            string item = _cboWorkDir.Items[e.Index].ToString();

            if (item == "---")
            {
                // Draw separator line
                int y = e.Bounds.Y + e.Bounds.Height / 2;
                using (Pen pen = new Pen(Color.Gray))
                {
                    e.Graphics.DrawLine(pen, e.Bounds.X + 2, y, e.Bounds.Right - 2, y);
                }
            }
            else
            {
                Color textColor = e.ForeColor;
                if (item == "Browse...")
                {
                    textColor = Color.FromArgb(0, 80, 160);
                }
                using (SolidBrush brush = new SolidBrush(textColor))
                {
                    e.Graphics.DrawString(item, e.Font, brush, e.Bounds.X + 2, e.Bounds.Y + 1);
                }
            }

            e.DrawFocusRectangle();
        }

        private void OnWorkDirChanged(object sender, EventArgs e)
        {
            int idx = _cboWorkDir.SelectedIndex;
            if (idx < 0) return;

            string item = _cboWorkDir.Items[idx].ToString();

            if (item == "---")
            {
                // Revert — separator is not selectable
                _cboWorkDir.SelectedIndex = _prevWorkDirIndex;
                return;
            }

            if (item == "Browse...")
            {
                // Show folder browser
                FolderBrowserDialog dlg = new FolderBrowserDialog();
                dlg.Description = "Select working directory";
                if (dlg.ShowDialog(this.FindForm()) == DialogResult.OK)
                {
                    string path = dlg.SelectedPath;
                    if (!ContainsWorkDir(path))
                    {
                        // Insert before the separator
                        int insertIdx = _cboWorkDir.Items.Count - 2;
                        if (insertIdx < 0) insertIdx = 0;
                        _cboWorkDir.Items.Insert(insertIdx, path);
                    }
                    // Select the browsed path
                    _cboWorkDir.SelectedIndexChanged -= new EventHandler(this.OnWorkDirChanged);
                    _cboWorkDir.SelectedIndex = FindWorkDirIndex(path);
                    _prevWorkDirIndex = _cboWorkDir.SelectedIndex;
                    _cboWorkDir.SelectedIndexChanged += new EventHandler(this.OnWorkDirChanged);
                }
                else
                {
                    // Cancel — revert
                    _cboWorkDir.SelectedIndexChanged -= new EventHandler(this.OnWorkDirChanged);
                    _cboWorkDir.SelectedIndex = _prevWorkDirIndex;
                    _cboWorkDir.SelectedIndexChanged += new EventHandler(this.OnWorkDirChanged);
                }
                return;
            }

            _prevWorkDirIndex = idx;
        }

        private bool ContainsWorkDir(string path)
        {
            for (int i = 0; i < _cboWorkDir.Items.Count; i++)
            {
                if (string.Compare(_cboWorkDir.Items[i].ToString(), path, true) == 0)
                {
                    return true;
                }
            }
            return false;
        }

        private int FindWorkDirIndex(string path)
        {
            for (int i = 0; i < _cboWorkDir.Items.Count; i++)
            {
                if (string.Compare(_cboWorkDir.Items[i].ToString(), path, true) == 0)
                {
                    return i;
                }
            }
            return 0;
        }

        // ================================================================
        // Helpers
        // ================================================================

        private void UpdateAttachListVisibility()
        {
            if (_contextFiles.Count > 0)
            {
                _attachList.Height = Math.Min(_contextFiles.Count * 20 + 4, 64);
                _attachList.Visible = true;
            }
            else
            {
                _attachList.Height = 0;
                _attachList.Visible = false;
            }
        }
    }

    // ================================================================
    // Event args for file attachment
    // ================================================================

    public class AttachEventArgs : EventArgs
    {
        private List<string> _files;

        public AttachEventArgs(List<string> files)
        {
            _files = files;
        }

        public List<string> Files
        {
            get { return _files; }
        }
    }
}
