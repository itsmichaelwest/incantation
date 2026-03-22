using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Incantation.Chat;

namespace Incantation.UI
{
    // A segment of text with uniform styling
    internal class TextSpan
    {
        public string Text;
        public Font Font;
        public Color Color;

        public TextSpan(string text, Font font, Color color)
        {
            Text = text;
            Font = font;
            Color = color;
        }
    }

    // A single logical line of styled text
    internal class DisplayLine
    {
        public List<TextSpan> Spans;
        public Color BackColor;
        public int LeftPadding;

        public DisplayLine()
        {
            Spans = new List<TextSpan>();
            BackColor = Color.Empty;
            LeftPadding = 0;
        }
    }

    // A chat message block (user message, assistant response, tool call, etc.)
    internal class ChatBlock
    {
        public string Role;
        public Color AccentColor;
        public Color BackColor;
        public List<DisplayLine> Lines;
        public int CachedHeight;
        public int CachedWidth;
        public bool Collapsed;
        public bool Collapsible;
        public int SummaryLineCount; // lines visible when collapsed
        public string FilePath; // set for file artifact blocks

        public ChatBlock(string role, Color accent, Color bg)
        {
            Role = role;
            AccentColor = accent;
            BackColor = bg;
            Lines = new List<DisplayLine>();
            CachedHeight = -1;
            CachedWidth = -1;
            Collapsed = false;
            Collapsible = false;
            SummaryLineCount = 0;
            FilePath = null;
        }

        public void InvalidateCache()
        {
            CachedHeight = -1;
            CachedWidth = -1;
        }
    }

    public class ChatPanel : Control, IChatRenderer
    {
        // ================================================================
        // Font smoothing detection
        // ================================================================
        private const int SPI_GETFONTSMOOTHINGTYPE = 0x200A;
        private const int FE_FONTSMOOTHINGCLEARTYPE = 0x0002;

        [DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(int uAction, int uParam, out int pvParam, int fWinIni);

        private static readonly System.Drawing.Text.TextRenderingHint _textHint = DetectTextHint();

        private static System.Drawing.Text.TextRenderingHint DetectTextHint()
        {
            if (!SystemInformation.IsFontSmoothingEnabled)
            {
                return System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            }
            int smoothType = 0;
            SystemParametersInfo(SPI_GETFONTSMOOTHINGTYPE, 0, out smoothType, 0);
            if (smoothType == FE_FONTSMOOTHINGCLEARTYPE)
            {
                return System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            }
            return System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        }

        // ================================================================
        // Visual constants
        // ================================================================
        private const int BLOCK_MARGIN = 6;
        private const int BLOCK_PADDING = 6;
        private const int ACCENT_WIDTH = 3;
        private const int CONTENT_MARGIN = 8;  // left of accent

        // ================================================================
        // Colors
        // ================================================================
        private static readonly Color BgUser = Color.FromArgb(235, 240, 244);
        private static readonly Color BgAssistant = Color.FromArgb(237, 245, 241);
        private static readonly Color BgTool = Color.FromArgb(245, 237, 230);
        private static readonly Color BgError = Color.FromArgb(252, 238, 237);
        private static readonly Color BgReasoning = Color.FromArgb(242, 242, 244);
        private static readonly Color BgCode = Color.FromArgb(228, 232, 236);

        private static readonly Color AccentUser = Color.FromArgb(61, 79, 95);
        private static readonly Color AccentAssistant = Color.FromArgb(46, 125, 89);
        private static readonly Color AccentTool = Color.FromArgb(181, 100, 58);
        private static readonly Color AccentError = Color.FromArgb(184, 48, 48);
        private static readonly Color AccentReasoning = Color.FromArgb(180, 190, 198);

        private static readonly Color ColorUserName = Color.FromArgb(61, 79, 95);
        private static readonly Color ColorAssistantName = Color.FromArgb(46, 125, 89);
        private static readonly Color ColorTimestamp = Color.Gray;
        private static readonly Color ColorContent = Color.Black;
        private static readonly Color ColorToolSummary = Color.FromArgb(181, 100, 58);
        private static readonly Color ColorSystem = Color.Gray;
        private static readonly Color ColorErrorText = Color.FromArgb(184, 48, 48);
        private static readonly Color ColorReasoning = Color.FromArgb(130, 140, 148);
        private static readonly Color ColorBullet = Color.FromArgb(90, 100, 110);
        private static readonly Color ColorHeader = Color.FromArgb(61, 79, 95);
        private static readonly Color ColorBlockquote = Color.FromArgb(90, 100, 110);
        private static readonly Color ColorLink = Color.FromArgb(46, 125, 89);
        private static readonly Color ColorCodeBorder = Color.FromArgb(200, 208, 214);

        // ================================================================
        // Pre-created GDI brushes and pens for paint performance
        // ================================================================
        private static readonly SolidBrush _brushBgUser = new SolidBrush(BgUser);
        private static readonly SolidBrush _brushBgAssistant = new SolidBrush(BgAssistant);
        private static readonly SolidBrush _brushBgTool = new SolidBrush(BgTool);
        private static readonly SolidBrush _brushBgError = new SolidBrush(BgError);
        private static readonly SolidBrush _brushBgReasoning = new SolidBrush(BgReasoning);
        private static readonly SolidBrush _brushBgCode = new SolidBrush(BgCode);
        private static readonly SolidBrush _brushBgFileArtifact = new SolidBrush(Color.FromArgb(250, 248, 245));

        private static readonly SolidBrush _brushAccentUser = new SolidBrush(AccentUser);
        private static readonly SolidBrush _brushAccentAssistant = new SolidBrush(AccentAssistant);
        private static readonly SolidBrush _brushAccentTool = new SolidBrush(AccentTool);
        private static readonly SolidBrush _brushAccentError = new SolidBrush(AccentError);
        private static readonly SolidBrush _brushAccentReasoning = new SolidBrush(AccentReasoning);

        private static readonly Pen _codeBorderPen = new Pen(ColorCodeBorder);

        // ================================================================
        // File icon cache
        // ================================================================
        private static Dictionary<string, Icon> _iconCache = new Dictionary<string, Icon>();

        // ================================================================
        // Fonts
        // ================================================================
        private Font _fontNormal;
        private Font _fontBold;
        private Font _fontItalic;
        private Font _fontBoldItalic;
        private Font _fontCode;
        private Font _fontInlineCode;
        private Font _fontHeader1;
        private Font _fontHeader2;
        private Font _fontSmall;
        private Font _fontStrike;

        // ================================================================
        // State
        // ================================================================
        private List<ChatBlock> _blocks;
        private VScrollBar _scrollBar;
        private int _totalHeight;
        private bool _autoScroll;

        // Paint throttling
        private bool _invalidatePending;
        private Timer _invalidateTimer;
        private int _suspendCount;
        private SolidBrush _highlightBrush;

        // Streaming state
        private StringBuilder _lineBuffer;
        private bool _inCodeBlock;
        private bool _inReasoning;
        private bool _skippingLangHint;
        private bool _inTable;
        private List<string> _tableRows;
        private ChatBlock _currentBlock;
        private ChatBlock _assistantBlock;

        // ================================================================
        // Selection state
        // ================================================================
        private struct PaintedLine
        {
            public int BlockIndex;
            public int LineIndex;
            public int Y;
            public int Height;
            public string Text;
        }

        private List<PaintedLine> _paintedLines;
        private bool _isSelecting;
        private int _selStartBlock;
        private int _selStartLine;
        private int _selEndBlock;
        private int _selEndLine;
        private bool _hasSelection;

        // ================================================================
        // Constructor
        // ================================================================

        public ChatPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable, true);

            BackColor = Color.White;
            Cursor = Cursors.IBeam;

            _fontNormal = new Font("Tahoma", 8.25f, FontStyle.Regular);
            _fontBold = new Font("Tahoma", 8.25f, FontStyle.Bold);
            _fontItalic = new Font("Tahoma", 8.25f, FontStyle.Italic);
            _fontBoldItalic = new Font("Tahoma", 8.25f, FontStyle.Bold | FontStyle.Italic);
            _fontCode = new Font("Lucida Console", 9f, FontStyle.Regular);
            _fontInlineCode = new Font("Lucida Console", 8.5f, FontStyle.Regular);
            _fontHeader1 = new Font("Tahoma", 12f, FontStyle.Bold);
            _fontHeader2 = new Font("Tahoma", 10f, FontStyle.Bold);
            _fontSmall = new Font("Lucida Console", 7f, FontStyle.Regular);
            _fontStrike = new Font("Tahoma", 8.25f, FontStyle.Strikeout);

            _blocks = new List<ChatBlock>();
            _autoScroll = true;

            _lineBuffer = new StringBuilder();
            _inCodeBlock = false;
            _inReasoning = false;
            _skippingLangHint = false;
            _inTable = false;
            _tableRows = new List<string>();
            _currentBlock = null;

            _paintedLines = new List<PaintedLine>();
            _isSelecting = false;
            _hasSelection = false;
            _selStartBlock = -1;
            _selStartLine = -1;
            _selEndBlock = -1;
            _selEndLine = -1;

            _scrollBar = new VScrollBar();
            _scrollBar.Dock = DockStyle.Right;
            _scrollBar.Visible = false;
            _scrollBar.Scroll += OnScroll;
            Controls.Add(_scrollBar);

            _highlightBrush = new SolidBrush(SystemColors.Highlight);

            _invalidatePending = false;
            _invalidateTimer = new Timer();
            _invalidateTimer.Interval = 50;
            _invalidateTimer.Tick += OnInvalidateTimerTick;
            _invalidateTimer.Start();
        }

        // ================================================================
        // IChatRenderer implementation
        // ================================================================

        public void SuspendPainting()
        {
            _suspendCount++;
        }

        public void ResumePainting()
        {
            _suspendCount--;
            if (_suspendCount <= 0)
            {
                _suspendCount = 0;
                Invalidate();
            }
        }

        private void InvalidateIfNotSuspended()
        {
            if (_suspendCount <= 0)
            {
                Invalidate();
            }
        }

        private void InvalidateThrottled()
        {
            _invalidatePending = true;
        }

        private void OnInvalidateTimerTick(object sender, EventArgs e)
        {
            if (_invalidatePending)
            {
                _invalidatePending = false;
                if (_suspendCount <= 0)
                {
                    Invalidate();
                }
            }
        }

        public void ScrollToEnd()
        {
            _autoScroll = true;
            UpdateScrollBar();
            InvalidateThrottled();
        }

        public void AppendUserMessage(string name, DateTime time, string content)
        {
            ChatBlock block = new ChatBlock("user", AccentUser, BgUser);

            DisplayLine header = new DisplayLine();
            header.Spans.Add(new TextSpan(name + " ", _fontBold, ColorUserName));
            header.Spans.Add(new TextSpan("[" + time.ToString("HH:mm") + "]", _fontNormal, ColorTimestamp));
            block.Lines.Add(header);

            // Parse content as simple text lines (user messages are plain text)
            string[] lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                DisplayLine dl = new DisplayLine();
                dl.Spans.Add(new TextSpan(lines[i], _fontNormal, ColorContent));
                block.Lines.Add(dl);
            }

            AddBlock(block);
        }

        public void AppendAssistantHeader(string name, DateTime time)
        {
            ChatBlock block = new ChatBlock("assistant", AccentAssistant, BgAssistant);

            DisplayLine header = new DisplayLine();
            header.Spans.Add(new TextSpan(name + " ", _fontBold, ColorAssistantName));
            header.Spans.Add(new TextSpan("[" + time.ToString("HH:mm") + "]", _fontNormal, ColorTimestamp));
            block.Lines.Add(header);

            _currentBlock = block;
            _assistantBlock = block;
            _lineBuffer = new StringBuilder();
            _inCodeBlock = false;
            _skippingLangHint = false;
            _inTable = false;
            _tableRows = new List<string>();
            AddBlock(block);
        }

        public void AppendReasoning(string text)
        {
            if (text == null) return;

            // Add reasoning lines directly to the assistant block so they
            // appear between the header and the content (above the response).
            ChatBlock target = _assistantBlock;
            if (target == null) return;

            if (!_inReasoning)
            {
                _inReasoning = true;
                DisplayLine dl = new DisplayLine();
                dl.Spans.Add(new TextSpan("thinking: ", _fontItalic, ColorReasoning));
                target.Lines.Add(dl);
            }

            // Append to last reasoning line, splitting on newlines
            string cleaned = StripEmoji(text);
            string[] parts = cleaned.Split('\n');

            for (int p = 0; p < parts.Length; p++)
            {
                if (p > 0)
                {
                    DisplayLine newLine = new DisplayLine();
                    newLine.Spans.Add(new TextSpan(parts[p], _fontItalic, ColorReasoning));
                    target.Lines.Add(newLine);
                }
                else
                {
                    DisplayLine last = target.Lines[target.Lines.Count - 1];
                    if (last.Spans.Count > 0)
                    {
                        TextSpan lastSpan = last.Spans[last.Spans.Count - 1];
                        lastSpan.Text += parts[p];
                    }
                    else
                    {
                        last.Spans.Add(new TextSpan(parts[p], _fontItalic, ColorReasoning));
                    }
                }
            }

            target.InvalidateCache();
            InvalidateThrottled();
        }

        public void EndReasoning()
        {
            if (_inReasoning && _assistantBlock != null)
            {
                // Add a spacer line between thinking and response
                DisplayLine spacer = new DisplayLine();
                spacer.Spans.Add(new TextSpan("", _fontNormal, ColorContent));
                _assistantBlock.Lines.Add(spacer);
                _assistantBlock.InvalidateCache();
            }
            _inReasoning = false;
        }

        public void AppendDelta(string text)
        {
            if (text == null) return;

            text = StripEmoji(text);

            if (_inReasoning)
            {
                EndReasoning();
            }

            // Restore assistant block if FinalizeMessage was called prematurely
            // (e.g. a "message" event between reasoning and content)
            if (_currentBlock == null && _assistantBlock != null)
            {
                _currentBlock = _assistantBlock;
            }

            _lineBuffer.Append(text);
            ProcessLineBuffer();
        }

        public void AppendToolCall(string summary, string detail)
        {
            // Flush any pending content so it appears BEFORE the tool call
            FlushLineBuffer();
            if (_inTable) { FlushTable(); _inTable = false; }
            if (_currentBlock != null) _currentBlock.InvalidateCache();

            ChatBlock block = new ChatBlock("tool", AccentTool, BgTool);

            bool hasDetail = detail != null && detail.Length > 0 && detail.Length < 500;
            string prefix = hasDetail ? "[+] " : "> ";

            DisplayLine sumLine = new DisplayLine();
            sumLine.Spans.Add(new TextSpan(prefix + summary, _fontBoldItalic, ColorToolSummary));
            block.Lines.Add(sumLine);
            block.SummaryLineCount = 1;

            if (hasDetail)
            {
                string detailText = detail;
                try
                {
                    Newtonsoft.Json.Linq.JObject parsed = Newtonsoft.Json.Linq.JObject.Parse(detail);
                    detailText = parsed.ToString(Newtonsoft.Json.Formatting.Indented);
                }
                catch { }

                string[] lines = detailText.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    DisplayLine dl = new DisplayLine();
                    dl.Spans.Add(new TextSpan("  " + lines[i].TrimEnd(), _fontSmall, ColorReasoning));
                    block.Lines.Add(dl);
                }

                block.Collapsible = true;
                block.Collapsed = true;
            }

            AddBlock(block);

            // Create a continuation block for content after the tool call
            if (_assistantBlock != null)
            {
                ChatBlock cont = new ChatBlock("assistant", AccentAssistant, BgAssistant);
                _currentBlock = cont;
                _assistantBlock = cont;
                AddBlock(cont);
            }
        }

        public void AppendToolCall(string summary)
        {
            AppendToolCall(summary, null);
        }

        public void AppendError(string message)
        {
            ChatBlock block = new ChatBlock("error", AccentError, BgError);
            DisplayLine dl = new DisplayLine();
            dl.Spans.Add(new TextSpan("Error: ", _fontBold, ColorErrorText));
            dl.Spans.Add(new TextSpan(message, _fontNormal, ColorErrorText));
            block.Lines.Add(dl);
            AddBlock(block);
        }

        public void AppendSystemMessage(string text)
        {
            ChatBlock block = new ChatBlock("system", Color.Empty, Color.Empty);
            DisplayLine dl = new DisplayLine();
            dl.Spans.Add(new TextSpan(text, _fontItalic, ColorSystem));
            block.Lines.Add(dl);
            AddBlock(block);
        }

        public void AppendFileArtifact(string filePath)
        {
            ChatBlock block = new ChatBlock("file", AccentTool, Color.FromArgb(250, 248, 245));
            block.FilePath = filePath;

            string fileName = System.IO.Path.GetFileName(filePath);
            DisplayLine dl = new DisplayLine();
            dl.LeftPadding = 20; // leave room for icon
            dl.Spans.Add(new TextSpan(fileName, _fontBold, ColorLink));
            block.Lines.Add(dl);

            AddBlock(block);
        }

        public void AppendNewline()
        {
            // Spacing handled by block margins
        }

        public void FinalizeMessage()
        {
            FlushLineBuffer();

            if (_inTable)
            {
                FlushTable();
                _inTable = false;
            }

            _inCodeBlock = false;
            _inReasoning = false;
            _skippingLangHint = false;
            _currentBlock = null;
            // Don't null _assistantBlock here — AppendDelta may still need it
            // if a "message" event fires between reasoning and content.
            // It's cleared by AppendAssistantHeader and Clear() instead.

            if (_autoScroll)
            {
                UpdateScrollBar();
            }
            InvalidateIfNotSuspended();
        }

        public void Clear()
        {
            _blocks.Clear();
            _currentBlock = null;
            _assistantBlock = null;
            _lineBuffer = new StringBuilder();
            _inCodeBlock = false;
            _inReasoning = false;
            _skippingLangHint = false;
            _inTable = false;
            _tableRows.Clear();
            _totalHeight = 0;
            _autoScroll = true;

            _paintedLines.Clear();
            _hasSelection = false;
            _isSelecting = false;
            _selStartBlock = -1;
            _selStartLine = -1;
            _selEndBlock = -1;
            _selEndLine = -1;

            UpdateScrollBar();
            InvalidateIfNotSuspended();
        }

        // ================================================================
        // Block management
        // ================================================================

        private void AddBlock(ChatBlock block)
        {
            _blocks.Add(block);
            if (_autoScroll)
            {
                UpdateScrollBar();
            }
            InvalidateIfNotSuspended();
        }

        // ================================================================
        // Streaming: line buffer processing
        // ================================================================

        private void ProcessLineBuffer()
        {
            string bufferStr = _lineBuffer.ToString();
            int lastNewline = bufferStr.LastIndexOf('\n');
            if (lastNewline < 0) return;

            string completePart = bufferStr.Substring(0, lastNewline + 1);
            string remaining = bufferStr.Substring(lastNewline + 1);
            _lineBuffer = new StringBuilder(remaining);

            int start = 0;
            for (int i = 0; i < completePart.Length; i++)
            {
                if (completePart[i] == '\n')
                {
                    string line = completePart.Substring(start, i - start);
                    start = i + 1;
                    RenderMarkdownLine(line);
                }
            }

            if (_currentBlock != null)
            {
                _currentBlock.InvalidateCache();
            }

            if (_autoScroll) UpdateScrollBar();
            InvalidateThrottled();
        }

        private void FlushLineBuffer()
        {
            if (_lineBuffer.Length == 0) return;
            string remaining = _lineBuffer.ToString();
            _lineBuffer = new StringBuilder();
            if (remaining.Length > 0)
            {
                RenderMarkdownLine(remaining);
            }
            if (_currentBlock != null) _currentBlock.InvalidateCache();
        }

        // ================================================================
        // Markdown -> DisplayLine conversion
        // ================================================================

        private void RenderMarkdownLine(string line)
        {
            if (_currentBlock == null) return;

            if (_skippingLangHint)
            {
                _skippingLangHint = false;
                return;
            }

            string trimmed = line.TrimStart();

            // Code block toggle
            if (StartsWith(trimmed, "```"))
            {
                _inCodeBlock = !_inCodeBlock;
                return;
            }

            // Inside code block
            if (_inCodeBlock)
            {
                DisplayLine dl = new DisplayLine();
                dl.BackColor = BgCode;
                dl.Spans.Add(new TextSpan(line, _fontCode, ColorContent));
                _currentBlock.Lines.Add(dl);
                return;
            }

            // Table handling
            bool isTableRow = trimmed.Length > 0
                && trimmed[0] == '|'
                && trimmed[trimmed.Length - 1] == '|';

            if (_inTable)
            {
                if (isTableRow) { _tableRows.Add(line); return; }
                else { FlushTable(); _inTable = false; }
            }
            else if (isTableRow)
            {
                _inTable = true;
                _tableRows = new List<string>();
                _tableRows.Add(line);
                return;
            }

            // Empty line
            if (line.Length == 0)
            {
                DisplayLine dl = new DisplayLine();
                dl.Spans.Add(new TextSpan("", _fontNormal, ColorContent));
                _currentBlock.Lines.Add(dl);
                return;
            }

            // Horizontal rule
            if (IsHorizontalRule(line))
            {
                DisplayLine dl = new DisplayLine();
                dl.Spans.Add(new TextSpan("----------------------------------------", _fontNormal, ColorTimestamp));
                _currentBlock.Lines.Add(dl);
                return;
            }

            // Headers
            if (StartsWith(line, "### "))
            {
                DisplayLine dl = new DisplayLine();
                dl.Spans.Add(new TextSpan(line.Substring(4), _fontBold, ColorHeader));
                _currentBlock.Lines.Add(dl);
                return;
            }
            if (StartsWith(line, "## "))
            {
                DisplayLine dl = new DisplayLine();
                dl.Spans.Add(new TextSpan(line.Substring(3), _fontHeader2, ColorHeader));
                _currentBlock.Lines.Add(dl);
                return;
            }
            if (StartsWith(line, "# "))
            {
                DisplayLine dl = new DisplayLine();
                dl.Spans.Add(new TextSpan(line.Substring(2), _fontHeader1, ColorHeader));
                _currentBlock.Lines.Add(dl);
                return;
            }

            // Blockquotes
            if (StartsWith(trimmed, "> "))
            {
                string quoteText = trimmed.Substring(2);
                while (StartsWith(quoteText, "> ")) quoteText = quoteText.Substring(2);
                if (StartsWith(quoteText, ">")) quoteText = quoteText.Substring(1);

                DisplayLine dl = new DisplayLine();
                dl.LeftPadding = 12;
                dl.Spans.Add(new TextSpan("| ", _fontNormal, ColorBlockquote));
                AddInlineSpans(dl, quoteText, _fontNormal, ColorBlockquote);
                _currentBlock.Lines.Add(dl);
                return;
            }
            if (trimmed == ">")
            {
                DisplayLine dl = new DisplayLine();
                dl.Spans.Add(new TextSpan("", _fontNormal, ColorContent));
                _currentBlock.Lines.Add(dl);
                return;
            }

            // Bullet list
            {
                int indent = 0;
                int idx = 0;
                while (idx < line.Length && line[idx] == ' ') { indent++; idx++; }
                if (idx + 1 < line.Length
                    && (line[idx] == '-' || line[idx] == '*')
                    && line[idx + 1] == ' '
                    && !IsHorizontalRule(line))
                {
                    string itemText = line.Substring(idx + 2);
                    int nestLevel = indent / 2;
                    DisplayLine dl = new DisplayLine();
                    dl.LeftPadding = 8 + nestLevel * 16;
                    dl.Spans.Add(new TextSpan("\u2022 ", _fontNormal, ColorBullet));
                    AddInlineSpans(dl, itemText, _fontNormal, ColorContent);
                    _currentBlock.Lines.Add(dl);
                    return;
                }
            }

            // Ordered list
            {
                int olContentStart, olIndent;
                if (IsOrderedListItem(line, out olContentStart, out olIndent))
                {
                    int nestLevel = olIndent / 2;
                    string prefix = line.Substring(olIndent, olContentStart - olIndent);
                    string itemText = line.Substring(olContentStart);
                    DisplayLine dl = new DisplayLine();
                    dl.LeftPadding = 8 + nestLevel * 16;
                    dl.Spans.Add(new TextSpan(prefix, _fontNormal, ColorBullet));
                    AddInlineSpans(dl, itemText, _fontNormal, ColorContent);
                    _currentBlock.Lines.Add(dl);
                    return;
                }
            }

            // Regular line with inline markdown
            DisplayLine regular = new DisplayLine();
            AddInlineSpans(regular, line, _fontNormal, ColorContent);
            _currentBlock.Lines.Add(regular);
        }

        // ================================================================
        // Inline markdown parsing -> TextSpans
        // ================================================================

        private void AddInlineSpans(DisplayLine dl, string text, Font defaultFont, Color defaultColor)
        {
            const int NORMAL = 0, BOLD = 1, ITALIC = 2, CODE = 3, STRIKE = 4;

            int state = NORMAL;
            StringBuilder buf = new StringBuilder();
            int len = text.Length;

            for (int i = 0; i < len; i++)
            {
                char c = text[i];

                if (c == '`')
                {
                    if (state == CODE)
                    {
                        if (buf.Length > 0) dl.Spans.Add(new TextSpan(buf.ToString(), _fontInlineCode, ColorContent));
                        buf = new StringBuilder();
                        state = NORMAL;
                    }
                    else
                    {
                        FlushSpan(dl, buf, state, defaultFont, defaultColor);
                        buf = new StringBuilder();
                        state = CODE;
                    }
                    continue;
                }

                if (state == CODE) { buf.Append(c); continue; }

                // Strikethrough: ~~
                if (c == '~' && i + 1 < len && text[i + 1] == '~')
                {
                    FlushSpan(dl, buf, state, defaultFont, defaultColor);
                    buf = new StringBuilder();
                    state = (state == STRIKE) ? NORMAL : STRIKE;
                    i++;
                    continue;
                }

                // Links: [text](url)
                if (c == '[')
                {
                    int closeBracket = text.IndexOf(']', i + 1);
                    if (closeBracket > i + 1 && closeBracket + 1 < len && text[closeBracket + 1] == '(')
                    {
                        int closeParen = text.IndexOf(')', closeBracket + 2);
                        if (closeParen > closeBracket + 2)
                        {
                            FlushSpan(dl, buf, state, defaultFont, defaultColor);
                            buf = new StringBuilder();
                            string linkText = text.Substring(i + 1, closeBracket - i - 1);
                            dl.Spans.Add(new TextSpan(linkText, defaultFont, ColorLink));
                            i = closeParen;
                            continue;
                        }
                    }
                    buf.Append(c);
                    continue;
                }

                if (c == '*')
                {
                    if (i + 1 < len && text[i + 1] == '*')
                    {
                        FlushSpan(dl, buf, state, defaultFont, defaultColor);
                        buf = new StringBuilder();
                        state = (state == BOLD) ? NORMAL : BOLD;
                        i++;
                        continue;
                    }
                    else
                    {
                        FlushSpan(dl, buf, state, defaultFont, defaultColor);
                        buf = new StringBuilder();
                        state = (state == ITALIC) ? NORMAL : ITALIC;
                        continue;
                    }
                }

                buf.Append(c);
            }

            if (buf.Length > 0) FlushSpan(dl, buf, state, defaultFont, defaultColor);
        }

        private void FlushSpan(DisplayLine dl, StringBuilder buf, int state, Font defaultFont, Color defaultColor)
        {
            if (buf.Length == 0) return;
            string text = buf.ToString();

            switch (state)
            {
                case 1: // bold
                    dl.Spans.Add(new TextSpan(text, _fontBold, defaultColor));
                    break;
                case 2: // italic
                    dl.Spans.Add(new TextSpan(text, _fontItalic, defaultColor));
                    break;
                case 3: // code
                    dl.Spans.Add(new TextSpan(text, _fontInlineCode, defaultColor));
                    break;
                case 4: // strikethrough
                    dl.Spans.Add(new TextSpan(text, _fontStrike, defaultColor));
                    break;
                default:
                    dl.Spans.Add(new TextSpan(text, defaultFont, defaultColor));
                    break;
            }
        }

        // ================================================================
        // Table rendering
        // ================================================================

        private void FlushTable()
        {
            if (_currentBlock == null || _tableRows.Count == 0) return;

            List<string[]> parsed = new List<string[]>();
            int maxCols = 0;

            for (int r = 0; r < _tableRows.Count; r++)
            {
                string row = _tableRows[r].Trim();
                if (IsTableSeparator(row)) continue;
                string[] cells = ParseTableRow(row);
                if (cells.Length > maxCols) maxCols = cells.Length;
                parsed.Add(cells);
            }

            if (parsed.Count == 0 || maxCols == 0) return;

            int[] widths = new int[maxCols];
            for (int r = 0; r < parsed.Count; r++)
            {
                for (int c = 0; c < parsed[r].Length; c++)
                {
                    if (parsed[r][c].Length > widths[c]) widths[c] = parsed[r][c].Length;
                }
            }

            for (int r = 0; r < parsed.Count; r++)
            {
                StringBuilder sb = new StringBuilder("  ");
                for (int c = 0; c < maxCols; c++)
                {
                    string cell = c < parsed[r].Length ? parsed[r][c] : "";
                    sb.Append(cell);
                    int pad = widths[c] - cell.Length + 2;
                    for (int p = 0; p < pad; p++) sb.Append(' ');
                }

                DisplayLine dl = new DisplayLine();
                Font f = (r == 0) ? _fontBold : _fontCode;
                dl.Spans.Add(new TextSpan(sb.ToString(), f, ColorContent));
                _currentBlock.Lines.Add(dl);

                if (r == 0)
                {
                    StringBuilder sep = new StringBuilder("  ");
                    for (int c = 0; c < maxCols; c++)
                    {
                        for (int d = 0; d < widths[c]; d++) sep.Append('-');
                        sep.Append("  ");
                    }
                    DisplayLine sepLine = new DisplayLine();
                    sepLine.Spans.Add(new TextSpan(sep.ToString(), _fontCode, ColorTimestamp));
                    _currentBlock.Lines.Add(sepLine);
                }
            }

            _tableRows.Clear();
        }

        // ================================================================
        // Layout: measuring blocks and lines
        // ================================================================

        private int MeasureBlock(Graphics g, ChatBlock block, int width)
        {
            if (block.Lines.Count == 0) return 0;

            if (block.CachedHeight >= 0 && block.CachedWidth == width)
                return block.CachedHeight;

            int contentWidth = width - CONTENT_MARGIN - ACCENT_WIDTH - BLOCK_PADDING * 2;
            if (contentWidth < 50) contentWidth = 50;

            int totalH = BLOCK_PADDING; // top padding

            int lineCount = block.Lines.Count;
            if (block.Collapsed && block.SummaryLineCount > 0)
            {
                lineCount = block.SummaryLineCount;
            }

            for (int i = 0; i < lineCount; i++)
            {
                totalH += MeasureLine(g, block.Lines[i], contentWidth);
            }

            totalH += BLOCK_PADDING; // bottom padding

            block.CachedHeight = totalH;
            block.CachedWidth = width;
            return totalH;
        }

        private int MeasureLine(Graphics g, DisplayLine line, int width)
        {
            int availWidth = width - line.LeftPadding;
            if (availWidth < 30) availWidth = 30;

            // Get the tallest font's line height
            int lineH = 0;
            StringBuilder fullText = new StringBuilder();
            Font measureFont = _fontNormal;

            for (int i = 0; i < line.Spans.Count; i++)
            {
                fullText.Append(line.Spans[i].Text);
                int h = line.Spans[i].Font.Height;
                if (h > lineH) lineH = h;
                if (i == 0) measureFont = line.Spans[i].Font;
            }

            string text = fullText.ToString();
            if (text.Length == 0) return lineH > 0 ? lineH : _fontNormal.Height;

            // Use TextRenderer to measure wrapped height
            Size proposed = new Size(availWidth, int.MaxValue);
            Size measured = TextRenderer.MeasureText(g, text, measureFont, proposed,
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

            return measured.Height > lineH ? measured.Height : lineH;
        }

        // ================================================================
        // Painting
        // ================================================================

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.TextRenderingHint = _textHint;

            int width = ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0);
            int scrollOffset = _scrollBar.Visible ? _scrollBar.Value : 0;
            int y = -scrollOffset;

            _paintedLines.Clear();
            int totalH = 0;

            for (int bi = 0; bi < _blocks.Count; bi++)
            {
                ChatBlock block = _blocks[bi];
                int blockH = MeasureBlock(g, block, width);

                // Skip blocks above visible area
                if (y + blockH + BLOCK_MARGIN < 0)
                {
                    y += blockH + BLOCK_MARGIN;
                    totalH += blockH + BLOCK_MARGIN;
                    continue;
                }

                // Stop if below visible area
                if (y > ClientSize.Height)
                {
                    totalH += blockH + BLOCK_MARGIN;
                    // Accumulate remaining heights using cache when available,
                    // estimating uncached blocks to avoid expensive measurement
                    for (int j = bi + 1; j < _blocks.Count; j++)
                    {
                        ChatBlock remaining = _blocks[j];
                        if (remaining.CachedHeight >= 0 && remaining.CachedWidth == width)
                        {
                            totalH += remaining.CachedHeight + BLOCK_MARGIN;
                        }
                        else
                        {
                            // Estimate: 20px per line is a reasonable default
                            int lineCount = remaining.Lines.Count;
                            if (lineCount == 0) lineCount = 1;
                            int estimate = BLOCK_PADDING * 2 + lineCount * 20;
                            totalH += estimate + BLOCK_MARGIN;
                        }
                    }
                    break;
                }

                PaintBlock(g, block, bi, y, width, blockH);
                y += blockH + BLOCK_MARGIN;
                totalH += blockH + BLOCK_MARGIN;
            }

            _totalHeight = totalH;
        }

        private void PaintBlock(Graphics g, ChatBlock block, int blockIndex, int y, int width, int blockH)
        {
            int left = CONTENT_MARGIN;
            int blockWidth = width - CONTENT_MARGIN;

            // Background fill
            if (block.BackColor != Color.Empty)
            {
                SolidBrush bgBrush = GetCachedBrush(block.BackColor);
                if (bgBrush != null)
                {
                    g.FillRectangle(bgBrush, left, y, blockWidth, blockH);
                }
                else
                {
                    using (SolidBrush bg = new SolidBrush(block.BackColor))
                    {
                        g.FillRectangle(bg, left, y, blockWidth, blockH);
                    }
                }
            }

            // Left accent border
            if (block.AccentColor != Color.Empty)
            {
                SolidBrush accentBrush = GetCachedBrush(block.AccentColor);
                if (accentBrush != null)
                {
                    g.FillRectangle(accentBrush, left, y, ACCENT_WIDTH, blockH);
                }
                else
                {
                    using (SolidBrush accent = new SolidBrush(block.AccentColor))
                    {
                        g.FillRectangle(accent, left, y, ACCENT_WIDTH, blockH);
                    }
                }
            }

            // Draw file icon for file artifact blocks
            if (block.FilePath != null)
            {
                try
                {
                    string ext = System.IO.Path.GetExtension(block.FilePath);
                    if (ext == null) ext = "";
                    ext = ext.ToLower();

                    Icon fileIcon = null;
                    if (_iconCache.ContainsKey(ext))
                    {
                        fileIcon = _iconCache[ext];
                    }
                    else if (System.IO.File.Exists(block.FilePath))
                    {
                        fileIcon = System.Drawing.Icon.ExtractAssociatedIcon(block.FilePath);
                        if (fileIcon != null)
                        {
                            _iconCache[ext] = fileIcon;
                        }
                    }

                    if (fileIcon != null)
                    {
                        int contentLeftIcon = left + ACCENT_WIDTH + BLOCK_PADDING;
                        g.DrawIcon(fileIcon, new Rectangle(contentLeftIcon + 2, y + BLOCK_PADDING + 1, 16, 16));
                    }
                    else
                    {
                        // Draw a generic document icon placeholder
                        int contentLeftIcon = left + ACCENT_WIDTH + BLOCK_PADDING;
                        using (Pen iconPen = new Pen(Color.FromArgb(120, 148, 168)))
                        {
                            g.DrawRectangle(iconPen, contentLeftIcon + 2, y + BLOCK_PADDING + 1, 12, 15);
                            g.DrawLine(iconPen, contentLeftIcon + 10, y + BLOCK_PADDING + 1, contentLeftIcon + 14, y + BLOCK_PADDING + 5);
                            g.DrawLine(iconPen, contentLeftIcon + 14, y + BLOCK_PADDING + 5, contentLeftIcon + 14, y + BLOCK_PADDING + 16);
                        }
                    }
                }
                catch { }
            }

            // Paint lines
            int contentLeft = left + ACCENT_WIDTH + BLOCK_PADDING;
            int contentWidth = blockWidth - ACCENT_WIDTH - BLOCK_PADDING * 2;
            int lineY = y + BLOCK_PADDING;

            int lineCount = block.Lines.Count;
            if (block.Collapsed && block.SummaryLineCount > 0)
            {
                lineCount = block.SummaryLineCount;
            }

            for (int i = 0; i < lineCount; i++)
            {
                DisplayLine line = block.Lines[i];
                int lineH = MeasureLine(g, line, contentWidth);
                int lineLeft = contentLeft + line.LeftPadding;
                int lineWidth = contentWidth - line.LeftPadding;

                bool selected = IsLineSelected(blockIndex, i);

                // Selection highlight background
                if (selected)
                {
                    g.FillRectangle(_highlightBrush, contentLeft, lineY, contentWidth, lineH);
                }
                // Code line background (only if not selected)
                else if (line.BackColor != Color.Empty)
                {
                    g.FillRectangle(_brushBgCode, contentLeft, lineY, contentWidth, lineH);
                    // Draw borders at code block boundaries
                    bool prevIsCode = i > 0 && block.Lines[i - 1].BackColor != Color.Empty;
                    bool nextIsCode = i + 1 < block.Lines.Count && block.Lines[i + 1].BackColor != Color.Empty;
                    g.DrawLine(_codeBorderPen, contentLeft, lineY, contentLeft + contentWidth, lineY);  // left edge always
                    g.DrawLine(_codeBorderPen, contentLeft + contentWidth, lineY, contentLeft + contentWidth, lineY + lineH);  // right
                    g.DrawLine(_codeBorderPen, contentLeft, lineY, contentLeft, lineY + lineH);  // left
                    if (!nextIsCode)
                    {
                        g.DrawLine(_codeBorderPen, contentLeft, lineY + lineH, contentLeft + contentWidth, lineY + lineH);  // bottom
                    }
                    if (!prevIsCode)
                    {
                        g.DrawLine(_codeBorderPen, contentLeft, lineY, contentLeft + contentWidth, lineY);  // top
                    }
                }

                PaintLine(g, line, lineLeft, lineY, lineWidth, lineH, selected);

                // Record painted line for hit testing
                PaintedLine pl;
                pl.BlockIndex = blockIndex;
                pl.LineIndex = i;
                pl.Y = lineY;
                pl.Height = lineH;
                StringBuilder plText = new StringBuilder();
                for (int s = 0; s < line.Spans.Count; s++)
                {
                    plText.Append(line.Spans[s].Text);
                }
                pl.Text = plText.ToString();
                _paintedLines.Add(pl);

                lineY += lineH;
            }
        }

        private void PaintLine(Graphics g, DisplayLine line, int x, int y, int width, int height, bool isSelected)
        {
            if (line.Spans.Count == 0) return;

            // Single span: use TextRenderer with WordBreak for proper wrapping
            if (line.Spans.Count == 1)
            {
                TextSpan span = line.Spans[0];
                Color textColor = isSelected ? SystemColors.HighlightText : span.Color;
                Rectangle bounds = new Rectangle(x, y, width, height);
                TextRenderer.DrawText(g, span.Text, span.Font, bounds, textColor,
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                return;
            }

            // Multi-span: paint left-to-right with simple wrapping
            int curX = x;
            int curY = y;
            int maxLineH = _fontNormal.Height;

            for (int i = 0; i < line.Spans.Count; i++)
            {
                TextSpan span = line.Spans[i];
                if (span.Text.Length == 0) continue;

                Color textColor = isSelected ? SystemColors.HighlightText : span.Color;

                Size size = TextRenderer.MeasureText(g, span.Text, span.Font,
                    new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);

                // Wrap if needed
                if (curX + size.Width > x + width && curX > x)
                {
                    curX = x;
                    curY += maxLineH;
                }

                TextRenderer.DrawText(g, span.Text, span.Font,
                    new Point(curX, curY), textColor);

                curX += size.Width;
                if (span.Font.Height > maxLineH) maxLineH = span.Font.Height;
            }
        }

        // ================================================================
        // Text selection
        // ================================================================

        private bool IsLineSelected(int blockIdx, int lineIdx)
        {
            if (!_hasSelection) return false;

            // Only allow selection on user and assistant message blocks
            if (blockIdx >= 0 && blockIdx < _blocks.Count)
            {
                string role = _blocks[blockIdx].Role;
                if (role != "user" && role != "assistant") return false;
            }

            // Normalize so start <= end
            int sb = _selStartBlock;
            int sl = _selStartLine;
            int eb = _selEndBlock;
            int el = _selEndLine;
            if (sb > eb || (sb == eb && sl > el))
            {
                sb = _selEndBlock;
                sl = _selEndLine;
                eb = _selStartBlock;
                el = _selStartLine;
            }

            if (blockIdx < sb || blockIdx > eb) return false;
            if (blockIdx == sb && blockIdx == eb) return lineIdx >= sl && lineIdx <= el;
            if (blockIdx == sb) return lineIdx >= sl;
            if (blockIdx == eb) return lineIdx <= el;
            return true; // between start and end blocks
        }

        private int FindLineAt(int mouseY)
        {
            for (int i = 0; i < _paintedLines.Count; i++)
            {
                if (mouseY >= _paintedLines[i].Y && mouseY < _paintedLines[i].Y + _paintedLines[i].Height)
                    return i;
            }
            // If below all lines, return last
            if (_paintedLines.Count > 0 && mouseY >= _paintedLines[_paintedLines.Count - 1].Y)
                return _paintedLines.Count - 1;
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            // Take focus for keyboard events
            Focus();

            int idx = FindLineAt(e.Y);
            if (idx < 0)
            {
                _hasSelection = false;
                InvalidateIfNotSuspended();
                return;
            }

            PaintedLine pl = _paintedLines[idx];
            _selStartBlock = pl.BlockIndex;
            _selStartLine = pl.LineIndex;
            _selEndBlock = pl.BlockIndex;
            _selEndLine = pl.LineIndex;
            _isSelecting = true;
            _hasSelection = false;
            InvalidateIfNotSuspended();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            // Update cursor based on what's under the mouse
            if (!_isSelecting)
            {
                int hoverIdx = FindLineAt(e.Y);
                if (hoverIdx >= 0)
                {
                    PaintedLine hpl = _paintedLines[hoverIdx];
                    if (hpl.BlockIndex >= 0 && hpl.BlockIndex < _blocks.Count
                        && (_blocks[hpl.BlockIndex].Collapsible || _blocks[hpl.BlockIndex].FilePath != null))
                    {
                        Cursor = Cursors.Hand;
                    }
                    else
                    {
                        Cursor = Cursors.IBeam;
                    }
                }
                else
                {
                    Cursor = Cursors.IBeam;
                }
            }

            if (!_isSelecting) return;
            if (e.Button != MouseButtons.Left) return;

            int idx = FindLineAt(e.Y);
            if (idx < 0) return;

            PaintedLine pl = _paintedLines[idx];
            _selEndBlock = pl.BlockIndex;
            _selEndLine = pl.LineIndex;

            // Selection exists if start != end
            _hasSelection = (_selStartBlock != _selEndBlock || _selStartLine != _selEndLine);
            InvalidateIfNotSuspended();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            if (_isSelecting)
            {
                _isSelecting = false;
                // If start == end, treat as click
                if (_selStartBlock == _selEndBlock && _selStartLine == _selEndLine)
                {
                    _hasSelection = false;

                    // Toggle collapse on tool blocks
                    int idx = FindLineAt(e.Y);
                    if (idx >= 0)
                    {
                        PaintedLine pl = _paintedLines[idx];
                        if (pl.BlockIndex >= 0 && pl.BlockIndex < _blocks.Count)
                        {
                            ChatBlock block = _blocks[pl.BlockIndex];
                            if (block.Collapsible)
                            {
                                block.Collapsed = !block.Collapsed;
                                // Update the prefix on the summary line
                                if (block.Lines.Count > 0 && block.Lines[0].Spans.Count > 0)
                                {
                                    TextSpan first = block.Lines[0].Spans[0];
                                    string text = first.Text;
                                    if (text.Length > 4 && (text.StartsWith("[+] ") || text.StartsWith("[-] ")))
                                    {
                                        string newPrefix = block.Collapsed ? "[+] " : "[-] ";
                                        block.Lines[0].Spans[0] = new TextSpan(
                                            newPrefix + text.Substring(4),
                                            first.Font, first.Color);
                                    }
                                }
                                block.InvalidateCache();
                            }

                            // Open file artifact on click
                            if (block.FilePath != null)
                            {
                                try
                                {
                                    System.Diagnostics.Process.Start(block.FilePath);
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            InvalidateIfNotSuspended();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode == Keys.C && _hasSelection)
            {
                CopySelection();
                e.Handled = true;
            }
        }

        private void CopySelection()
        {
            // Normalize selection range
            int sb = _selStartBlock;
            int sl = _selStartLine;
            int eb = _selEndBlock;
            int el = _selEndLine;
            if (sb > eb || (sb == eb && sl > el))
            {
                sb = _selEndBlock;
                sl = _selEndLine;
                eb = _selStartBlock;
                el = _selStartLine;
            }

            StringBuilder text = new StringBuilder();
            for (int b = sb; b <= eb && b < _blocks.Count; b++)
            {
                ChatBlock block = _blocks[b];
                int startLine = (b == sb) ? sl : 0;
                int endLine = (b == eb) ? el : block.Lines.Count - 1;
                for (int l = startLine; l <= endLine && l < block.Lines.Count; l++)
                {
                    DisplayLine dl = block.Lines[l];
                    for (int s = 0; s < dl.Spans.Count; s++)
                    {
                        text.Append(dl.Spans[s].Text);
                    }
                    text.Append("\r\n");
                }
                if (b < eb) text.Append("\r\n"); // extra line between blocks
            }

            string result = text.ToString().TrimEnd();
            if (result.Length > 0)
            {
                try
                {
                    Clipboard.SetText(result);
                }
                catch { }
            }
        }

        // ================================================================
        // Scrolling
        // ================================================================

        private void UpdateScrollBar()
        {
            if (!IsHandleCreated) return;

            // Always recompute _totalHeight by measuring all blocks
            using (Graphics g = CreateGraphics())
            {
                int width = ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0);
                int h = 0;
                for (int i = 0; i < _blocks.Count; i++)
                {
                    h += MeasureBlock(g, _blocks[i], width) + BLOCK_MARGIN;
                }
                _totalHeight = h;
            }

            int viewHeight = ClientSize.Height;

            if (_totalHeight > viewHeight)
            {
                _scrollBar.Visible = true;
                _scrollBar.Minimum = 0;
                _scrollBar.Maximum = _totalHeight;
                _scrollBar.LargeChange = viewHeight;
                _scrollBar.SmallChange = 30;

                if (_autoScroll)
                {
                    _scrollBar.Value = Math.Max(0, _totalHeight - viewHeight);
                }
            }
            else
            {
                _scrollBar.Visible = false;
                _scrollBar.Value = 0;
            }
        }

        private void OnScroll(object sender, ScrollEventArgs e)
        {
            // If user scrolls up, disable auto-scroll
            if (e.Type != ScrollEventType.EndScroll)
            {
                int maxVal = Math.Max(0, _totalHeight - ClientSize.Height);
                _autoScroll = (_scrollBar.Value >= maxVal - 30);
            }
            InvalidateIfNotSuspended();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!_scrollBar.Visible) return;

            int delta = -(e.Delta / 120) * 60;
            int newVal = _scrollBar.Value + delta;
            if (newVal < 0) newVal = 0;
            int maxVal = Math.Max(0, _scrollBar.Maximum - _scrollBar.LargeChange + 1);
            if (newVal > maxVal) newVal = maxVal;
            _scrollBar.Value = newVal;

            _autoScroll = (newVal >= maxVal - 30);
            InvalidateIfNotSuspended();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Invalidate all block caches since width changed
            for (int i = 0; i < _blocks.Count; i++)
            {
                _blocks[i].InvalidateCache();
            }
            _totalHeight = 0;
            UpdateScrollBar();
            InvalidateIfNotSuspended();
        }

        // ================================================================
        // Utility methods
        // ================================================================

        private static SolidBrush GetCachedBrush(Color color)
        {
            if (color == BgUser) return _brushBgUser;
            if (color == BgAssistant) return _brushBgAssistant;
            if (color == BgTool) return _brushBgTool;
            if (color == BgError) return _brushBgError;
            if (color == BgReasoning) return _brushBgReasoning;
            if (color == BgCode) return _brushBgCode;
            if (color == AccentUser) return _brushAccentUser;
            if (color == AccentAssistant) return _brushAccentAssistant;
            if (color == AccentTool) return _brushAccentTool;
            if (color == AccentError) return _brushAccentError;
            if (color == AccentReasoning) return _brushAccentReasoning;
            // Check the file artifact background color
            if (color.A == 255 && color.R == 250 && color.G == 248 && color.B == 245)
                return _brushBgFileArtifact;
            return null;
        }

        private static string StripEmoji(string text)
        {
            if (text == null) return text;
            StringBuilder sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++;
                    continue;
                }
                sb.Append(text[i]);
            }
            return sb.ToString();
        }

        private static bool StartsWith(string text, string prefix)
        {
            if (text.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (text[i] != prefix[i]) return false;
            }
            return true;
        }

        private static bool IsHorizontalRule(string line)
        {
            string t = line.Trim();
            if (t.Length < 3) return false;
            char ruleChar = '\0';
            int count = 0;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c == ' ') continue;
                if (c == '-' || c == '*' || c == '_')
                {
                    if (ruleChar == '\0') ruleChar = c;
                    else if (c != ruleChar) return false;
                    count++;
                }
                else return false;
            }
            return count >= 3;
        }

        private static bool IsOrderedListItem(string line, out int contentStart, out int indentSpaces)
        {
            contentStart = 0;
            indentSpaces = 0;
            int i = 0;
            while (i < line.Length && line[i] == ' ') { indentSpaces++; i++; }
            if (i >= line.Length || line[i] < '0' || line[i] > '9') return false;
            while (i < line.Length && line[i] >= '0' && line[i] <= '9') i++;
            if (i + 1 >= line.Length || line[i] != '.' || line[i + 1] != ' ') return false;
            contentStart = i + 2;
            return true;
        }

        private static bool IsTableSeparator(string row)
        {
            string trimmed = row.Trim();
            if (trimmed.Length < 3 || trimmed[0] != '|') return false;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (c != '|' && c != '-' && c != ':' && c != ' ') return false;
            }
            return true;
        }

        private static string[] ParseTableRow(string row)
        {
            string trimmed = row.Trim();
            if (trimmed.Length > 0 && trimmed[0] == '|') trimmed = trimmed.Substring(1);
            if (trimmed.Length > 0 && trimmed[trimmed.Length - 1] == '|')
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            string[] parts = trimmed.Split('|');
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            return parts;
        }
    }
}
