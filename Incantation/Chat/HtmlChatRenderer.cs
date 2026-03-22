using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace Incantation.Chat
{
    public class HtmlChatRenderer : IChatRenderer
    {
        private WebBrowser _browser;
        private bool _documentReady;
        private int _msgIndex;
        private string _currentContentId;
        private StringBuilder _currentHtml;
        private StringBuilder _lineBuffer;
        private bool _inCodeBlock;
        private bool _inReasoning;
        private bool _skippingLangHint;
        private bool _inTable;
        private List<string> _tableRows;
        private List<string> _pendingOps;
        private Dictionary<string, string> _pendingUpdates;

        private static readonly string HTML_TEMPLATE =
            "<html><head><style>" +
            "body{margin:0;padding:8px;font-family:Tahoma,sans-serif;font-size:8.25pt;color:#000;background:#fff;}" +
            ".msg{margin:0 0 6px 0;padding:5px 8px;}" +
            ".msg-user{background:#eef3fb;border-left:3px solid #003399;}" +
            ".msg-assistant{background:#f7faf7;border-left:3px solid #006400;}" +
            ".msg-header{margin-bottom:3px;}" +
            ".name-user{font-weight:bold;color:#003399;}" +
            ".name-assistant{font-weight:bold;color:#006400;}" +
            ".timestamp{color:#808080;font-size:7.5pt;margin-left:6px;}" +
            ".msg-content{padding:2px 0;line-height:1.4;}" +
            ".reasoning{color:#8c8c8c;font-style:italic;font-size:8pt;padding:2px 0 2px 12px;" +
            "border-left:2px solid #d0d0d0;margin:2px 0;}" +
            ".tool-call{margin:2px 0;padding:4px 8px;background:#fdf8f0;border-left:3px solid #c86400;}" +
            ".tool-summary{font-weight:bold;font-style:italic;color:#c86400;}" +
            ".tool-detail{font-family:'Lucida Console',monospace;font-size:7pt;color:#8c8c8c;" +
            "padding:2px 0 2px 12px;white-space:pre;word-wrap:break-word;}" +
            ".system-msg{color:#808080;font-style:italic;text-align:center;padding:4px 0;}" +
            ".error-msg{color:#cc0000;padding:4px 8px;background:#fff0f0;border-left:3px solid #cc0000;}" +
            ".error-label{font-weight:bold;}" +
            "h1,h2,h3{color:#003399;margin:6px 0 3px 0;}" +
            "h1{font-size:12pt;}h2{font-size:10pt;}h3{font-size:8.25pt;}" +
            "blockquote{margin:2px 0 2px 8px;padding:2px 8px;border-left:3px solid #999;color:#646464;}" +
            "pre{font-family:'Lucida Console',monospace;font-size:9pt;background:#f5f5f5;" +
            "border:1px solid #e0e0e0;padding:6px 8px;margin:4px 0;white-space:pre;word-wrap:break-word;overflow:hidden;}" +
            "code{font-family:'Lucida Console',monospace;font-size:8.5pt;background:#f5f5f5;padding:1px 3px;}" +
            "a{color:#0000c8;}" +
            "ul,ol{margin:2px 0 2px 20px;padding:0;}li{margin:1px 0;}" +
            "table{border-collapse:collapse;margin:4px 0;font-family:'Lucida Console',monospace;font-size:8.5pt;}" +
            "th,td{border:1px solid #d0d0d0;padding:2px 6px;text-align:left;}" +
            "th{background:#f0f0f0;font-weight:bold;}" +
            "hr{border:none;border-top:1px solid #c0c0c0;margin:6px 0;}" +
            "#end{height:1px;}" +
            "</style></head><body><div id=\"chat\"></div><div id=\"end\"></div></body></html>";

        public HtmlChatRenderer(WebBrowser browser)
        {
            _browser = browser;
            _documentReady = false;
            _msgIndex = 0;
            _currentContentId = null;
            _currentHtml = new StringBuilder();
            _lineBuffer = new StringBuilder();
            _inCodeBlock = false;
            _inReasoning = false;
            _skippingLangHint = false;
            _inTable = false;
            _tableRows = new List<string>();
            _pendingOps = new List<string>();
            _pendingUpdates = new Dictionary<string, string>();

            _browser.DocumentCompleted += OnDocumentReady;
            _browser.DocumentText = HTML_TEMPLATE;
        }

        private void OnDocumentReady(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            _documentReady = true;
            // Flush any operations that were queued before the document was ready
            if (_pendingOps.Count > 0)
            {
                for (int i = 0; i < _pendingOps.Count; i++)
                {
                    AppendHtmlToChat(_pendingOps[i]);
                }
                _pendingOps.Clear();
            }
            // Apply any element updates that were deferred (e.g. streaming content during replay)
            if (_pendingUpdates.Count > 0)
            {
                foreach (KeyValuePair<string, string> kv in _pendingUpdates)
                {
                    HtmlElement el = _browser.Document.GetElementById(kv.Key);
                    if (el != null)
                    {
                        el.InnerHtml = kv.Value;
                    }
                }
                _pendingUpdates.Clear();
            }
            ScrollToEnd();
        }

        // ====================================================================
        // Public API — matches IChatRenderer
        // ====================================================================

        public void SuspendPainting()
        {
            // MSHTML batches repaints well — no-op for now
        }

        public void ResumePainting()
        {
            // MSHTML batches repaints well — no-op for now
        }

        public void ScrollToEnd()
        {
            if (!_documentReady || _browser.Document == null) return;
            HtmlElement end = _browser.Document.GetElementById("end");
            if (end != null)
            {
                end.ScrollIntoView(false);
            }
        }

        public void AppendUserMessage(string name, DateTime time, string content)
        {
            string timeStr = time.ToString("HH:mm");
            string html =
                "<div class=\"msg msg-user\">" +
                "<div class=\"msg-header\">" +
                "<span class=\"name-user\">" + HtmlEncode(name) + "</span>" +
                "<span class=\"timestamp\">[" + timeStr + "]</span>" +
                "</div>" +
                "<div class=\"msg-content\">" + HtmlEncode(content).Replace("\n", "<br>") + "</div>" +
                "</div>";
            AppendHtmlToChat(html);
        }

        public void AppendAssistantHeader(string name, DateTime time)
        {
            string timeStr = time.ToString("HH:mm");
            _msgIndex++;
            _currentContentId = "content-" + _msgIndex;
            _currentHtml = new StringBuilder();

            string html =
                "<div id=\"msg-" + _msgIndex + "\" class=\"msg msg-assistant\">" +
                "<div class=\"msg-header\">" +
                "<span class=\"name-assistant\">" + HtmlEncode(name) + "</span>" +
                "<span class=\"timestamp\">[" + timeStr + "]</span>" +
                "</div>" +
                "<div id=\"" + _currentContentId + "\" class=\"msg-content\"></div>" +
                "</div>";
            AppendHtmlToChat(html);
        }

        public void AppendReasoning(string text)
        {
            if (text == null) return;

            if (!_inReasoning)
            {
                _inReasoning = true;
                _msgIndex++;
                string reasoningId = "reasoning-" + _msgIndex;
                string html =
                    "<div id=\"" + reasoningId + "\" class=\"reasoning\">thinking: </div>";
                AppendHtmlToChat(html);
                _currentContentId = reasoningId;
                _currentHtml = new StringBuilder("thinking: ");
            }

            _currentHtml.Append(HtmlEncode(text).Replace("\n", "<br>"));
            UpdateElement(_currentContentId, _currentHtml.ToString());
        }

        public void EndReasoning()
        {
            if (_inReasoning)
            {
                _inReasoning = false;
                _currentContentId = null;
                _currentHtml = new StringBuilder();
            }
        }

        public void AppendDelta(string text)
        {
            if (text == null) return;

            text = StripEmoji(text);

            if (_inReasoning)
            {
                EndReasoning();
            }

            _lineBuffer.Append(text);
            ProcessLineBuffer();
        }

        public void AppendToolCall(string summary, string detail)
        {
            StringBuilder html = new StringBuilder();
            html.Append("<div class=\"tool-call\">");
            html.Append("<div class=\"tool-summary\">&gt; ");
            html.Append(HtmlEncode(summary));
            html.Append("</div>");

            if (detail != null && detail.Length > 0 && detail.Length < 500)
            {
                string detailText = detail;
                try
                {
                    Newtonsoft.Json.Linq.JObject parsed = Newtonsoft.Json.Linq.JObject.Parse(detail);
                    detailText = parsed.ToString(Newtonsoft.Json.Formatting.Indented);
                }
                catch { }

                html.Append("<div class=\"tool-detail\">");
                html.Append(HtmlEncode(detailText));
                html.Append("</div>");
            }

            html.Append("</div>");
            AppendHtmlToChat(html.ToString());
        }

        public void AppendToolCall(string summary)
        {
            AppendToolCall(summary, null);
        }

        public void AppendError(string message)
        {
            string html =
                "<div class=\"error-msg\">" +
                "<span class=\"error-label\">Error: </span>" +
                HtmlEncode(message) +
                "</div>";
            AppendHtmlToChat(html);
        }

        public void AppendSystemMessage(string text)
        {
            string html = "<div class=\"system-msg\">" + HtmlEncode(text) + "</div>";
            AppendHtmlToChat(html);
        }

        public void AppendNewline()
        {
            // Small spacer between message groups
        }

        public void FinalizeMessage()
        {
            FlushLineBuffer();

            if (_inTable)
            {
                FlushTable();
                _inTable = false;
            }

            // Commit any remaining accumulated HTML to the content div
            if (_currentContentId != null && _currentHtml.Length > 0)
            {
                UpdateElement(_currentContentId, _currentHtml.ToString());
            }

            _inCodeBlock = false;
            _inReasoning = false;
            _skippingLangHint = false;
            _currentContentId = null;
            _currentHtml = new StringBuilder();
        }

        public void Clear()
        {
            _msgIndex = 0;
            _inCodeBlock = false;
            _inReasoning = false;
            _lineBuffer = new StringBuilder();
            _currentHtml = new StringBuilder();
            _currentContentId = null;
            _skippingLangHint = false;
            _inTable = false;
            _tableRows.Clear();
            _pendingOps.Clear();
            _pendingUpdates.Clear();
            _documentReady = false;
            _browser.DocumentText = HTML_TEMPLATE;
        }

        // ====================================================================
        // Line buffering (same logic as ChatRenderer)
        // ====================================================================

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

            // Push accumulated HTML to the DOM
            if (_currentContentId != null && _currentHtml.Length > 0)
            {
                UpdateElement(_currentContentId, _currentHtml.ToString());
            }
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
        }

        // ====================================================================
        // Markdown-to-HTML rendering
        // ====================================================================

        private void RenderMarkdownLine(string line)
        {
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
                if (_inCodeBlock)
                {
                    _currentHtml.Append("<pre>");
                }
                else
                {
                    _currentHtml.Append("</pre>");
                }
                return;
            }

            // Inside code block — render raw
            if (_inCodeBlock)
            {
                _currentHtml.Append(HtmlEncode(line));
                _currentHtml.Append("\n");
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
                }
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
                _currentHtml.Append("<br>");
                return;
            }

            // Horizontal rule
            if (IsHorizontalRule(line))
            {
                _currentHtml.Append("<hr>");
                return;
            }

            // Headers
            if (StartsWith(line, "### "))
            {
                _currentHtml.Append("<h3>");
                _currentHtml.Append(RenderInlineMarkdown(line.Substring(4)));
                _currentHtml.Append("</h3>");
                return;
            }
            if (StartsWith(line, "## "))
            {
                _currentHtml.Append("<h2>");
                _currentHtml.Append(RenderInlineMarkdown(line.Substring(3)));
                _currentHtml.Append("</h2>");
                return;
            }
            if (StartsWith(line, "# "))
            {
                _currentHtml.Append("<h1>");
                _currentHtml.Append(RenderInlineMarkdown(line.Substring(2)));
                _currentHtml.Append("</h1>");
                return;
            }

            // Blockquotes
            if (StartsWith(trimmed, "> "))
            {
                string quoteText = trimmed.Substring(2);
                while (StartsWith(quoteText, "> "))
                {
                    quoteText = quoteText.Substring(2);
                }
                if (StartsWith(quoteText, ">"))
                {
                    quoteText = quoteText.Substring(1);
                }
                _currentHtml.Append("<blockquote>");
                _currentHtml.Append(RenderInlineMarkdown(quoteText));
                _currentHtml.Append("</blockquote>");
                return;
            }
            if (trimmed == ">")
            {
                _currentHtml.Append("<br>");
                return;
            }

            // Bullet list items
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
                    string marginLeft = (nestLevel * 20) + "px";
                    _currentHtml.Append("<div style=\"margin-left:");
                    _currentHtml.Append(marginLeft);
                    _currentHtml.Append(";padding:1px 0;\">");
                    _currentHtml.Append("\u2022 ");
                    _currentHtml.Append(RenderInlineMarkdown(itemText));
                    _currentHtml.Append("</div>");
                    return;
                }
            }

            // Ordered list items
            {
                int olContentStart;
                int olIndent;
                if (IsOrderedListItem(line, out olContentStart, out olIndent))
                {
                    int nestLevel = olIndent / 2;
                    string prefix = line.Substring(olIndent, olContentStart - olIndent);
                    string itemText = line.Substring(olContentStart);
                    string marginLeft = (nestLevel * 20) + "px";
                    _currentHtml.Append("<div style=\"margin-left:");
                    _currentHtml.Append(marginLeft);
                    _currentHtml.Append(";padding:1px 0;\">");
                    _currentHtml.Append(HtmlEncode(prefix));
                    _currentHtml.Append(RenderInlineMarkdown(itemText));
                    _currentHtml.Append("</div>");
                    return;
                }
            }

            // Regular line
            _currentHtml.Append(RenderInlineMarkdown(line));
            _currentHtml.Append("<br>");
        }

        private string RenderInlineMarkdown(string text)
        {
            const int NORMAL = 0;
            const int BOLD = 1;
            const int ITALIC = 2;
            const int CODE = 3;
            const int STRIKE = 4;

            int state = NORMAL;
            StringBuilder buf = new StringBuilder();
            StringBuilder result = new StringBuilder();
            int len = text.Length;

            for (int i = 0; i < len; i++)
            {
                char c = text[i];

                if (c == '`')
                {
                    if (state == CODE)
                    {
                        result.Append("<code>");
                        result.Append(HtmlEncode(buf.ToString()));
                        result.Append("</code>");
                        buf = new StringBuilder();
                        state = NORMAL;
                    }
                    else
                    {
                        FlushInline(result, buf, state);
                        buf = new StringBuilder();
                        state = CODE;
                    }
                    continue;
                }

                if (state == CODE)
                {
                    buf.Append(c);
                    continue;
                }

                // Strikethrough: ~~
                if (c == '~' && i + 1 < len && text[i + 1] == '~')
                {
                    FlushInline(result, buf, state);
                    buf = new StringBuilder();
                    state = (state == STRIKE) ? NORMAL : STRIKE;
                    i++;
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
                            FlushInline(result, buf, state);
                            buf = new StringBuilder();

                            string linkText = text.Substring(i + 1, closeBracket - i - 1);
                            string linkUrl = text.Substring(closeBracket + 2, closeParen - closeBracket - 2);

                            result.Append("<a href=\"");
                            result.Append(HtmlEncode(linkUrl));
                            result.Append("\">");
                            result.Append(HtmlEncode(linkText));
                            result.Append("</a>");

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
                        FlushInline(result, buf, state);
                        buf = new StringBuilder();
                        state = (state == BOLD) ? NORMAL : BOLD;
                        i++;
                        continue;
                    }
                    else
                    {
                        FlushInline(result, buf, state);
                        buf = new StringBuilder();
                        state = (state == ITALIC) ? NORMAL : ITALIC;
                        continue;
                    }
                }

                buf.Append(c);
            }

            if (buf.Length > 0)
            {
                FlushInline(result, buf, state);
            }

            return result.ToString();
        }

        private void FlushInline(StringBuilder result, StringBuilder buf, int state)
        {
            if (buf.Length == 0) return;
            string text = HtmlEncode(buf.ToString());

            switch (state)
            {
                case 1: // bold
                    result.Append("<strong>");
                    result.Append(text);
                    result.Append("</strong>");
                    break;
                case 2: // italic
                    result.Append("<em>");
                    result.Append(text);
                    result.Append("</em>");
                    break;
                case 3: // code
                    result.Append("<code>");
                    result.Append(text);
                    result.Append("</code>");
                    break;
                case 4: // strikethrough
                    result.Append("<span style=\"text-decoration:line-through;\">");
                    result.Append(text);
                    result.Append("</span>");
                    break;
                default:
                    result.Append(text);
                    break;
            }
        }

        // ====================================================================
        // Table rendering
        // ====================================================================

        private void FlushTable()
        {
            if (_tableRows.Count == 0) return;

            List<string[]> parsed = new List<string[]>();
            for (int r = 0; r < _tableRows.Count; r++)
            {
                string row = _tableRows[r].Trim();
                if (IsTableSeparator(row)) continue;
                parsed.Add(ParseTableRow(row));
            }

            if (parsed.Count == 0) return;

            _currentHtml.Append("<table>");

            for (int r = 0; r < parsed.Count; r++)
            {
                _currentHtml.Append("<tr>");
                string tag = (r == 0) ? "th" : "td";
                for (int c = 0; c < parsed[r].Length; c++)
                {
                    _currentHtml.Append("<");
                    _currentHtml.Append(tag);
                    _currentHtml.Append(">");
                    _currentHtml.Append(RenderInlineMarkdown(parsed[r][c]));
                    _currentHtml.Append("</");
                    _currentHtml.Append(tag);
                    _currentHtml.Append(">");
                }
                _currentHtml.Append("</tr>");
            }

            _currentHtml.Append("</table>");
            _tableRows.Clear();
        }

        // ====================================================================
        // DOM manipulation helpers
        // ====================================================================

        private void AppendHtmlToChat(string html)
        {
            if (!_documentReady || _browser.Document == null)
            {
                _pendingOps.Add(html);
                return;
            }

            HtmlElement chat = _browser.Document.GetElementById("chat");
            if (chat == null) return;

            HtmlElement div = _browser.Document.CreateElement("div");
            div.InnerHtml = html;

            // Append all children of the wrapper div (to avoid an extra nesting layer)
            // Unfortunately HtmlElement doesn't expose a clean way to do this,
            // so we set the outer div's innerHTML and let MSHTML parse it
            // We actually want to append the html directly to chat's innerHTML
            // but that replaces content. Instead, use insertAdjacentHTML via InnerHtml trick.

            // Simplest reliable approach: set chat.InnerHtml += html
            // This re-parses the whole chat which is slow for large conversations.
            // Better: create a container div and append it as a child.
            chat.AppendChild(div);
        }

        private void UpdateElement(string id, string html)
        {
            if (!_documentReady || _browser.Document == null)
            {
                _pendingUpdates[id] = html;
                return;
            }

            HtmlElement el = _browser.Document.GetElementById(id);
            if (el != null)
            {
                el.InnerHtml = html;
            }
        }

        // ====================================================================
        // Utility methods (ported from ChatRenderer)
        // ====================================================================

        private static string HtmlEncode(string text)
        {
            if (text == null) return "";
            StringBuilder sb = new StringBuilder(text.Length + 16);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
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
            while (i < line.Length && line[i] == ' ')
            {
                indentSpaces++;
                i++;
            }
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
            if (trimmed.Length > 0 && trimmed[0] == '|')
                trimmed = trimmed.Substring(1);
            if (trimmed.Length > 0 && trimmed[trimmed.Length - 1] == '|')
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            string[] parts = trimmed.Split('|');
            for (int i = 0; i < parts.Length; i++)
                parts[i] = parts[i].Trim();
            return parts;
        }
    }
}
