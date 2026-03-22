using System;

namespace Incantation.Chat
{
    public class ChatMessage
    {
        private string _role;
        private string _content;
        private DateTime _timestamp;
        private string _type;
        private string _detail;
        private bool _completed;

        public ChatMessage()
        {
            _role = "";
            _content = "";
            _timestamp = DateTime.Now;
            _type = "text";
            _detail = "";
            _completed = false;
        }

        public ChatMessage(string role, string content)
        {
            _role = role;
            _content = content;
            _timestamp = DateTime.Now;
            _type = "text";
            _detail = "";
            _completed = false;
        }

        public ChatMessage(string role, string content, DateTime timestamp)
        {
            _role = role;
            _content = content;
            _timestamp = timestamp;
            _type = "text";
            _detail = "";
            _completed = false;
        }

        public ChatMessage(string role, string content, string type)
        {
            _role = role;
            _content = content;
            _timestamp = DateTime.Now;
            _type = type;
            _detail = "";
            _completed = false;
        }

        public string Role
        {
            get { return _role; }
            set { _role = value; }
        }

        public string Content
        {
            get { return _content; }
            set { _content = value; }
        }

        public DateTime Timestamp
        {
            get { return _timestamp; }
            set { _timestamp = value; }
        }

        public string Type
        {
            get { return _type; }
            set { _type = value; }
        }

        public string Detail
        {
            get { return _detail; }
            set { _detail = value; }
        }

        public bool Completed
        {
            get { return _completed; }
            set { _completed = value; }
        }
    }
}
