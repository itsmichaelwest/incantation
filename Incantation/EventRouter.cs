using System;
using Newtonsoft.Json.Linq;

namespace Incantation
{
    // ================================================================
    // Event argument classes
    // ================================================================

    public class IntentEventArgs : EventArgs
    {
        private string _intent;

        public IntentEventArgs(string intent)
        {
            _intent = intent;
        }

        public string Intent
        {
            get { return _intent; }
        }
    }

    public class ContentEventArgs : EventArgs
    {
        private string _content;

        public ContentEventArgs(string content)
        {
            _content = content;
        }

        public string Content
        {
            get { return _content; }
        }
    }

    public class ToolStartEventArgs : EventArgs
    {
        private string _toolName;
        private string _toolId;
        private string _input;

        public ToolStartEventArgs(string toolName, string toolId, string input)
        {
            _toolName = toolName;
            _toolId = toolId;
            _input = input;
        }

        public string ToolName
        {
            get { return _toolName; }
        }

        public string ToolId
        {
            get { return _toolId; }
        }

        public string Input
        {
            get { return _input; }
        }
    }

    public class ToolEndEventArgs : EventArgs
    {
        private string _toolId;
        private bool _success;
        private string _output;

        public ToolEndEventArgs(string toolId, bool success, string output)
        {
            _toolId = toolId;
            _success = success;
            _output = output;
        }

        public string ToolId
        {
            get { return _toolId; }
        }

        public bool Success
        {
            get { return _success; }
        }

        public string Output
        {
            get { return _output; }
        }
    }

    public class TitleChangedEventArgs : EventArgs
    {
        private string _title;

        public TitleChangedEventArgs(string title)
        {
            _title = title;
        }

        public string Title
        {
            get { return _title; }
        }
    }

    public class ErrorEventArgs : EventArgs
    {
        private string _message;

        public ErrorEventArgs(string message)
        {
            _message = message;
        }

        public string Message
        {
            get { return _message; }
        }
    }

    // ================================================================
    // EventRouter — parses SSE JSON and raises typed events
    // ================================================================

    public class EventRouter
    {
        public event EventHandler<IntentEventArgs> IntentReceived;
        public event EventHandler<ContentEventArgs> ReasoningReceived;
        public event EventHandler<ContentEventArgs> DeltaReceived;
        public event EventHandler<ContentEventArgs> MessageReceived;
        public event EventHandler<ToolStartEventArgs> ToolStartReceived;
        public event EventHandler<ToolEndEventArgs> ToolEndReceived;
        public event EventHandler<TitleChangedEventArgs> TitleChanged;
        public event EventHandler IdleReceived;
        public event EventHandler<ErrorEventArgs> ErrorReceived;

        public void HandleEvent(string json)
        {
            if (json == null) return;

            JObject obj = JObject.Parse(json);
            JToken typeToken = obj.SelectToken("type");
            if (typeToken == null) return;
            string eventType = (string)typeToken;

            if (eventType == "intent")
            {
                JToken intentToken = obj.SelectToken("intent");
                if (intentToken != null && IntentReceived != null)
                {
                    IntentReceived(this, new IntentEventArgs((string)intentToken));
                }
            }
            else if (eventType == "reasoning")
            {
                JToken contentToken = obj.SelectToken("content");
                if (contentToken != null && ReasoningReceived != null)
                {
                    ReasoningReceived(this, new ContentEventArgs((string)contentToken));
                }
            }
            else if (eventType == "delta")
            {
                JToken contentToken = obj.SelectToken("content");
                if (contentToken != null && DeltaReceived != null)
                {
                    DeltaReceived(this, new ContentEventArgs((string)contentToken));
                }
            }
            else if (eventType == "message")
            {
                JToken contentToken = obj.SelectToken("content");
                string content = contentToken != null ? (string)contentToken : "";
                if (MessageReceived != null)
                {
                    MessageReceived(this, new ContentEventArgs(content));
                }
            }
            else if (eventType == "tool_start")
            {
                JToken toolToken = obj.SelectToken("tool");
                string toolName = toolToken != null ? (string)toolToken : "unknown";
                JToken idToken = obj.SelectToken("id");
                string toolId = idToken != null ? (string)idToken : null;
                JToken inputToken = obj.SelectToken("input");
                string inputStr = inputToken != null ? (string)inputToken : null;

                if (ToolStartReceived != null)
                {
                    ToolStartReceived(this, new ToolStartEventArgs(toolName, toolId, inputStr));
                }
            }
            else if (eventType == "tool_end")
            {
                JToken idToken = obj.SelectToken("id");
                string toolId = idToken != null ? (string)idToken : null;
                JToken successToken = obj.SelectToken("success");
                bool success = successToken != null && (bool)successToken;
                JToken outputToken = obj.SelectToken("output");
                string output = outputToken != null ? (string)outputToken : null;

                if (ToolEndReceived != null)
                {
                    ToolEndReceived(this, new ToolEndEventArgs(toolId, success, output));
                }
            }
            else if (eventType == "title_changed")
            {
                JToken titleToken = obj.SelectToken("title");
                if (titleToken != null && TitleChanged != null)
                {
                    TitleChanged(this, new TitleChangedEventArgs((string)titleToken));
                }
            }
            else if (eventType == "idle")
            {
                if (IdleReceived != null)
                {
                    IdleReceived(this, EventArgs.Empty);
                }
            }
            else if (eventType == "error")
            {
                JToken msgToken = obj.SelectToken("message");
                if (msgToken != null && ErrorReceived != null)
                {
                    ErrorReceived(this, new ErrorEventArgs((string)msgToken));
                }
            }
        }
    }
}
