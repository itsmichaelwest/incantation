using System;
using System.Collections.Generic;

namespace Incantation.Chat
{
    public class ChatHistory
    {
        private List<ChatMessage> _messages;

        public ChatHistory()
        {
            _messages = new List<ChatMessage>();
        }

        public void Add(ChatMessage msg)
        {
            _messages.Add(msg);
        }

        public void Clear()
        {
            _messages.Clear();
        }

        public List<ChatMessage> Messages
        {
            get { return _messages; }
        }
    }
}
