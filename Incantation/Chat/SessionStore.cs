using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Incantation.Chat
{
    public class SessionData
    {
        private string _sessionId;
        private string _title;
        private DateTime _created;
        private DateTime _modified;
        private List<ChatMessage> _messages;
        private List<string> _contextFiles;
        private List<string> _outputFiles;
        private string _workingDirectory;

        public SessionData()
        {
            _sessionId = "";
            _title = "";
            _created = DateTime.Now;
            _modified = DateTime.Now;
            _messages = new List<ChatMessage>();
            _contextFiles = new List<string>();
            _outputFiles = new List<string>();
            _workingDirectory = "";
        }

        public SessionData(string sessionId)
        {
            _sessionId = sessionId;
            _title = "";
            _created = DateTime.Now;
            _modified = DateTime.Now;
            _messages = new List<ChatMessage>();
            _contextFiles = new List<string>();
            _outputFiles = new List<string>();
            _workingDirectory = "";
        }

        public string SessionId
        {
            get { return _sessionId; }
            set { _sessionId = value; }
        }

        public string Title
        {
            get { return _title; }
            set { _title = value; }
        }

        public DateTime Created
        {
            get { return _created; }
            set { _created = value; }
        }

        public DateTime Modified
        {
            get { return _modified; }
            set { _modified = value; }
        }

        public List<ChatMessage> Messages
        {
            get { return _messages; }
            set { _messages = value; }
        }

        public List<string> ContextFiles
        {
            get { return _contextFiles; }
            set { _contextFiles = value; }
        }

        public List<string> OutputFiles
        {
            get { return _outputFiles; }
            set { _outputFiles = value; }
        }

        public string WorkingDirectory
        {
            get { return _workingDirectory; }
            set { _workingDirectory = value; }
        }

        public void AddMessage(ChatMessage msg)
        {
            _messages.Add(msg);
            _modified = DateTime.Now;
        }

        public string DisplayTitle
        {
            get
            {
                if (_title != null && _title.Length > 0)
                {
                    return _title;
                }
                if (_sessionId != null && _sessionId.Length > 12)
                {
                    return _sessionId.Substring(0, 12) + "...";
                }
                return _sessionId != null ? _sessionId : "Untitled";
            }
        }

        public string DisplayTime
        {
            get
            {
                // Use last message timestamp, fall back to modified
                DateTime ts = _modified;
                if (_messages != null && _messages.Count > 0)
                {
                    ts = _messages[_messages.Count - 1].Timestamp;
                }
                TimeSpan age = DateTime.Now - ts;
                if (age.TotalMinutes < 1) return "just now";
                if (age.TotalMinutes < 60) return string.Format("{0}m ago", (int)age.TotalMinutes);
                if (age.TotalHours < 24) return string.Format("{0}h ago", (int)age.TotalHours);
                return ts.ToString("MM/dd");
            }
        }
    }

    public class SessionStore
    {
        private string _sessionsDir;

        public SessionStore(string baseDir)
        {
            _sessionsDir = Path.Combine(baseDir, "sessions");
            if (!Directory.Exists(_sessionsDir))
            {
                Directory.CreateDirectory(_sessionsDir);
            }
        }

        public void Save(SessionData data)
        {
            if (data == null || data.SessionId == null || data.SessionId.Length == 0)
            {
                return;
            }
            string path = GetFilePath(data.SessionId);
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        public SessionData Load(string sessionId)
        {
            string path = GetFilePath(sessionId);
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                string json = File.ReadAllText(path);
                SessionData data = JsonConvert.DeserializeObject<SessionData>(json);
                return data;
            }
            catch
            {
                return null;
            }
        }

        public List<SessionData> ListAll()
        {
            List<SessionData> result = new List<SessionData>();
            if (!Directory.Exists(_sessionsDir))
            {
                return result;
            }

            string[] files = Directory.GetFiles(_sessionsDir, "*.json");
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    string json = File.ReadAllText(files[i]);
                    SessionData data = JsonConvert.DeserializeObject<SessionData>(json);
                    if (data != null && data.SessionId != null)
                    {
                        result.Add(data);
                    }
                }
                catch
                {
                    // Skip corrupt files
                }
            }

            // Sort by modified descending — most recently used session first
            result.Sort(new SessionCreatedComparer());
            return result;
        }

        public void Delete(string sessionId)
        {
            string path = GetFilePath(sessionId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private string GetFilePath(string sessionId)
        {
            // Sanitize session ID for filename
            string safe = sessionId;
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
            {
                safe = safe.Replace(invalid[i], '_');
            }
            return Path.Combine(_sessionsDir, safe + ".json");
        }
    }

    internal class SessionCreatedComparer : IComparer<SessionData>
    {
        public int Compare(SessionData a, SessionData b)
        {
            // Descending — most recently used session first
            return b.Modified.CompareTo(a.Modified);
        }
    }
}
