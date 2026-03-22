using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Incantation.Config
{
    public class AppSettings
    {
        private string _proxyAddress;
        private bool _merlinEnabled;
        private int _windowX;
        private int _windowY;
        private int _windowWidth;
        private int _windowHeight;
        private int _outerSplitDistance;
        private int _innerSplitDistance;
        private int _chatSplitDistance;
        private List<string> _workDirHistory;

        public AppSettings()
        {
            _proxyAddress = "http://192.168.50.1:5000";
            _merlinEnabled = true;
            _windowX = -1;
            _windowY = -1;
            _windowWidth = 1024;
            _windowHeight = 768;
            _outerSplitDistance = 120;
            _innerSplitDistance = -1;
            _chatSplitDistance = -1;
            _workDirHistory = new List<string>();
        }

        public string ProxyAddress
        {
            get { return _proxyAddress; }
            set { _proxyAddress = value; }
        }

        public bool MerlinEnabled
        {
            get { return _merlinEnabled; }
            set { _merlinEnabled = value; }
        }

        public int WindowX
        {
            get { return _windowX; }
            set { _windowX = value; }
        }

        public int WindowY
        {
            get { return _windowY; }
            set { _windowY = value; }
        }

        public int WindowWidth
        {
            get { return _windowWidth; }
            set { _windowWidth = value; }
        }

        public int WindowHeight
        {
            get { return _windowHeight; }
            set { _windowHeight = value; }
        }

        public int OuterSplitDistance
        {
            get { return _outerSplitDistance; }
            set { _outerSplitDistance = value; }
        }

        public int InnerSplitDistance
        {
            get { return _innerSplitDistance; }
            set { _innerSplitDistance = value; }
        }

        public int ChatSplitDistance
        {
            get { return _chatSplitDistance; }
            set { _chatSplitDistance = value; }
        }

        public List<string> WorkDirHistory
        {
            get { return _workDirHistory; }
            set { _workDirHistory = value; }
        }

        public static AppSettings Load(string path)
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }
            try
            {
                string json = File.ReadAllText(path);
                AppSettings settings = JsonConvert.DeserializeObject<AppSettings>(json);
                if (settings == null)
                {
                    return new AppSettings();
                }
                if (settings._workDirHistory == null)
                {
                    settings._workDirHistory = new List<string>();
                }
                return settings;
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save(string path)
        {
            try
            {
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch
            {
                // Best effort save
            }
        }
    }
}
