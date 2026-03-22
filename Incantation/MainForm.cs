using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Incantation.Agent;
using Incantation.Chat;
using Incantation.Net;
using Incantation.UI;
using Newtonsoft.Json.Linq;

namespace Incantation
{
    public class MainForm : Form
    {
        private string _proxyAddress = "http://192.168.50.1:5000";
        private string _sessionId;
        private string _proxySessionId;  // live proxy session ID (null until first message)
        private string _pendingMessage;  // queued message waiting for proxy session creation
        private bool _merlinEnabled = true;

        private ProxyClient _proxyClient;
        private ChatHistory _chatHistory;
        private IChatRenderer _chatRenderer;
        private MerlinHelper _merlin;
        private System.Diagnostics.Process _toolServerProcess;

        // Layout: outer[left | inner[center | right]]
        private SplitContainer _outerSplit;
        private SplitContainer _innerSplit;
        private SplitContainer _chatSplit;

        // Toolbar
        private ToolStrip _toolStrip;

        // Left sidebar — Sessions
        private Panel _leftPanel;
        private Panel _hdrSessions;
        private ListBox _sessionList;

        // Center — Chat
        private ChatPanel _chatBox;
        private TextBox _inputBox;
        private Panel _inputPanel;
        private Button _btnSend;
        private ComboBox _cboWorkDir;
        private ComboBox _cboModel;
        private ListBox _attachList;

        // Right sidebar — Tasks / Output / Context (stacked)
        private Panel _rightPanel;
        private SplitContainer _rightSplit1;
        private SplitContainer _rightSplit2;
        private Panel _hdrTasks;
        private CheckedListBox _tasksList;
        private Panel _hdrOutput;
        private ListBox _outputList;
        private Panel _hdrContext;
        private ListBox _contextList;

        // Menu and status
        private MenuStrip _menuStrip;
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusConnection;
        private ToolStripStatusLabel _statusSession;
        private ToolStripStatusLabel _statusState;

        // Input border
        private Panel _inputBorder;

        // Workers
        private BackgroundWorker _messageWorker;
        private BackgroundWorker _sessionWorker;

        // Session list hover
        private int _sessionHotIndex = -1;

        // State
        private List<string> _sessionIds;
        private Dictionary<string, string> _sessionTitles;
        private Dictionary<string, int> _toolCallIndex;
        private List<string> _contextFiles;
        private List<string> _outputFiles;

        // Persistence
        private SessionStore _sessionStore;
        private SessionData _currentSessionData;
        private string _assistantBuffer;
        private string _reasoningBuffer;

        public MainForm()
        {
            _sessionIds = new List<string>();
            _sessionTitles = new Dictionary<string, string>();
            _toolCallIndex = new Dictionary<string, int>();
            _contextFiles = new List<string>();
            _outputFiles = new List<string>();
            _assistantBuffer = "";
            _reasoningBuffer = "";

            InitializeComponents();
            _chatHistory = new ChatHistory();
            _chatRenderer = _chatBox;
            ToolStripManager.Renderer = new ToolStripProfessionalRenderer(new SlateCopperColorTable());
            _proxyClient = new ProxyClient(_proxyAddress);
            _sessionStore = new SessionStore(Application.StartupPath);
        }

        private void InitializeComponents()
        {
            this.Text = "Incantation - AI Coding Assistant";
            this.Size = new Size(1024, 768);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Tahoma", 8.25f);

            // ============================================================
            // Main menu (MenuStrip)
            // ============================================================
            _menuStrip = new MenuStrip();

            ToolStripMenuItem fileMenu = new ToolStripMenuItem("File");
            fileMenu.DropDownItems.Add(new ToolStripMenuItem("New Session", null, new EventHandler(this.OnNewSession)));
            fileMenu.DropDownItems.Add(new ToolStripMenuItem("Attach File...", null, new EventHandler(this.OnAttachClick)));
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(new ToolStripMenuItem("Exit", null, new EventHandler(this.OnExit)));

            ToolStripMenuItem editMenu = new ToolStripMenuItem("Edit");
            editMenu.DropDownItems.Add(new ToolStripMenuItem("Copy", null, new EventHandler(this.OnCopy)));
            editMenu.DropDownItems.Add(new ToolStripMenuItem("Clear History", null, new EventHandler(this.OnClearHistory)));

            ToolStripMenuItem viewMenu = new ToolStripMenuItem("View");
            viewMenu.DropDownItems.Add(new ToolStripMenuItem("Sessions Panel", null, new EventHandler(this.OnToggleSessionsPanel)));
            viewMenu.DropDownItems.Add(new ToolStripMenuItem("Details Panel", null, new EventHandler(this.OnToggleDetailsPanel)));

            ToolStripMenuItem toolsMenu = new ToolStripMenuItem("Tools");
            toolsMenu.DropDownItems.Add(new ToolStripMenuItem("Settings", null, new EventHandler(this.OnSettings)));
            toolsMenu.DropDownItems.Add(new ToolStripSeparator());
            toolsMenu.DropDownItems.Add(new ToolStripMenuItem("Merlin On/Off", null, new EventHandler(this.OnToggleMerlin)));

            ToolStripMenuItem helpMenu = new ToolStripMenuItem("Help");
            helpMenu.DropDownItems.Add(new ToolStripMenuItem("About", null, new EventHandler(this.OnAbout)));

            _menuStrip.Items.Add(fileMenu);
            _menuStrip.Items.Add(editMenu);
            _menuStrip.Items.Add(viewMenu);
            _menuStrip.Items.Add(toolsMenu);
            _menuStrip.Items.Add(helpMenu);
            this.MainMenuStrip = _menuStrip;

            // ============================================================
            // ToolStrip
            // ============================================================
            _toolStrip = new ToolStrip();
            _toolStrip.GripStyle = ToolStripGripStyle.Hidden;
            _toolStrip.Items.Add(new ToolStripButton("New Session", null, new EventHandler(this.OnNewSession)));
            _toolStrip.Items.Add(new ToolStripSeparator());
            _toolStrip.Items.Add(new ToolStripButton("Settings", null, new EventHandler(this.OnSettings)));

            // ============================================================
            // Status bar (StatusStrip)
            // ============================================================
            _statusStrip = new StatusStrip();

            _statusConnection = new ToolStripStatusLabel();
            _statusConnection.Text = "Disconnected";
            _statusConnection.Width = 150;
            _statusConnection.AutoSize = false;

            _statusSession = new ToolStripStatusLabel();
            _statusSession.Text = "No Session";
            _statusSession.Width = 250;
            _statusSession.AutoSize = false;

            _statusState = new ToolStripStatusLabel();
            _statusState.Text = "Ready";
            _statusState.Spring = true;

            _statusStrip.Items.Add(_statusConnection);
            _statusStrip.Items.Add(_statusSession);
            _statusStrip.Items.Add(_statusState);

            // ============================================================
            // Left sidebar — Sessions
            // ============================================================
            _leftPanel = new Panel();
            _leftPanel.Dock = DockStyle.Fill;
            _leftPanel.BackColor = GradientHeader.SidebarBg;

            _hdrSessions = new Panel();
            _hdrSessions.Dock = DockStyle.Top;
            _hdrSessions.Height = 22;
            _hdrSessions.Paint += new PaintEventHandler(this.OnPaintSessionsHeader);

            _sessionList = new ListBox();
            _sessionList.Dock = DockStyle.Fill;
            _sessionList.IntegralHeight = false;
            _sessionList.DrawMode = DrawMode.OwnerDrawFixed;
            _sessionList.ItemHeight = 40;
            _sessionList.BorderStyle = BorderStyle.None;
            _sessionList.BackColor = GradientHeader.SidebarBg;
            _sessionList.SelectedIndexChanged += new EventHandler(this.OnSessionListSelected);
            _sessionList.DrawItem += new DrawItemEventHandler(this.OnDrawSessionItem);
            _sessionList.MouseDown += new MouseEventHandler(this.OnSessionListMouseDown);
            _sessionList.MouseMove += new MouseEventHandler(this.OnSessionListMouseMove);
            _sessionList.MouseLeave += new EventHandler(this.OnSessionListMouseLeave);

            ContextMenu sessionCtx = new ContextMenu();
            sessionCtx.MenuItems.Add(new MenuItem("Rename...", new EventHandler(this.OnRenameSession)));
            sessionCtx.MenuItems.Add(new MenuItem("-"));
            sessionCtx.MenuItems.Add(new MenuItem("Delete", new EventHandler(this.OnDeleteSession)));
            _sessionList.ContextMenu = sessionCtx;

            // Fill first (docks last), then edge controls (dock first)
            _leftPanel.Controls.Add(_sessionList);     // Fill — index 0, docks last
            _leftPanel.Controls.Add(_hdrSessions);     // Top — index 1, docks first

            // ============================================================
            // Right sidebar — Tasks / Output / Context (stacked)
            // ============================================================
            _rightPanel = new Panel();
            _rightPanel.Dock = DockStyle.Fill;
            _rightPanel.BackColor = GradientHeader.SidebarBg;

            // Tasks section
            Panel tasksPanel = new Panel();
            tasksPanel.Dock = DockStyle.Fill;

            _hdrTasks = new Panel();
            _hdrTasks.Tag = "Tasks";
            _hdrTasks.Dock = DockStyle.Top;
            _hdrTasks.Height = 22;
            _hdrTasks.Paint += new PaintEventHandler(this.OnPaintSectionHeader);

            _tasksList = new CheckedListBox();
            _tasksList.Dock = DockStyle.Fill;
            _tasksList.IntegralHeight = false;
            _tasksList.CheckOnClick = false;
            _tasksList.BackColor = GradientHeader.SidebarBg;
            _tasksList.BorderStyle = BorderStyle.None;

            tasksPanel.Controls.Add(_tasksList);      // Fill — index 0, docks last
            tasksPanel.Controls.Add(_hdrTasks);       // Top — index 1, docks first

            // Output section
            Panel outputPanel = new Panel();
            outputPanel.Dock = DockStyle.Fill;

            _hdrOutput = new Panel();
            _hdrOutput.Tag = "Output";
            _hdrOutput.Dock = DockStyle.Top;
            _hdrOutput.Height = 22;
            _hdrOutput.Paint += new PaintEventHandler(this.OnPaintSectionHeader);

            _outputList = new ListBox();
            _outputList.Dock = DockStyle.Fill;
            _outputList.IntegralHeight = false;
            _outputList.DrawMode = DrawMode.OwnerDrawFixed;
            _outputList.ItemHeight = 20;
            _outputList.DoubleClick += new EventHandler(this.OnOutputDoubleClick);
            _outputList.DrawItem += new DrawItemEventHandler(this.OnDrawOutputItem);
            _outputList.BackColor = GradientHeader.SidebarBg;
            _outputList.BorderStyle = BorderStyle.None;

            outputPanel.Controls.Add(_outputList);    // Fill — index 0, docks last
            outputPanel.Controls.Add(_hdrOutput);     // Top — index 1, docks first

            // Context section
            Panel contextPanel = new Panel();
            contextPanel.Dock = DockStyle.Fill;

            _hdrContext = new Panel();
            _hdrContext.Tag = "Context";
            _hdrContext.Dock = DockStyle.Top;
            _hdrContext.Height = 22;
            _hdrContext.Paint += new PaintEventHandler(this.OnPaintSectionHeader);

            _contextList = new ListBox();
            _contextList.Dock = DockStyle.Fill;
            _contextList.IntegralHeight = false;
            _contextList.BackColor = GradientHeader.SidebarBg;
            _contextList.BorderStyle = BorderStyle.None;

            ContextMenu ctxMenu = new ContextMenu();
            ctxMenu.MenuItems.Add(new MenuItem("Remove", new EventHandler(this.OnRemoveContext)));
            _contextList.ContextMenu = ctxMenu;

            contextPanel.Controls.Add(_contextList);  // Fill — index 0, docks last
            contextPanel.Controls.Add(_hdrContext);    // Top — index 1, docks first

            // Stack: _rightSplit2 = Output | Context
            _rightSplit2 = new SplitContainer();
            _rightSplit2.Dock = DockStyle.Fill;
            _rightSplit2.Orientation = Orientation.Horizontal;
            _rightSplit2.SplitterDistance = 100;
            _rightSplit2.Panel1MinSize = 60;
            _rightSplit2.Panel2MinSize = 60;
            _rightSplit2.Panel1.Controls.Add(outputPanel);
            _rightSplit2.Panel2.Controls.Add(contextPanel);

            // Stack: _rightSplit1 = Tasks | (_rightSplit2)
            _rightSplit1 = new SplitContainer();
            _rightSplit1.Dock = DockStyle.Fill;
            _rightSplit1.Orientation = Orientation.Horizontal;
            _rightSplit1.SplitterDistance = 140;
            _rightSplit1.Panel1MinSize = 60;
            _rightSplit1.Panel2MinSize = 120;
            _rightSplit1.Panel1.Controls.Add(tasksPanel);
            _rightSplit1.Panel2.Controls.Add(_rightSplit2);

            _rightPanel.Controls.Add(_rightSplit1);

            // ============================================================
            // Center — Chat area
            // ============================================================
            _chatBox = new ChatPanel();
            _chatBox.Dock = DockStyle.Fill;

            _inputPanel = new Panel();
            _inputPanel.Dock = DockStyle.Fill;

            // --- Input toolbar strip (working dir, model, attach) ---
            ToolStrip _inputToolbar = new ToolStrip();
            _inputToolbar.GripStyle = ToolStripGripStyle.Hidden;
            _inputToolbar.Dock = DockStyle.Top;

            ToolStripLabel lblDir = new ToolStripLabel("Dir:");
            _inputToolbar.Items.Add(lblDir);

            _cboWorkDir = new ComboBox();
            _cboWorkDir.DropDownStyle = ComboBoxStyle.DropDown;
            _cboWorkDir.Font = new Font("Tahoma", 7.5f);
            _cboWorkDir.Width = 200;
            _cboWorkDir.FlatStyle = FlatStyle.Flat;
            _cboWorkDir.Items.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            _cboWorkDir.Items.Add(@"C:\Projects");
            _cboWorkDir.Items.Add(@"C:\");
            _cboWorkDir.SelectedIndex = 0;
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
            _inputBox.ScrollBars = ScrollBars.Vertical;
            _inputBox.Font = new Font("Tahoma", 8.25f);
            _inputBox.BorderStyle = BorderStyle.None;
            _inputBox.KeyDown += new KeyEventHandler(this.OnInputKeyDown);

            _inputBorder = new Panel();
            _inputBorder.Dock = DockStyle.Fill;
            _inputBorder.Padding = new Padding(2);
            _inputBorder.BackColor = Color.FromArgb(148, 170, 186);
            _inputBorder.Controls.Add(_inputBox);

            // --- Input area with send button beside textbox ---
            Panel _inputRow = new Panel();
            _inputRow.Dock = DockStyle.Fill;
            _inputRow.Controls.Add(_inputBorder);      // Fill — textbox area
            _inputRow.Controls.Add(_btnSend);           // Right — send button

            // Assemble: toolbar on top, attachments, then input+send
            _inputPanel.Controls.Add(_inputRow);        // Fill — docks last
            _inputPanel.Controls.Add(_attachList);      // Bottom
            _inputPanel.Controls.Add(_inputToolbar);    // Top — docks first

            // ============================================================
            // Assemble layout
            // ============================================================

            _chatSplit = new SplitContainer();
            _chatSplit.Dock = DockStyle.Fill;
            _chatSplit.Orientation = Orientation.Horizontal;
            _chatSplit.Panel2MinSize = 120;
            _chatSplit.FixedPanel = FixedPanel.Panel2;
            _chatSplit.Panel1.Controls.Add(_chatBox);
            _chatSplit.Panel2.Controls.Add(_inputPanel);

            _innerSplit = new SplitContainer();
            _innerSplit.Dock = DockStyle.Fill;
            _innerSplit.Orientation = Orientation.Vertical;
            _innerSplit.Panel2MinSize = 140;
            _innerSplit.FixedPanel = FixedPanel.Panel2;
            _innerSplit.Panel1.Controls.Add(_chatSplit);
            _innerSplit.Panel2.Controls.Add(_rightPanel);

            _outerSplit = new SplitContainer();
            _outerSplit.Dock = DockStyle.Fill;
            _outerSplit.Orientation = Orientation.Vertical;
            _outerSplit.SplitterDistance = 120;
            _outerSplit.Panel1MinSize = 100;
            _outerSplit.FixedPanel = FixedPanel.Panel1;
            _outerSplit.Panel1.Controls.Add(_leftPanel);
            _outerSplit.Panel2.Controls.Add(_innerSplit);

            // CRITICAL: Add order determines dock priority in WinForms.
            // Last added = docked first. Fill must be added FIRST so it
            // docks LAST (takes remaining space after edge controls).
            this.Controls.Add(_outerSplit);   // Fill — index 0, docks last
            this.Controls.Add(_statusStrip);  // Bottom — docks before Fill
            this.Controls.Add(_toolStrip);    // Top — docks before Fill
            this.Controls.Add(_menuStrip);    // Top — above toolbar (docked first)

            // ============================================================
            // Background workers
            // ============================================================
            _messageWorker = new BackgroundWorker();
            _messageWorker.WorkerReportsProgress = true;
            _messageWorker.WorkerSupportsCancellation = true;
            _messageWorker.DoWork += new DoWorkEventHandler(this.OnMessageDoWork);
            _messageWorker.ProgressChanged += new ProgressChangedEventHandler(this.OnMessageProgress);
            _messageWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(this.OnMessageCompleted);

            _sessionWorker = new BackgroundWorker();
            _sessionWorker.DoWork += new DoWorkEventHandler(this.OnSessionDoWork);
            _sessionWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(this.OnSessionCompleted);

            this.Load += new EventHandler(this.OnFormLoad);
            this.FormClosing += new FormClosingEventHandler(this.OnFormClosing);
        }

        // ====================================================================
        // Gradient header paint handlers
        // ====================================================================

        private void OnPaintSessionsHeader(object sender, PaintEventArgs e)
        {
            Panel p = (Panel)sender;
            GradientHeader.PaintHeader(e.Graphics, new Rectangle(0, 0, p.Width, p.Height), "Sessions");
        }

        private void OnPaintSectionHeader(object sender, PaintEventArgs e)
        {
            Panel p = (Panel)sender;
            string text = p.Tag as string;
            if (text == null) text = "";
            GradientHeader.PaintHeader(e.Graphics, new Rectangle(0, 0, p.Width, p.Height), text);
        }

        // ====================================================================
        // Owner-drawn session list
        // ====================================================================

        private void OnDrawSessionItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _sessionIds.Count) return;

            string sid = _sessionIds[e.Index];
            string title = "Untitled";
            string timeText = "";

            if (_sessionTitles.ContainsKey(sid))
            {
                title = _sessionTitles[sid];
            }
            else
            {
                title = TruncateId(sid);
            }

            // Get time from persisted session data if available
            SessionData sd = _sessionStore.Load(sid);
            if (sd != null)
            {
                title = sd.DisplayTitle;
                timeText = sd.DisplayTime;
            }

            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool active = sid == _sessionId;
            bool hover = (e.Index == _sessionHotIndex && !active);

            GradientHeader.PaintSessionItem(e.Graphics, e.Bounds, title, timeText, selected || hover, active);
        }

        // ====================================================================
        // Form lifecycle
        // ====================================================================

        private void OnFormLoad(object sender, EventArgs e)
        {
            // Auto-launch ToolServer if not already running
            StartToolServer();

            // Set splitter distances now that controls have their actual sizes
            // (setting during construction causes clamping on small initial sizes)
            if (_innerSplit.Width > 200)
            {
                _innerSplit.SplitterDistance = _innerSplit.Width - 180;
            }
            if (_chatSplit.Height > 200)
            {
                _chatSplit.SplitterDistance = _chatSplit.Height - 140;
            }

            _merlin = new MerlinHelper();
            try
            {
                if (_merlin.Initialize())
                {
                    _merlin.Show();
                    _merlin.MoveNearForm(this);
                    _merlin.AnimateGreet();
                }
            }
            catch
            {
            }

            _chatRenderer.AppendSystemMessage("Welcome to Incantation - AI Coding Assistant");
            _chatRenderer.ScrollToEnd();

            // Load persisted sessions into sidebar
            LoadPersistedSessions();

            // Create a new proxy session
            _chatRenderer.AppendSystemMessage(string.Format("Connecting to proxy at {0}...", _proxyAddress));
            _chatRenderer.ScrollToEnd();
            _sessionWorker.RunWorkerAsync(GetSelectedWorkDir());
        }

        private void LoadPersistedSessions()
        {
            // Suppress SelectedIndexChanged during rebuild to prevent side effects
            _sessionList.SelectedIndexChanged -= new EventHandler(this.OnSessionListSelected);

            List<SessionData> saved = _sessionStore.ListAll();
            _sessionList.Items.Clear();
            _sessionIds.Clear();

            int selectIdx = -1;
            for (int i = 0; i < saved.Count; i++)
            {
                SessionData sd = saved[i];
                _sessionIds.Add(sd.SessionId);
                _sessionTitles[sd.SessionId] = sd.DisplayTitle;
                string display = string.Format("{0}  ({1})", sd.DisplayTitle, sd.DisplayTime);
                _sessionList.Items.Add(display);

                if (sd.SessionId == _sessionId)
                {
                    selectIdx = i;
                }
            }

            // Re-select the active session
            if (selectIdx >= 0)
            {
                _sessionList.SelectedIndex = selectIdx;
            }

            _sessionList.SelectedIndexChanged += new EventHandler(this.OnSessionListSelected);
        }

        private void OnSessionListMouseDown(object sender, MouseEventArgs e)
        {
            // Select the item under the right-click so context menu operates on the right session
            if (e.Button == MouseButtons.Right)
            {
                int idx = _sessionList.IndexFromPoint(e.Location);
                if (idx >= 0)
                {
                    _sessionList.SelectedIndex = idx;
                }
            }
        }

        private void OnSessionListMouseMove(object sender, MouseEventArgs e)
        {
            int idx = _sessionList.IndexFromPoint(e.Location);
            if (idx != _sessionHotIndex)
            {
                _sessionHotIndex = idx;
                _sessionList.Invalidate();
            }
        }

        private void OnSessionListMouseLeave(object sender, EventArgs e)
        {
            if (_sessionHotIndex >= 0)
            {
                _sessionHotIndex = -1;
                _sessionList.Invalidate();
            }
        }

        private void OnRenameSession(object sender, EventArgs e)
        {
            int idx = _sessionList.SelectedIndex;
            if (idx < 0 || idx >= _sessionIds.Count) return;

            string sid = _sessionIds[idx];
            string currentTitle = _sessionTitles.ContainsKey(sid) ? _sessionTitles[sid] : "";
            string newTitle = ShowInputDialog("Rename session:", currentTitle);

            if (newTitle != null && newTitle.Length > 0)
            {
                _sessionTitles[sid] = newTitle;
                SessionData sd = _sessionStore.Load(sid);
                if (sd != null)
                {
                    sd.Title = newTitle;
                    _sessionStore.Save(sd);
                }
                if (sid == _sessionId)
                {
                    if (_currentSessionData != null) _currentSessionData.Title = newTitle;
                    _statusSession.Text = newTitle;
                }
                LoadPersistedSessions();
            }
        }

        private void OnDeleteSession(object sender, EventArgs e)
        {
            int idx = _sessionList.SelectedIndex;
            if (idx < 0 || idx >= _sessionIds.Count) return;

            string sid = _sessionIds[idx];
            string title = _sessionTitles.ContainsKey(sid) ? _sessionTitles[sid] : TruncateId(sid);

            DialogResult result = MessageBox.Show(
                string.Format("Delete session \"{0}\"?", title),
                "Delete Session",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                _sessionStore.Delete(sid);
                _sessionTitles.Remove(sid);

                // If deleting the active session, clear the UI
                if (sid == _sessionId)
                {
                    _sessionId = null;
                    _proxySessionId = null;
                    _currentSessionData = null;
                    _chatRenderer.Clear();
                    _chatHistory.Clear();
                    _tasksList.Items.Clear();
                    _outputList.Items.Clear();
                    _outputFiles.Clear();
                    _toolCallIndex.Clear();
                    _statusSession.Text = "No Session";
                }

                LoadPersistedSessions();
            }
        }

        private void StartToolServer()
        {
            try
            {
                // Check if ToolServer.exe exists next to Incantation.exe
                string appDir = Application.StartupPath;
                string toolServerPath = System.IO.Path.Combine(appDir, "Incantation.ToolServer.exe");
                if (!System.IO.File.Exists(toolServerPath))
                {
                    // Try one level up (bin\Debug layout)
                    string parentDir = System.IO.Path.GetDirectoryName(appDir);
                    if (parentDir != null)
                    {
                        string altPath = System.IO.Path.Combine(parentDir, "Incantation.ToolServer.exe");
                        if (System.IO.File.Exists(altPath))
                        {
                            toolServerPath = altPath;
                        }
                    }
                }

                if (!System.IO.File.Exists(toolServerPath))
                {
                    // Try the ToolServer project output
                    string projPath = System.IO.Path.Combine(
                        System.IO.Path.Combine(
                            System.IO.Path.Combine(
                                System.IO.Path.Combine(
                                    System.IO.Path.Combine(
                                        System.IO.Path.Combine(appDir, ".."), ".."), ".."),
                                "Incantation.ToolServer"), "bin"),
                        System.IO.Path.Combine("Debug", "Incantation.ToolServer.exe"));
                    if (System.IO.File.Exists(projPath))
                    {
                        toolServerPath = projPath;
                    }
                }

                if (System.IO.File.Exists(toolServerPath))
                {
                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
                    psi.FileName = toolServerPath;
                    psi.Arguments = "8888";
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    _toolServerProcess = System.Diagnostics.Process.Start(psi);
                    _chatRenderer.AppendSystemMessage("Tool Server started on port 8888.");
                }
                else
                {
                    _chatRenderer.AppendError(string.Format("Tool Server not found at: {0}", toolServerPath));
                }
            }
            catch (Exception ex)
            {
                _chatRenderer.AppendError(string.Format("Failed to start Tool Server: {0}", ex.Message));
                // ToolServer launch failed — tools will fail but chat still works
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            // Save current session
            if (_currentSessionData != null)
            {
                _sessionStore.Save(_currentSessionData);
            }
            if (_messageWorker.IsBusy)
            {
                _messageWorker.CancelAsync();
            }
            if (_merlin != null)
            {
                _merlin.Dispose();
                _merlin = null;
            }
            // Stop ToolServer
            if (_toolServerProcess != null && !_toolServerProcess.HasExited)
            {
                try { _toolServerProcess.Kill(); }
                catch { }
            }
        }

        // ====================================================================
        // Session creation
        // ====================================================================

        private void OnSessionDoWork(object sender, DoWorkEventArgs e)
        {
            bool healthy = _proxyClient.CheckHealth();
            if (!healthy)
            {
                e.Result = null;
                return;
            }
            string workDir = e.Argument as string;
            if (workDir == null || workDir.Length == 0)
            {
                workDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            e.Result = _proxyClient.CreateSession(workDir);
        }

        private void OnSessionCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                _statusConnection.Text = "Error";
                _statusState.Text = "Ready";
                _chatRenderer.AppendError(string.Format("Connection failed: {0}", e.Error.Message));
                _chatRenderer.ScrollToEnd();
                if (_merlin != null && _merlin.IsAvailable) _merlin.AnimateSad();
                return;
            }

            string sid = e.Result as string;
            if (sid == null)
            {
                _statusConnection.Text = "Disconnected";
                _statusState.Text = "Ready";
                _chatRenderer.AppendError("Could not connect to proxy. Is it running?");
                _chatRenderer.ScrollToEnd();
                if (_merlin != null && _merlin.IsAvailable) _merlin.AnimateSad();
                return;
            }

            _proxySessionId = sid;
            _statusConnection.Text = "Connected";
            _statusState.Text = "Ready";

            // Only set _sessionId if this is a new session
            // (not a lazy proxy creation for a resumed session)
            if (_sessionId == null)
            {
                _sessionId = sid;
                _statusSession.Text = string.Format("Session: {0}", TruncateId(sid));

                _tasksList.Items.Clear();
                _outputList.Items.Clear();
                _outputFiles.Clear();
                _toolCallIndex.Clear();

                // Don't create persisted session yet — defer to first message
                _chatRenderer.AppendSystemMessage("Session created. You can start chatting.");
                _chatRenderer.ScrollToEnd();

                FetchModels();
            }
            else
            {
                _chatRenderer.ScrollToEnd();
            }

            if (_merlin != null && _merlin.IsAvailable) _merlin.AnimateIdle();

            // Send pending message if one was queued
            if (_pendingMessage != null)
            {
                string msg = _pendingMessage;
                _pendingMessage = null;
                _messageWorker.RunWorkerAsync(msg);
            }
        }

        // ====================================================================
        // Session list
        // ====================================================================

        private void RefreshSessionList()
        {
            LoadPersistedSessions();
        }

        private void OnSessionListSelected(object sender, EventArgs e)
        {
            int idx = _sessionList.SelectedIndex;
            if (idx < 0 || idx >= _sessionIds.Count) return;

            string selectedId = _sessionIds[idx];
            if (selectedId == _sessionId) return;

            if (_messageWorker.IsBusy)
            {
                _chatRenderer.AppendError("Cannot switch while a response is in progress.");
                _chatRenderer.ScrollToEnd();
                return;
            }

            // Save current session before switching
            if (_currentSessionData != null)
            {
                _sessionStore.Save(_currentSessionData);
            }

            // Load selected session
            SessionData loaded = _sessionStore.Load(selectedId);
            if (loaded == null)
            {
                _chatRenderer.AppendError("Could not load session data.");
                _chatRenderer.ScrollToEnd();
                return;
            }

            _sessionId = selectedId;
            _currentSessionData = loaded;
            _chatRenderer.Clear();
            _chatHistory.Clear();
            _tasksList.Items.Clear();
            _outputList.Items.Clear();
            _outputFiles.Clear();
            _contextList.Items.Clear();
            _contextFiles.Clear();
            _toolCallIndex.Clear();

            // Replay messages into chat renderer
            List<ChatMessage> msgs = loaded.Messages;
            for (int i = 0; i < msgs.Count; i++)
            {
                ChatMessage msg = msgs[i];
                if (msg.Role == "user")
                {
                    _chatRenderer.AppendUserMessage("You", msg.Timestamp, msg.Content);
                }
                else if (msg.Role == "assistant")
                {
                    if (msg.Type == "reasoning")
                    {
                        // Peek ahead: if next message is assistant text, create header first
                        if (_assistantBuffer.Length == 0)
                        {
                            // Find the timestamp from the next assistant text message
                            DateTime reasoningTime = msg.Timestamp;
                            for (int j = i + 1; j < msgs.Count; j++)
                            {
                                if (msgs[j].Role == "assistant" && msgs[j].Type != "reasoning")
                                {
                                    reasoningTime = msgs[j].Timestamp;
                                    break;
                                }
                            }
                            _chatRenderer.AppendAssistantHeader("Assistant", reasoningTime);
                            _assistantBuffer = " "; // mark that header was created
                        }
                        _chatRenderer.AppendReasoning(msg.Content);
                        _chatRenderer.EndReasoning();
                    }
                    else
                    {
                        // Only create header if not already created by reasoning
                        if (_assistantBuffer.Length == 0)
                        {
                            _chatRenderer.AppendAssistantHeader("Assistant", msg.Timestamp);
                        }
                        _assistantBuffer = "";
                        _chatRenderer.AppendDelta(msg.Content);
                        _chatRenderer.FinalizeMessage();
                    }
                }
                else if (msg.Role == "tool")
                {
                    _chatRenderer.AppendToolCall(msg.Content, msg.Detail);
                }
                else if (msg.Role == "system")
                {
                    if (msg.Type == "intent")
                    {
                        _tasksList.Items.Add(msg.Content);
                        int taskIdx = _tasksList.Items.Count - 1;
                        if (msg.Completed)
                        {
                            _tasksList.SetItemChecked(taskIdx, true);
                        }
                    }
                    else if (msg.Type == "error")
                    {
                        _chatRenderer.AppendError(msg.Content);
                    }
                    else
                    {
                        _chatRenderer.AppendSystemMessage(msg.Content);
                    }
                }
            }

            // Restore sidebar state
            if (loaded.ContextFiles != null)
            {
                _contextFiles = new List<string>(loaded.ContextFiles);
                _contextList.Items.Clear();
                for (int i = 0; i < _contextFiles.Count; i++)
                {
                    _contextList.Items.Add(System.IO.Path.GetFileName(_contextFiles[i]));
                }
            }
            if (loaded.OutputFiles != null)
            {
                _outputFiles = new List<string>(loaded.OutputFiles);
                _outputList.Items.Clear();
                for (int i = 0; i < _outputFiles.Count; i++)
                {
                    _outputList.Items.Add(_outputFiles[i]);
                }
            }

            string title = loaded.DisplayTitle;
            _statusSession.Text = title;
            _chatRenderer.ScrollToEnd();

            // Don't create a proxy session now -- it will be created lazily on first message
            _proxySessionId = null;

            _sessionList.Invalidate();  // Repaint to update active indicator
        }

        // ====================================================================
        // Send message
        // ====================================================================

        private void SendMessage()
        {
            string text = _inputBox.Text.Trim();
            if (text.Length == 0 && _contextFiles.Count == 0) return;
            if (_sessionId == null)
            {
                _chatRenderer.AppendError("No active session. Try File > New Session.");
                _chatRenderer.ScrollToEnd();
                return;
            }
            if (_messageWorker.IsBusy)
            {
                _chatRenderer.AppendError("Please wait for the current response to complete.");
                _chatRenderer.ScrollToEnd();
                return;
            }

            // Build prompt with attached file contents
            string prompt = "";
            for (int i = 0; i < _contextFiles.Count; i++)
            {
                try
                {
                    string content = System.IO.File.ReadAllText(_contextFiles[i]);
                    string name = System.IO.Path.GetFileName(_contextFiles[i]);
                    prompt += string.Format("[Attached file: {0}]\n```\n{1}\n```\n\n", name, content);
                }
                catch
                {
                    // Skip unreadable files
                }
            }
            prompt += text;

            _inputBox.Text = "";

            // Capture attached files before clearing
            List<string> attachedFiles = new List<string>();
            if (_contextFiles.Count > 0)
            {
                for (int i = 0; i < _contextFiles.Count; i++)
                {
                    attachedFiles.Add(_contextFiles[i]);
                }
                _contextFiles.Clear();
                _attachList.Items.Clear();
                UpdateAttachListVisibility();
            }

            // Create persisted session on first message (deferred from OnSessionCompleted)
            if (_currentSessionData == null && _sessionId != null)
            {
                _currentSessionData = new SessionData(_sessionId);
                string autoTitle = text;
                if (autoTitle.Length > 50)
                {
                    autoTitle = autoTitle.Substring(0, 47) + "...";
                }
                _currentSessionData.Title = autoTitle;
                _sessionTitles[_sessionId] = autoTitle;
                _statusSession.Text = autoTitle;
                _sessionStore.Save(_currentSessionData);
                LoadPersistedSessions();
            }
            else if (_currentSessionData != null && (_currentSessionData.Title == null || _currentSessionData.Title.Length == 0))
            {
                // Auto-title from first message if session has no title
                string autoTitle = text;
                if (autoTitle.Length > 50)
                {
                    autoTitle = autoTitle.Substring(0, 47) + "...";
                }
                _currentSessionData.Title = autoTitle;
                _sessionTitles[_sessionId] = autoTitle;
                _statusSession.Text = autoTitle;
                _sessionStore.Save(_currentSessionData);
                LoadPersistedSessions();
            }

            DateTime now = DateTime.Now;
            ChatMessage userMsg = new ChatMessage("user", text, now);
            _chatHistory.Add(userMsg);
            if (_currentSessionData != null)
            {
                _currentSessionData.AddMessage(userMsg);
            }
            _assistantBuffer = "";
            _reasoningBuffer = "";
            _chatRenderer.AppendUserMessage("You", now, text);
            for (int i = 0; i < attachedFiles.Count; i++)
            {
                _chatRenderer.AppendFileArtifact(attachedFiles[i]);
            }
            _chatRenderer.AppendAssistantHeader("Assistant", now);
            _chatRenderer.ScrollToEnd();

            _statusState.Text = "Thinking...";
            if (_merlin != null && _merlin.IsAvailable && _merlinEnabled)
            {
                _merlin.AnimateThinking();
            }

            if (_proxySessionId == null)
            {
                // Need to create a proxy session first
                _statusState.Text = "Creating session...";
                _pendingMessage = prompt;
                if (!_sessionWorker.IsBusy)
                {
                    _sessionWorker.RunWorkerAsync(GetSelectedWorkDir());
                }
                return;
            }

            _messageWorker.RunWorkerAsync(prompt);
        }

        private void OnMessageDoWork(object sender, DoWorkEventArgs e)
        {
            string prompt = (string)e.Argument;
            BackgroundWorker worker = (BackgroundWorker)sender;
            try
            {
                _proxyClient.SendMessage(_proxySessionId, prompt, worker);
            }
            catch (Exception ex)
            {
                e.Result = ex;
            }
        }

        private void OnMessageProgress(object sender, ProgressChangedEventArgs e)
        {
            string json = e.UserState as string;
            if (json == null) return;

            try
            {
                JObject obj = JObject.Parse(json);
                JToken typeToken = obj.SelectToken("type");
                if (typeToken == null) return;
                string eventType = (string)typeToken;

                if (eventType == "intent")
                {
                    JToken intentToken = obj.SelectToken("intent");
                    if (intentToken != null)
                    {
                        string intentText = (string)intentToken;
                        // Check off the previous intent (it's done)
                        if (_tasksList.Items.Count > 0)
                        {
                            int lastIdx = _tasksList.Items.Count - 1;
                            if (!_tasksList.GetItemChecked(lastIdx))
                            {
                                _tasksList.SetItemChecked(lastIdx, true);
                                MarkLastIntentCompleted();
                            }
                        }
                        // Add new intent as unchecked
                        _tasksList.Items.Add(intentText);
                        _tasksList.TopIndex = _tasksList.Items.Count - 1;
                        _statusState.Text = intentText;
                        if (_currentSessionData != null)
                        {
                            ChatMessage intentMsg = new ChatMessage("system", intentText, "intent");
                            _currentSessionData.AddMessage(intentMsg);
                        }
                    }
                }
                else if (eventType == "reasoning")
                {
                    JToken contentToken = obj.SelectToken("content");
                    if (contentToken != null)
                    {
                        string reasoningText = (string)contentToken;
                        _reasoningBuffer += reasoningText;
                        _chatRenderer.AppendReasoning(reasoningText);
                        _chatRenderer.ScrollToEnd();
                        _statusState.Text = "Thinking...";
                    }
                }
                else if (eventType == "delta")
                {
                    // Save accumulated reasoning before first content delta
                    if (_reasoningBuffer.Length > 0 && _currentSessionData != null)
                    {
                        _currentSessionData.AddMessage(new ChatMessage("assistant", _reasoningBuffer, "reasoning"));
                        _reasoningBuffer = "";
                    }

                    JToken contentToken = obj.SelectToken("content");
                    if (contentToken != null)
                    {
                        string content = (string)contentToken;
                        _assistantBuffer += content;
                        _chatRenderer.AppendDelta(content);
                        _chatRenderer.ScrollToEnd();
                        _statusState.Text = "Streaming...";
                        if (_merlin != null && _merlin.IsAvailable && _merlinEnabled)
                        {
                            _merlin.AnimateWriting();
                        }
                    }
                }
                else if (eventType == "message")
                {
                    _chatRenderer.FinalizeMessage();
                    _chatRenderer.ScrollToEnd();
                }
                else if (eventType == "tool_start")
                {
                    JToken toolToken = obj.SelectToken("tool");
                    string toolName = toolToken != null ? (string)toolToken : "unknown";
                    JToken idToken = obj.SelectToken("id");
                    string toolId = idToken != null ? (string)idToken : null;
                    JToken inputToken = obj.SelectToken("input");
                    string inputStr = inputToken != null ? (string)inputToken : null;

                    // Extract a human-readable summary from the tool input
                    string summary = ExtractToolSummary(toolName, inputStr);
                    string filePath = ExtractFilePath(toolName, inputStr);

                    // Track tool call index for checking off on tool_end
                    if (toolId != null)
                    {
                        _toolCallIndex[toolId] = -1; // no task list entry
                    }

                    _chatRenderer.AppendToolCall(summary, inputStr);
                    if (_currentSessionData != null)
                    {
                        ChatMessage toolMsg = new ChatMessage("tool", summary, "tool");
                        toolMsg.Detail = inputStr != null ? inputStr : "";
                        _currentSessionData.AddMessage(toolMsg);
                    }
                    _chatRenderer.ScrollToEnd();
                    _statusState.Text = string.Format("Tool: {0}", toolName);

                    // Add file path to output list if applicable
                    if (filePath != null)
                    {
                        AddOutputItem(filePath);
                    }

                    // Show file artifact in chat for file-creating tools
                    if (toolName == "write" || toolName == "Write"
                        || toolName == "xp_write_file" || toolName == "Edit" || toolName == "edit")
                    {
                        if (filePath != null)
                        {
                            _chatRenderer.AppendFileArtifact(filePath);
                        }
                    }

                    if (_merlin != null && _merlin.IsAvailable && _merlinEnabled)
                    {
                        _merlin.AnimateSearching();
                    }
                }
                else if (eventType == "tool_end")
                {
                    JToken idToken = obj.SelectToken("id");
                    string toolId = idToken != null ? (string)idToken : null;
                    JToken successToken = obj.SelectToken("success");
                    bool success = successToken != null && (bool)successToken;

                    // Check off the task
                    if (toolId != null && _toolCallIndex.ContainsKey(toolId))
                    {
                        int idx = _toolCallIndex[toolId];
                        if (idx < _tasksList.Items.Count)
                        {
                            _tasksList.SetItemChecked(idx, success);
                        }
                    }

                    // Add output files if present
                    JToken outputToken = obj.SelectToken("output");
                    if (outputToken != null)
                    {
                        string output = (string)outputToken;
                        if (output != null && output.Length > 0 && output.Length < 500)
                        {
                            // Short output — might be a file path or result summary
                            string trimmed = output.Trim();
                            if (trimmed.Length > 0 && trimmed.Length < 260)
                            {
                                AddOutputItem(trimmed);
                            }
                        }
                    }
                }
                else if (eventType == "title_changed")
                {
                    JToken titleToken = obj.SelectToken("title");
                    if (titleToken != null)
                    {
                        string title = (string)titleToken;
                        if (_sessionId != null)
                        {
                            _sessionTitles[_sessionId] = title;
                            _statusSession.Text = title;
                            if (_currentSessionData != null)
                            {
                                _currentSessionData.Title = title;
                                _sessionStore.Save(_currentSessionData);
                            }
                            LoadPersistedSessions();
                        }
                    }
                }
                else if (eventType == "idle")
                {
                    // Check off the last intent
                    if (_tasksList.Items.Count > 0)
                    {
                        int lastIdx = _tasksList.Items.Count - 1;
                        if (!_tasksList.GetItemChecked(lastIdx))
                        {
                            _tasksList.SetItemChecked(lastIdx, true);
                            MarkLastIntentCompleted();
                        }
                    }
                    // Save any remaining reasoning
                    if (_currentSessionData != null && _reasoningBuffer.Length > 0)
                    {
                        _currentSessionData.AddMessage(new ChatMessage("assistant", _reasoningBuffer, "reasoning"));
                        _reasoningBuffer = "";
                    }
                    // Save assistant response to session
                    if (_currentSessionData != null && _assistantBuffer.Length > 0)
                    {
                        _currentSessionData.AddMessage(new ChatMessage("assistant", _assistantBuffer, DateTime.Now));
                        _currentSessionData.ContextFiles = new List<string>(_contextFiles);
                        _currentSessionData.OutputFiles = new List<string>(_outputFiles);
                        _sessionStore.Save(_currentSessionData);
                        _assistantBuffer = "";
                    }
                    _chatRenderer.AppendNewline();
                    _chatRenderer.ScrollToEnd();
                    _statusState.Text = "Ready";
                    if (_merlin != null && _merlin.IsAvailable && _merlinEnabled)
                    {
                        _merlin.AnimateIdle();
                    }
                }
                else if (eventType == "error")
                {
                    JToken msgToken = obj.SelectToken("message");
                    if (msgToken != null)
                    {
                        string errorMsg = (string)msgToken;
                        _chatRenderer.AppendError(errorMsg);
                        _chatRenderer.ScrollToEnd();
                        if (_currentSessionData != null)
                        {
                            _currentSessionData.AddMessage(new ChatMessage("system", errorMsg, "error"));
                        }
                    }
                    if (_merlin != null && _merlin.IsAvailable && _merlinEnabled)
                    {
                        _merlin.AnimateSad();
                    }
                }
            }
            catch
            {
            }
        }

        private void OnMessageCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            _statusState.Text = "Ready";

            if (e.Error != null)
            {
                _chatRenderer.AppendError(string.Format("Request failed: {0}", e.Error.Message));
                _chatRenderer.ScrollToEnd();
                if (_merlin != null && _merlin.IsAvailable && _merlinEnabled) _merlin.AnimateSad();
                return;
            }

            Exception ex = e.Result as Exception;
            if (ex != null)
            {
                _chatRenderer.AppendError(string.Format("Request failed: {0}", ex.Message));
                _chatRenderer.ScrollToEnd();
                if (_merlin != null && _merlin.IsAvailable && _merlinEnabled) _merlin.AnimateSad();
                return;
            }

            _chatRenderer.FinalizeMessage();
            _chatRenderer.ScrollToEnd();
            if (_merlin != null && _merlin.IsAvailable && _merlinEnabled) _merlin.AnimateIdle();
        }

        // ====================================================================
        // Right sidebar helpers
        // ====================================================================

        private void MarkLastIntentCompleted()
        {
            if (_currentSessionData == null) return;
            List<ChatMessage> msgs = _currentSessionData.Messages;
            for (int i = msgs.Count - 1; i >= 0; i--)
            {
                if (msgs[i].Type == "intent" && !msgs[i].Completed)
                {
                    msgs[i].Completed = true;
                    break;
                }
            }
        }

        private void AddOutputItem(string text)
        {
            for (int i = 0; i < _outputFiles.Count; i++)
            {
                if (_outputFiles[i] == text) return;
            }
            _outputFiles.Add(text);
            _outputList.Items.Add(text);
        }

        private static string ExtractToolSummary(string toolName, string input)
        {
            if (input == null || input.Length == 0)
            {
                return toolName;
            }

            try
            {
                JObject inputObj = JObject.Parse(input);

                if (toolName == "xp_shell")
                {
                    JToken cmdToken = inputObj.SelectToken("command");
                    if (cmdToken != null)
                    {
                        string cmd = (string)cmdToken;
                        // Truncate long commands
                        if (cmd.Length > 80)
                        {
                            cmd = cmd.Substring(0, 77) + "...";
                        }
                        return string.Format("Run: {0}", cmd);
                    }
                }
                else if (toolName == "xp_read_file")
                {
                    JToken pathToken = inputObj.SelectToken("file_path");
                    if (pathToken != null)
                    {
                        return string.Format("Read: {0}", System.IO.Path.GetFileName((string)pathToken));
                    }
                }
                else if (toolName == "xp_write_file")
                {
                    JToken pathToken = inputObj.SelectToken("file_path");
                    if (pathToken != null)
                    {
                        return string.Format("Write: {0}", System.IO.Path.GetFileName((string)pathToken));
                    }
                }
                else if (toolName == "xp_list_directory")
                {
                    JToken pathToken = inputObj.SelectToken("directory_path");
                    if (pathToken != null)
                    {
                        return string.Format("List: {0}", (string)pathToken);
                    }
                }
            }
            catch
            {
                // Input wasn't valid JSON
            }

            return toolName;
        }

        private static string ExtractFilePath(string toolName, string input)
        {
            if (input == null || input.Length == 0) return null;

            if (toolName == "read" || toolName == "view" || toolName == "write"
                || toolName == "Read" || toolName == "View" || toolName == "Write"
                || toolName == "Edit" || toolName == "edit"
                || toolName == "xp_read_file" || toolName == "xp_write_file")
            {
                try
                {
                    JObject inputObj = JObject.Parse(input);
                    JToken pathToken = inputObj.SelectToken("file_path");
                    if (pathToken != null) return (string)pathToken;
                    pathToken = inputObj.SelectToken("path");
                    if (pathToken != null) return (string)pathToken;
                }
                catch
                {
                    if (input.Length < 260 && input.IndexOf('{') < 0)
                    {
                        return input.Trim();
                    }
                }
            }
            return null;
        }

        // ====================================================================
        // UI event handlers
        // ====================================================================

        private void OnSendClick(object sender, EventArgs e)
        {
            SendMessage();
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SendMessage();
            }
        }

        private void OnAttachClick(object sender, EventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Title = "Attach File";
            dlg.Filter = "All files (*.*)|*.*|C# files (*.cs)|*.cs|Text files (*.txt)|*.txt";
            dlg.Multiselect = true;
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                string[] files = dlg.FileNames;
                for (int i = 0; i < files.Length; i++)
                {
                    string filePath = files[i];
                    string fileName = System.IO.Path.GetFileName(filePath);

                    _contextFiles.Add(filePath);
                    _attachList.Items.Add(fileName);
                    _contextList.Items.Add(fileName);
                }
                UpdateAttachListVisibility();
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
                if (idx < _contextList.Items.Count)
                {
                    _contextList.Items.RemoveAt(idx);
                }
                UpdateAttachListVisibility();
            }
        }

        private void OnRemoveContext(object sender, EventArgs e)
        {
            int idx = _contextList.SelectedIndex;
            if (idx >= 0 && idx < _contextFiles.Count)
            {
                _contextFiles.RemoveAt(idx);
                _contextList.Items.RemoveAt(idx);
                if (idx < _attachList.Items.Count)
                {
                    _attachList.Items.RemoveAt(idx);
                }
                UpdateAttachListVisibility();
            }
        }

        private void OnDrawAttachItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _contextFiles.Count) return;

            e.DrawBackground();

            string filePath = _contextFiles[e.Index];
            string fileName = System.IO.Path.GetFileName(filePath);

            try
            {
                Icon fileIcon = null;
                if (System.IO.File.Exists(filePath))
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

        private void UpdateAttachListVisibility()
        {
            if (_contextFiles.Count > 0)
            {
                _attachList.Height = System.Math.Min(_contextFiles.Count * 20 + 4, 64);
                _attachList.Visible = true;
            }
            else
            {
                _attachList.Height = 0;
                _attachList.Visible = false;
            }
        }

        private void OnOutputDoubleClick(object sender, EventArgs e)
        {
            int idx = _outputList.SelectedIndex;
            if (idx < 0 || idx >= _outputFiles.Count) return;

            string item = _outputFiles[idx];
            try
            {
                if (System.IO.File.Exists(item))
                {
                    System.Diagnostics.Process.Start(item);
                }
                else
                {
                    // Fallback: show in text viewer
                    Form viewer = new Form();
                    viewer.Text = "Output: " + item;
                    viewer.Size = new Size(600, 400);
                    viewer.StartPosition = FormStartPosition.CenterParent;
                    RichTextBox rtb = new RichTextBox();
                    rtb.Dock = DockStyle.Fill;
                    rtb.ReadOnly = true;
                    rtb.Font = new Font("Lucida Console", 9f);
                    rtb.Text = item;
                    viewer.Controls.Add(rtb);
                    viewer.Show(this);
                }
            }
            catch { }
        }

        private void OnDrawOutputItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _outputFiles.Count) return;

            e.DrawBackground();

            string filePath = _outputFiles[e.Index];
            string fileName = System.IO.Path.GetFileName(filePath);

            // Draw file icon
            try
            {
                Icon fileIcon = null;
                if (System.IO.File.Exists(filePath))
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

            // Draw filename
            using (SolidBrush brush = new SolidBrush(e.ForeColor))
            {
                e.Graphics.DrawString(fileName, e.Font, brush, e.Bounds.X + 22, e.Bounds.Y + 3);
            }

            e.DrawFocusRectangle();
        }

        private void OnNewSession(object sender, EventArgs e)
        {
            if (_messageWorker.IsBusy) _messageWorker.CancelAsync();

            _sessionId = null;
            _proxySessionId = null;
            _pendingMessage = null;
            _currentSessionData = null;
            _sessionList.SelectedIndexChanged -= new EventHandler(this.OnSessionListSelected);
            _sessionList.SelectedIndex = -1;
            _sessionList.SelectedIndexChanged += new EventHandler(this.OnSessionListSelected);
            _sessionList.Invalidate();
            _chatRenderer.Clear();
            _chatHistory.Clear();
            _tasksList.Items.Clear();
            _outputList.Items.Clear();
            _outputFiles.Clear();
            _toolCallIndex.Clear();
            _statusSession.Text = "No Session";
            _statusState.Text = "Connecting...";
            _chatRenderer.AppendSystemMessage("Creating new session...");
            _chatRenderer.ScrollToEnd();

            if (!_sessionWorker.IsBusy) _sessionWorker.RunWorkerAsync(GetSelectedWorkDir());
        }

        private void OnExit(object sender, EventArgs e)
        {
            this.Close();
        }

        private void OnCopy(object sender, EventArgs e)
        {
            // Copy not yet implemented for ChatPanel

        }

        private void OnClearHistory(object sender, EventArgs e)
        {
            _chatRenderer.Clear();
            _chatHistory.Clear();
            _tasksList.Items.Clear();
            _outputList.Items.Clear();
            _outputFiles.Clear();
            _toolCallIndex.Clear();
            _chatRenderer.AppendSystemMessage("Chat history cleared.");
            _chatRenderer.ScrollToEnd();
        }

        private void OnToggleSessionsPanel(object sender, EventArgs e)
        {
            _outerSplit.Panel1Collapsed = !_outerSplit.Panel1Collapsed;
        }

        private void OnToggleDetailsPanel(object sender, EventArgs e)
        {
            _innerSplit.Panel2Collapsed = !_innerSplit.Panel2Collapsed;
        }

        private void OnSettings(object sender, EventArgs e)
        {
            string input = ShowInputDialog("Proxy Address:", _proxyAddress);
            if (input != null && input.Length > 0)
            {
                _proxyAddress = input;
                _proxyClient.BaseUrl = _proxyAddress;
                _chatRenderer.AppendSystemMessage(string.Format("Proxy address changed to: {0}", _proxyAddress));
                _chatRenderer.ScrollToEnd();
            }
        }

        private void OnToggleMerlin(object sender, EventArgs e)
        {
            _merlinEnabled = !_merlinEnabled;
            if (_merlin != null && _merlin.IsAvailable)
            {
                if (_merlinEnabled)
                {
                    _merlin.Show();
                    _merlin.AnimateGreet();
                }
                else
                {
                    _merlin.Hide();
                }
            }
        }

        private void OnAbout(object sender, EventArgs e)
        {
            MessageBox.Show(
                "Incantation - AI Coding Assistant\nVersion 1.0.0\n\nA Windows XP native client for AI-assisted coding.\nPowered by GitHub Copilot SDK.",
                "About Incantation",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        // ====================================================================
        // Helpers
        // ====================================================================

        private void FetchModels()
        {
            try
            {
                string[] models = _proxyClient.ListModels();
                if (models.Length > 0)
                {
                    _cboModel.Items.Clear();
                    for (int i = 0; i < models.Length; i++)
                    {
                        _cboModel.Items.Add(models[i]);
                    }
                    _cboModel.SelectedIndex = 0;
                }
            }
            catch
            {
                // Models fetch failed — keep default
            }
        }

        private string GetSelectedWorkDir()
        {
            if (_cboWorkDir.SelectedItem != null)
            {
                return _cboWorkDir.SelectedItem.ToString();
            }
            if (_cboWorkDir.Text.Length > 0)
            {
                return _cboWorkDir.Text;
            }
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private static string TruncateId(string id)
        {
            if (id == null) return "";
            if (id.Length <= 12) return id;
            return id.Substring(0, 12) + "...";
        }

        private string ShowInputDialog(string prompt, string defaultValue)
        {
            Form dlg = new Form();
            dlg.Text = "Settings";
            dlg.Size = new Size(400, 150);
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
            dlg.MaximizeBox = false;
            dlg.MinimizeBox = false;

            Label lbl = new Label();
            lbl.Text = prompt;
            lbl.Location = new Point(10, 15);
            lbl.Size = new Size(360, 20);

            TextBox txt = new TextBox();
            txt.Text = defaultValue;
            txt.Location = new Point(10, 40);
            txt.Size = new Size(360, 20);

            Button btnOk = new Button();
            btnOk.Text = "OK";
            btnOk.DialogResult = DialogResult.OK;
            btnOk.Location = new Point(210, 75);
            btnOk.Size = new Size(75, 25);

            Button btnCancel = new Button();
            btnCancel.Text = "Cancel";
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Location = new Point(295, 75);
            btnCancel.Size = new Size(75, 25);

            dlg.Controls.Add(lbl);
            dlg.Controls.Add(txt);
            dlg.Controls.Add(btnOk);
            dlg.Controls.Add(btnCancel);
            dlg.AcceptButton = btnOk;
            dlg.CancelButton = btnCancel;

            string result = null;
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                result = txt.Text;
            }
            dlg.Dispose();
            return result;
        }
    }
}
