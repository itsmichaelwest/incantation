using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Incantation.Agent
{
    public class MerlinHelper : IDisposable
    {
        private bool _isAvailable;
        private bool _isVisible;
        private bool _disposed;
        private object _agentControl;
        private object _characters;
        private object _character;
        private Type _agentType;
        private Type _charsType;
        private Type _charType;
        private string _lastAnimation;

        public bool IsAvailable
        {
            get { return _isAvailable; }
        }

        public bool IsVisible
        {
            get { return _isVisible; }
        }

        public bool Initialize()
        {
            try
            {
                _agentType = Type.GetTypeFromProgID("Agent.Control.2");
                if (_agentType == null)
                {
                    return false;
                }

                _agentControl = Activator.CreateInstance(_agentType);

                // Get Characters collection
                _characters = _agentType.InvokeMember("Characters",
                    BindingFlags.GetProperty, null, _agentControl, null);
                _charsType = _characters.GetType();

                // Try loading Merlin from known paths
                string[] paths = new string[] {
                    @"C:\Windows\MSAgent\chars\Merlin.acs",
                    @"C:\Windows\srchasst\chars\Merlin.acs",
                    @"C:\WINDOWS\MSAgent\chars\Merlin.acs",
                    @"C:\WINDOWS\srchasst\chars\Merlin.acs"
                };

                bool loaded = false;
                for (int i = 0; i < paths.Length; i++)
                {
                    if (System.IO.File.Exists(paths[i]))
                    {
                        _charsType.InvokeMember("Load", BindingFlags.InvokeMethod,
                            null, _characters, new object[] { "Merlin", paths[i] });
                        loaded = true;
                        break;
                    }
                }

                if (!loaded)
                {
                    return false;
                }

                _character = _charsType.InvokeMember("Character",
                    BindingFlags.InvokeMethod, null, _characters, new object[] { "Merlin" });
                _charType = _character.GetType();

                _isAvailable = true;
                return true;
            }
            catch
            {
                _isAvailable = false;
                return false;
            }
        }

        public void Show()
        {
            if (!_isAvailable || _character == null)
            {
                return;
            }
            try
            {
                _charType.InvokeMember("Show", BindingFlags.InvokeMethod,
                    null, _character, new object[] { (short)0 });
                _isVisible = true;
            }
            catch
            {
                // Swallow - agent may not be fully ready
            }
        }

        public void Hide()
        {
            if (!_isAvailable || _character == null)
            {
                return;
            }
            try
            {
                _charType.InvokeMember("Hide", BindingFlags.InvokeMethod,
                    null, _character, new object[] { (short)0 });
                _isVisible = false;
            }
            catch
            {
                // Swallow
            }
        }

        public void AnimateThinking()
        {
            if (_lastAnimation == "Thinking")
            {
                return;
            }
            StopAll();
            Play("Thinking");
            _lastAnimation = "Thinking";
        }

        public void AnimateReading()
        {
            if (_lastAnimation == "Reading")
            {
                return;
            }
            StopAll();
            Play("Reading");
            _lastAnimation = "Reading";
        }

        public void AnimateWriting()
        {
            if (_lastAnimation == "Writing")
            {
                return;
            }
            StopAll();
            Play("Writing");
            _lastAnimation = "Writing";
        }

        public void AnimateSearching()
        {
            if (_lastAnimation == "Searching")
            {
                return;
            }
            StopAll();
            Play("Searching");
            _lastAnimation = "Searching";
        }

        public void AnimateIdle()
        {
            if (_lastAnimation == "RestPose")
            {
                return;
            }
            StopAll();
            Play("RestPose");
            _lastAnimation = "RestPose";
        }

        public void AnimateSad()
        {
            if (_lastAnimation == "Sad")
            {
                return;
            }
            StopAll();
            Play("Sad");
            _lastAnimation = "Sad";
        }

        public void AnimateGreet()
        {
            Play("Greet");
            _lastAnimation = "Greet";
        }

        public void Speak(string text)
        {
            if (!_isAvailable || _character == null)
            {
                return;
            }
            try
            {
                _charType.InvokeMember("Speak", BindingFlags.InvokeMethod,
                    null, _character, new object[] { text, null });
            }
            catch
            {
                // Swallow
            }
        }

        public void Think(string text)
        {
            if (!_isAvailable || _character == null)
            {
                return;
            }
            try
            {
                _charType.InvokeMember("Think", BindingFlags.InvokeMethod,
                    null, _character, new object[] { text });
            }
            catch
            {
                // Swallow
            }
        }

        public void StopAll()
        {
            if (!_isAvailable || _character == null)
            {
                return;
            }
            try
            {
                _charType.InvokeMember("StopAll", BindingFlags.InvokeMethod,
                    null, _character, new object[] { null });
                _lastAnimation = null;
            }
            catch
            {
                // Swallow
            }
        }

        public void MoveNearForm(Form form)
        {
            if (!_isAvailable || _character == null || form == null)
            {
                return;
            }
            try
            {
                // Position to the right of the form, vertically centered
                Point screenPos = form.PointToScreen(new Point(form.ClientSize.Width, 0));
                int x = screenPos.X + 20;
                int y = screenPos.Y + (form.ClientSize.Height / 4);

                // Clamp to short range
                if (x > short.MaxValue) x = short.MaxValue;
                if (y > short.MaxValue) y = short.MaxValue;
                if (x < 0) x = 0;
                if (y < 0) y = 0;

                _charType.InvokeMember("MoveTo", BindingFlags.InvokeMethod,
                    null, _character, new object[] { (short)x, (short)y, 1000 });
            }
            catch
            {
                // Swallow - position is best effort
            }
        }

        private void Play(string animation)
        {
            if (!_isAvailable || _character == null)
            {
                return;
            }
            try
            {
                _charType.InvokeMember("Play", BindingFlags.InvokeMethod,
                    null, _character, new object[] { animation });
            }
            catch
            {
                // Animation may not exist, swallow
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            try
            {
                if (_isVisible && _character != null)
                {
                    Hide();
                }
            }
            catch
            {
                // Swallow
            }

            // Unload character
            try
            {
                if (_characters != null && _charsType != null)
                {
                    _charsType.InvokeMember("Unload", BindingFlags.InvokeMethod,
                        null, _characters, new object[] { "Merlin" });
                }
            }
            catch
            {
                // Swallow
            }

            // Release COM objects
            if (_character != null)
            {
                try { Marshal.FinalReleaseComObject(_character); }
                catch { }
                _character = null;
            }

            if (_characters != null)
            {
                try { Marshal.FinalReleaseComObject(_characters); }
                catch { }
                _characters = null;
            }

            if (_agentControl != null)
            {
                try { Marshal.FinalReleaseComObject(_agentControl); }
                catch { }
                _agentControl = null;
            }

            _isAvailable = false;
            _isVisible = false;
        }
    }
}
