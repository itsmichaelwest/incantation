using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Incantation.Chat
{
    public class ChatRenderer : IChatRenderer
    {
        private RichTextBox _rtb;
        private bool _inCodeBlock;
        private bool _inReasoning;
        private Font _normalFont;
        private Font _boldFont;
        private Font _italicFont;
        private Font _codeFont;
        private Font _reasoningFont;
        private Font _headerFont;
        private Font _subheaderFont;
        private Font _inlineCodeFont;
        private Font _strikeFont;

        private StringBuilder _lineBuffer;
        private bool _skippingLangHint;
        private bool _inTable;
        private System.Collections.Generic.List<string> _tableRows;

        private static readonly Color ColorUserName = Color.FromArgb(0, 51, 153);
        private static readonly Color ColorAssistantName = Color.FromArgb(0, 100, 0);
        private static readonly Color ColorTimestamp = Color.Gray;
        private static readonly Color ColorContent = Color.Black;
        private static readonly Color ColorToolCall = Color.FromArgb(200, 100, 0);
        private static readonly Color ColorError = Color.Red;
        private static readonly Color ColorSystem = Color.Gray;
        private static readonly Color ColorReasoning = Color.FromArgb(140, 140, 140);
        private static readonly Color ColorCodeBackground = Color.FromArgb(245, 245, 245);
        private static readonly Color ColorBullet = Color.FromArgb(100, 100, 100);
        private static readonly Color ColorHeader = Color.FromArgb(0, 51, 153);
        private static readonly Color ColorBlockquote = Color.FromArgb(100, 100, 100);
        private static readonly Color ColorLink = Color.FromArgb(0, 0, 200);

        private const int WM_SETREDRAW = 0x000B;

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        public ChatRenderer(RichTextBox rtb)
        {
            _rtb = rtb;
            _inCodeBlock = false;
            _inReasoning = false;
            _lineBuffer = new StringBuilder();
            _skippingLangHint = false;
            _inTable = false;
            _tableRows = new System.Collections.Generic.List<string>();
            _normalFont = new Font("Tahoma", 8.25f, FontStyle.Regular);
            _boldFont = new Font("Tahoma", 8.25f, FontStyle.Bold);
            _italicFont = new Font("Tahoma", 8.25f, FontStyle.Italic);
            _codeFont = new Font("Lucida Console", 9f, FontStyle.Regular);
            _reasoningFont = new Font("Tahoma", 8f, FontStyle.Italic);
            _headerFont = new Font("Tahoma", 12f, FontStyle.Bold);
            _subheaderFont = new Font("Tahoma", 10f, FontStyle.Bold);
            _inlineCodeFont = new Font("Lucida Console", 8.5f, FontStyle.Regular);
            _strikeFont = new Font("Tahoma", 8.25f, FontStyle.Strikeout);
        }

        public void SuspendPainting()
        {
            SendMessage(_rtb.Handle, WM_SETREDRAW, 0, 0);
        }

        public void ResumePainting()
        {
            SendMessage(_rtb.Handle, WM_SETREDRAW, 1, 0);
            _rtb.Invalidate();
        }

        public void ScrollToEnd()
        {
            _rtb.SelectionStart = _rtb.TextLength;
            _rtb.ScrollToCaret();
        }

        public void AppendUserMessage(string name, DateTime time, string content)
        {
            string timeStr = time.ToString("HH:mm");
            AppendFormatted(name + " ", _boldFont, ColorUserName);
            AppendFormatted("[" + timeStr + "]", _normalFont, ColorTimestamp);
            AppendFormatted("\n", _normalFont, ColorContent);
            AppendFormatted(content, _normalFont, ColorContent);
            AppendFormatted("\n\n", _normalFont, ColorContent);
        }

        public void AppendAssistantHeader(string name, DateTime time)
        {
            string timeStr = time.ToString("HH:mm");
            AppendFormatted(name + " ", _boldFont, ColorAssistantName);
            AppendFormatted("[" + timeStr + "]", _normalFont, ColorTimestamp);
            AppendFormatted("\n", _normalFont, ColorContent);
        }

        public void AppendReasoning(string text)
        {
            if (text == null)
            {
                return;
            }
            if (!_inReasoning)
            {
                _inReasoning = true;
                AppendFormatted("thinking: ", _reasoningFont, ColorReasoning);
            }
            AppendFormatted(text, _reasoningFont, ColorReasoning);
        }

        public void EndReasoning()
        {
            if (_inReasoning)
            {
                _inReasoning = false;
                AppendFormatted("\n", _reasoningFont, ColorReasoning);
            }
        }

        public void AppendDelta(string text)
        {
            if (text == null)
            {
                return;
            }

            text = StripEmoji(text);

            // End reasoning block when regular content starts
            if (_inReasoning)
            {
                EndReasoning();
            }

            // Append incoming text to the line buffer
            _lineBuffer.Append(text);

            // Process complete lines from the buffer
            ProcessLineBuffer();
        }

        public void AppendToolCall(string summary, string detail)
        {
            // Summary line in bold orange
            Font boldItalic = new Font("Tahoma", 8.25f, FontStyle.Bold | FontStyle.Italic);
            AppendFormatted("> " + summary + "\n", boldItalic, ColorToolCall);
            boldItalic.Dispose();

            // Detail in small gray if available and not too long
            if (detail != null && detail.Length > 0 && detail.Length < 500)
            {
                Font detailFont = new Font("Lucida Console", 7f, FontStyle.Regular);
                // Try to pretty-print JSON input
                string detailText = detail;
                try
                {
                    Newtonsoft.Json.Linq.JObject parsed = Newtonsoft.Json.Linq.JObject.Parse(detail);
                    detailText = parsed.ToString(Newtonsoft.Json.Formatting.Indented);
                }
                catch { }

                // Indent each line
                string[] lines = detailText.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    AppendFormatted("  " + lines[i].TrimEnd() + "\n", detailFont, ColorReasoning);
                }
                detailFont.Dispose();
            }
        }

        // Overload for backward compatibility
        public void AppendToolCall(string summary)
        {
            AppendToolCall(summary, null);
        }

        public void AppendError(string message)
        {
            AppendFormatted("Error: ", _boldFont, ColorError);
            AppendFormatted(message + "\n", _normalFont, ColorError);
        }

        public void AppendSystemMessage(string text)
        {
            AppendFormatted(text + "\n", _italicFont, ColorSystem);
        }

        public void AppendNewline()
        {
            _rtb.AppendText("\n");
        }

        public void FinalizeMessage()
        {
            // Flush any remaining text in the line buffer
            FlushLineBuffer();

            if (_inTable)
            {
                FlushTable();
                _inTable = false;
            }

            _inCodeBlock = false;
            _inReasoning = false;
            _skippingLangHint = false;
            AppendFormatted("\n\n", _normalFont, ColorContent);
        }

        private void ProcessLineBuffer()
        {
            // Extract and render complete lines (ending with \n)
            string bufferStr = _lineBuffer.ToString();
            int lastNewline = bufferStr.LastIndexOf('\n');

            if (lastNewline < 0)
            {
                // No complete line yet, keep buffering
                return;
            }

            // Split out complete lines and the remaining partial
            string completePart = bufferStr.Substring(0, lastNewline + 1);
            string remaining = bufferStr.Substring(lastNewline + 1);

            _lineBuffer = new StringBuilder(remaining);

            // Process each complete line
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
        }

        private void FlushLineBuffer()
        {
            if (_lineBuffer.Length == 0)
            {
                return;
            }

            string remaining = _lineBuffer.ToString();
            _lineBuffer = new StringBuilder();

            if (remaining.Length > 0)
            {
                RenderMarkdownLine(remaining);
            }
        }

        private void RenderMarkdownLine(string line)
        {
            // If we are skipping a language hint after ```, consume it
            if (_skippingLangHint)
            {
                _skippingLangHint = false;
                // The line after ``` opening is the language hint; skip it entirely
                // (it was already consumed when we entered code block mode)
                // Actually, the lang hint is on the SAME line as ```, so this
                // flag means the ``` was at end of delta without a newline.
                // Now we got the newline, just skip this line.
                return;
            }

            // Check for code block toggle
            string trimmed = line.TrimStart();
            if (StartsWith(trimmed, "```"))
            {
                _inCodeBlock = !_inCodeBlock;
                if (_inCodeBlock)
                {
                    // Entering code block - skip language hint (rest of this line)
                    // Don't render anything
                }
                else
                {
                    // Exiting code block - don't render the closing ```
                }
                return;
            }

            // Inside a code block, render raw with code font
            if (_inCodeBlock)
            {
                AppendCodeText(line + "\n");
                return;
            }

            // Table handling
            bool isTableRow = trimmed.Length > 0
                && trimmed[0] == '|'
                && trimmed[trimmed.Length - 1] == '|';

            if (_inTable)
            {
                if (isTableRow)
                {
                    _tableRows.Add(line);
                    return;
                }
                else
                {
                    FlushTable();
                    _inTable = false;
                    // Fall through to render this non-table line normally
                }
            }
            else if (isTableRow)
            {
                _inTable = true;
                _tableRows = new System.Collections.Generic.List<string>();
                _tableRows.Add(line);
                return;
            }

            // Empty line
            if (line.Length == 0)
            {
                AppendFormatted("\n", _normalFont, ColorContent);
                return;
            }

            // Horizontal rule: --- or *** or ___
            if (IsHorizontalRule(line))
            {
                AppendFormatted("----------------------------------------\n",
                    _normalFont, ColorTimestamp);
                return;
            }

            // Headers
            if (StartsWith(line, "### "))
            {
                string headerText = line.Substring(4);
                AppendFormatted(headerText + "\n", _boldFont, ColorHeader);
                return;
            }
            if (StartsWith(line, "## "))
            {
                string headerText = line.Substring(3);
                AppendFormatted(headerText + "\n", _subheaderFont, ColorHeader);
                return;
            }
            if (StartsWith(line, "# "))
            {
                string headerText = line.Substring(2);
                AppendFormatted(headerText + "\n", _headerFont, ColorHeader);
                return;
            }

            // Blockquotes: > text
            if (StartsWith(trimmed, "> "))
            {
                string quoteText = trimmed.Substring(2);
                // Strip nested > prefixes
                while (StartsWith(quoteText, "> "))
                {
                    quoteText = quoteText.Substring(2);
                }
                if (StartsWith(quoteText, ">"))
                {
                    quoteText = quoteText.Substring(1);
                }
                AppendFormatted("  | ", _normalFont, ColorBlockquote);
                RenderInlineMarkdown(quoteText);
                AppendFormatted("\n", _normalFont, ColorContent);
                return;
            }
            if (trimmed == ">")
            {
                AppendFormatted("\n", _normalFont, ColorContent);
                return;
            }

            // Bullet list items with nesting: [spaces]- item or [spaces]* item
            {
                int indent = 0;
                int idx = 0;
                while (idx < line.Length && line[idx] == ' ')
                {
                    indent++;
                    idx++;
                }
                if (idx + 1 < line.Length
                    && (line[idx] == '-' || line[idx] == '*')
                    && line[idx + 1] == ' '
                    && !IsHorizontalRule(line))
                {
                    string itemText = line.Substring(idx + 2);
                    int nestLevel = indent / 2;
                    string indentStr = new string(' ', 2 + nestLevel * 4);
                    AppendFormatted(indentStr + "\u2022 ", _normalFont, ColorBullet);
                    RenderInlineMarkdown(itemText);
                    AppendFormatted("\n", _normalFont, ColorContent);
                    return;
                }
            }

            // Ordered list items: [spaces]N. item
            {
                int olContentStart;
                int olIndent;
                if (IsOrderedListItem(line, out olContentStart, out olIndent))
                {
                    int nestLevel = olIndent / 2;
                    string prefix = line.Substring(olIndent, olContentStart - olIndent);
                    string itemText = line.Substring(olContentStart);
                    string indentStr = new string(' ', 2 + nestLevel * 4);
                    AppendFormatted(indentStr + prefix, _normalFont, ColorBullet);
                    RenderInlineMarkdown(itemText);
                    AppendFormatted("\n", _normalFont, ColorContent);
                    return;
                }
            }

            // Regular line: render inline markdown + newline
            RenderInlineMarkdown(line);
            AppendFormatted("\n", _normalFont, ColorContent);
        }

        private void RenderInlineMarkdown(string text)
        {
            // States: 0=normal, 1=bold, 2=italic, 3=inline code, 4=strikethrough
            const int STATE_NORMAL = 0;
            const int STATE_BOLD = 1;
            const int STATE_ITALIC = 2;
            const int STATE_CODE = 3;
            const int STATE_STRIKE = 4;

            int state = STATE_NORMAL;
            StringBuilder buf = new StringBuilder();
            int len = text.Length;

            for (int i = 0; i < len; i++)
            {
                char c = text[i];

                if (c == '`')
                {
                    if (state == STATE_CODE)
                    {
                        // Flush code buffer
                        FlushInline(buf, STATE_CODE);
                        buf = new StringBuilder();
                        state = STATE_NORMAL;
                    }
                    else
                    {
                        // Flush current buffer, enter code mode
                        FlushInline(buf, state);
                        buf = new StringBuilder();
                        state = STATE_CODE;
                    }
                    continue;
                }

                // Don't parse markdown inside inline code
                if (state == STATE_CODE)
                {
                    buf.Append(c);
                    continue;
                }

                // Strikethrough toggle: ~~
                if (c == '~' && i + 1 < len && text[i + 1] == '~')
                {
                    FlushInline(buf, state);
                    buf = new StringBuilder();
                    if (state == STATE_STRIKE)
                    {
                        state = STATE_NORMAL;
                    }
                    else
                    {
                        state = STATE_STRIKE;
                    }
                    i++; // skip second ~
                    continue;
                }

                // Links: [text](url)
                if (c == '[')
                {
                    int closeBracket = text.IndexOf(']', i + 1);
                    if (closeBracket > i + 1
                        && closeBracket + 1 < len
                        && text[closeBracket + 1] == '(')
                    {
                        int closeParen = text.IndexOf(')', closeBracket + 2);
                        if (closeParen > closeBracket + 2)
                        {
                            FlushInline(buf, state);
                            buf = new StringBuilder();

                            string linkText = text.Substring(i + 1, closeBracket - i - 1);
                            string linkUrl = text.Substring(closeBracket + 2, closeParen - closeBracket - 2);

                            AppendFormatted(linkText, _normalFont, ColorLink);
                            AppendFormatted(" (" + linkUrl + ")", _normalFont, ColorTimestamp);

                            i = closeParen; // loop will i++ past the )
                            continue;
                        }
                    }
                    buf.Append(c);
                    continue;
                }

                if (c == '*')
                {
                    // Check for ** (bold toggle)
                    if (i + 1 < len && text[i + 1] == '*')
                    {
                        // Flush current buffer
                        FlushInline(buf, state);
                        buf = new StringBuilder();

                        if (state == STATE_BOLD)
                        {
                            state = STATE_NORMAL;
                        }
                        else
                        {
                            state = STATE_BOLD;
                        }

                        i++; // skip the second *
                        continue;
                    }
                    else
                    {
                        // Single * - italic toggle
                        FlushInline(buf, state);
                        buf = new StringBuilder();

                        if (state == STATE_ITALIC)
                        {
                            state = STATE_NORMAL;
                        }
                        else
                        {
                            state = STATE_ITALIC;
                        }
                        continue;
                    }
                }

                buf.Append(c);
            }

            // Flush remaining buffer
            if (buf.Length > 0)
            {
                FlushInline(buf, state);
            }
        }

        private void FlushInline(StringBuilder buf, int state)
        {
            if (buf.Length == 0)
            {
                return;
            }

            string text = buf.ToString();

            switch (state)
            {
                case 1: // bold
                    AppendFormatted(text, _boldFont, ColorContent);
                    break;
                case 2: // italic
                    AppendFormatted(text, _italicFont, ColorContent);
                    break;
                case 3: // inline code
                    AppendInlineCode(text);
                    break;
                case 4: // strikethrough
                    AppendFormatted(text, _strikeFont, ColorContent);
                    break;
                default: // normal
                    AppendFormatted(text, _normalFont, ColorContent);
                    break;
            }
        }

        private void AppendInlineCode(string text)
        {
            int start = _rtb.TextLength;
            _rtb.AppendText(text);
            _rtb.Select(start, text.Length);
            _rtb.SelectionFont = _inlineCodeFont;
            _rtb.SelectionColor = ColorContent;
            _rtb.SelectionBackColor = ColorCodeBackground;
            _rtb.Select(_rtb.TextLength, 0);
        }

        private void FlushTable()
        {
            if (_tableRows.Count == 0)
            {
                return;
            }

            // Parse all rows into cells, skipping separator rows
            System.Collections.Generic.List<string[]> parsed = new System.Collections.Generic.List<string[]>();
            int maxCols = 0;

            for (int r = 0; r < _tableRows.Count; r++)
            {
                string row = _tableRows[r].Trim();
                if (IsTableSeparator(row))
                {
                    continue;
                }
                string[] cells = ParseTableRow(row);
                if (cells.Length > maxCols)
                {
                    maxCols = cells.Length;
                }
                parsed.Add(cells);
            }

            if (parsed.Count == 0 || maxCols == 0)
            {
                return;
            }

            // Calculate column widths
            int[] widths = new int[maxCols];
            for (int r = 0; r < parsed.Count; r++)
            {
                for (int c = 0; c < parsed[r].Length; c++)
                {
                    if (parsed[r][c].Length > widths[c])
                    {
                        widths[c] = parsed[r][c].Length;
                    }
                }
            }

            // Render each row in code font with padded columns
            for (int r = 0; r < parsed.Count; r++)
            {
                StringBuilder sb = new StringBuilder("  ");
                for (int c = 0; c < maxCols; c++)
                {
                    string cell = c < parsed[r].Length ? parsed[r][c] : "";
                    sb.Append(cell);
                    int pad = widths[c] - cell.Length + 2;
                    for (int p = 0; p < pad; p++)
                    {
                        sb.Append(' ');
                    }
                }
                AppendFormatted(sb.ToString() + "\n", _codeFont, ColorContent);

                // Render separator after header row
                if (r == 0)
                {
                    StringBuilder sep = new StringBuilder("  ");
                    for (int c = 0; c < maxCols; c++)
                    {
                        for (int d = 0; d < widths[c]; d++)
                        {
                            sep.Append('-');
                        }
                        sep.Append("  ");
                    }
                    AppendFormatted(sep.ToString() + "\n", _codeFont, ColorTimestamp);
                }
            }

            _tableRows.Clear();
        }

        private static bool IsTableSeparator(string row)
        {
            string trimmed = row.Trim();
            if (trimmed.Length < 3 || trimmed[0] != '|')
            {
                return false;
            }
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (c != '|' && c != '-' && c != ':' && c != ' ')
                {
                    return false;
                }
            }
            return true;
        }

        private static string[] ParseTableRow(string row)
        {
            string trimmed = row.Trim();
            // Remove leading and trailing pipes
            if (trimmed.Length > 0 && trimmed[0] == '|')
            {
                trimmed = trimmed.Substring(1);
            }
            if (trimmed.Length > 0 && trimmed[trimmed.Length - 1] == '|')
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            }
            string[] parts = trimmed.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = parts[i].Trim();
            }
            return parts;
        }

        private static bool IsHorizontalRule(string line)
        {
            string t = line.Trim();
            if (t.Length < 3)
            {
                return false;
            }

            // Must be all the same char (-, *, _) optionally with spaces
            char ruleChar = '\0';
            int count = 0;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c == ' ')
                {
                    continue;
                }
                if (c == '-' || c == '*' || c == '_')
                {
                    if (ruleChar == '\0')
                    {
                        ruleChar = c;
                    }
                    else if (c != ruleChar)
                    {
                        return false;
                    }
                    count++;
                }
                else
                {
                    return false;
                }
            }

            return count >= 3;
        }

        private static bool IsOrderedListItem(string line, out int contentStart, out int indentSpaces)
        {
            contentStart = 0;
            indentSpaces = 0;
            int i = 0;
            while (i < line.Length && line[i] == ' ')
            {
                indentSpaces++;
                i++;
            }
            if (i >= line.Length || line[i] < '0' || line[i] > '9')
            {
                return false;
            }
            while (i < line.Length && line[i] >= '0' && line[i] <= '9')
            {
                i++;
            }
            if (i + 1 >= line.Length || line[i] != '.' || line[i + 1] != ' ')
            {
                return false;
            }
            contentStart = i + 2;
            return true;
        }

        private static bool StartsWith(string text, string prefix)
        {
            if (text.Length < prefix.Length)
            {
                return false;
            }
            for (int i = 0; i < prefix.Length; i++)
            {
                if (text[i] != prefix[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static string StripEmoji(string text)
        {
            if (text == null)
            {
                return text;
            }
            StringBuilder sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++; // skip both chars of the surrogate pair
                    continue;
                }
                sb.Append(text[i]);
            }
            return sb.ToString();
        }

        private void AppendCodeText(string text)
        {
            int start = _rtb.TextLength;
            _rtb.AppendText(text);
            _rtb.Select(start, text.Length);
            _rtb.SelectionFont = _codeFont;
            _rtb.SelectionColor = ColorContent;
            _rtb.SelectionBackColor = ColorCodeBackground;
            _rtb.Select(_rtb.TextLength, 0);
        }

        public void Clear()
        {
            _rtb.Clear();
            _inCodeBlock = false;
            _inReasoning = false;
            _lineBuffer = new StringBuilder();
            _skippingLangHint = false;
            _inTable = false;
            _tableRows.Clear();
        }

        private void AppendFormatted(string text, Font font, Color color)
        {
            int start = _rtb.TextLength;
            _rtb.AppendText(text);
            _rtb.Select(start, text.Length);
            _rtb.SelectionFont = font;
            _rtb.SelectionColor = color;
            _rtb.Select(_rtb.TextLength, 0);
        }
    }
}
