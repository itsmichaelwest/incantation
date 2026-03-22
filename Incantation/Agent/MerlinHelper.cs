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
        private string _lastError;

        public bool IsAvailable
        {
            get { return _isAvailable; }
        }

        public bool IsVisible
        {
            get { return _isVisible; }
        }

        public string LastError
        {
            get { return _lastError; }
        }

        public bool Initialize()
        {
            _lastError = null;

            _agentType = Type.GetTypeFromProgID("Agent.Control.2");
            if (_agentType == null)
            {
                _lastError = "Agent.Control.2 COM class is not registered.";
                return false;
            }

            try
            {
                _agentControl = Activator.CreateInstance(_agentType);
            }
            catch (Exception ex)
            {
                _lastError = "Failed to create Agent control: " + ex.Message;
                return false;
            }

            // Required when created outside an ActiveX container
            try
            {
                _agentType.InvokeMember("Connected",
                    BindingFlags.SetProperty, null, _agentControl, new object[] { true });
            }
            catch (Exception ex)
            {
                _lastError = "Failed to connect Agent control: " + ex.Message;
                return false;
            }

            try
            {
                _characters = _agentType.InvokeMember("Characters",
                    BindingFlags.GetProperty, null, _agentControl, null);
                _charsType = _characters.GetType();
            }
            catch (Exception ex)
            {
                _lastError = "Failed to access Characters collection: " + ex.Message;
                return false;
            }

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
                    try
                    {
                        _charsType.InvokeMember("Load", BindingFlags.InvokeMethod,
                            null, _characters, new object[] { "Merlin", paths[i] });
                        loaded = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        _lastError = "Failed to load Merlin from " + paths[i] + ": " + ex.Message;
                    }
                }
            }

            if (!loaded)
            {
                if (_lastError == null)
                {
                    _lastError = "Merlin.acs not found in any expected location.";
                }
                return false;
            }

            try
            {
                _character = _charsType.InvokeMember("Character",
                    BindingFlags.InvokeMethod, null, _characters, new object[] { "Merlin" });
                _charType = _character.GetType();
            }
            catch (Exception ex)
            {
                _lastError = "Failed to get Merlin character: " + ex.Message;
                return false;
            }

            _isAvailable = true;
            return true;
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
                    null, _character, new object[] { false });
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
                    null, _character, new object[] { false });
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
            StopAll();
            Play("Greet");
            _lastAnimation = "Greet";
        }

        public void AnimateExplain()
        {
            if (_lastAnimation == "Explain") return;
            StopAll();
            Play("Explain");
            _lastAnimation = "Explain";
        }

        public void AnimateSuggest()
        {
            if (_lastAnimation == "Suggest") return;
            StopAll();
            Play("Suggest");
            _lastAnimation = "Suggest";
        }

        public void AnimateProcessing()
        {
            if (_lastAnimation == "Processing") return;
            StopAll();
            Play("Processing");
            _lastAnimation = "Processing";
        }

        public void AnimateCongratulate()
        {
            StopAll();
            Play("Congratulate");
            _lastAnimation = "Congratulate";
        }

        public void AnimateSurprised()
        {
            StopAll();
            Play("Surprised");
            _lastAnimation = "Surprised";
        }

        public void AnimateConfused()
        {
            if (_lastAnimation == "Confused") return;
            StopAll();
            Play("Confused");
            _lastAnimation = "Confused";
        }

        public void AnimateAcknowledge()
        {
            StopAll();
            Play("Acknowledge");
            _lastAnimation = "Acknowledge";
        }

        public void AnimateWave()
        {
            StopAll();
            Play("Wave");
            _lastAnimation = "Wave";
        }

        public void AnimateDoMagic()
        {
            StopAll();
            Play("DoMagic1");
            Play("DoMagic2");
            _lastAnimation = "DoMagic2";
        }

        public void AnimateAnnounce()
        {
            StopAll();
            Play("Announce");
            _lastAnimation = "Announce";
        }

        public void AnimateGetAttention()
        {
            StopAll();
            Play("GetAttention");
            _lastAnimation = "GetAttention";
        }

        public void AnimatePleased()
        {
            StopAll();
            Play("Pleased");
            _lastAnimation = "Pleased";
        }

        public void AnimateDecline()
        {
            StopAll();
            Play("Decline");
            _lastAnimation = "Decline";
        }

        public void AnimateLookUp()
        {
            StopAll();
            Play("LookUp");
            _lastAnimation = "LookUp";
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
                    null, _character, null);
                _lastAnimation = null;
            }
            catch
            {
                // Swallow
            }
        }

        public void MoveToControl(Control control)
        {
            if (!_isAvailable || _character == null || control == null || !control.IsHandleCreated)
            {
                return;
            }
            try
            {
                // Center horizontally in the control, anchor to the bottom
                Point screenPos = control.PointToScreen(Point.Empty);
                int charWidth = 128;
                int charHeight = 128;
                int x = screenPos.X + (control.Width - charWidth) / 2;
                int y = screenPos.Y + control.Height - charHeight;

                // Clamp to short range
                if (x > short.MaxValue) x = short.MaxValue;
                if (y > short.MaxValue) y = short.MaxValue;
                if (x < 0) x = 0;
                if (y < 0) y = 0;

                _charType.InvokeMember("MoveTo", BindingFlags.InvokeMethod,
                    null, _character, new object[] { (short)x, (short)y, 0 });
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
