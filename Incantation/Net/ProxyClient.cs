using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Incantation.Net
{
    public class ProxyClient
    {
        private string _baseUrl;

        public ProxyClient(string baseUrl)
        {
            _baseUrl = baseUrl;
        }

        public string BaseUrl
        {
            get { return _baseUrl; }
            set { _baseUrl = value; }
        }

        public bool CheckHealth()
        {
            try
            {
                string url = _baseUrl + "/health";
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 3000;
                req.ReadWriteTimeout = 3000;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    return resp.StatusCode == HttpStatusCode.OK;
                }
            }
            catch
            {
                return false;
            }
        }

        public string[] ListModels()
        {
            try
            {
                string url = _baseUrl + "/models";
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 10000;
                req.ReadWriteTimeout = 10000;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    JObject obj = JObject.Parse(body);
                    JToken modelsToken = obj.SelectToken("models");
                    if (modelsToken != null && modelsToken.Type == JTokenType.Array)
                    {
                        JArray arr = (JArray)modelsToken;
                        List<string> result = new List<string>();
                        for (int i = 0; i < arr.Count; i++)
                        {
                            JToken idToken = arr[i].SelectToken("id");
                            if (idToken != null)
                            {
                                result.Add((string)idToken);
                            }
                        }
                        return result.ToArray();
                    }
                    return new string[0];
                }
            }
            catch
            {
                return new string[0];
            }
        }

        public string CreateSession(string workingDirectory)
        {
            return CreateSession(workingDirectory, null);
        }

        public string CreateSession(string workingDirectory, string conversationHistory)
        {
            string url = _baseUrl + "/session";
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Timeout = 30000;
            req.ReadWriteTimeout = 30000;

            SessionPayload payload = new SessionPayload(workingDirectory);
            payload.history = conversationHistory;
            string bodyJson = JsonConvert.SerializeObject(payload);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(bodyJson);
            req.ContentLength = bodyBytes.Length;

            using (Stream reqStream = req.GetRequestStream())
            {
                reqStream.Write(bodyBytes, 0, bodyBytes.Length);
            }

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string body = reader.ReadToEnd();
                JObject obj = JObject.Parse(body);
                JToken token = obj.SelectToken("sessionId");
                if (token != null)
                {
                    return (string)token;
                }
                return null;
            }
        }

        public void SendMessage(string sessionId, string prompt, BackgroundWorker worker)
        {
            string url = _baseUrl + "/session/" + sessionId + "/message";
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.ReadWriteTimeout = 300000;
            req.Timeout = 300000;

            string bodyJson = JsonConvert.SerializeObject(new PromptPayload(prompt));
            byte[] bodyBytes = Encoding.UTF8.GetBytes(bodyJson);
            req.ContentLength = bodyBytes.Length;

            using (Stream reqStream = req.GetRequestStream())
            {
                reqStream.Write(bodyBytes, 0, bodyBytes.Length);
            }

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (worker.CancellationPending)
                    {
                        break;
                    }
                    if (line.StartsWith("data: "))
                    {
                        string json = line.Substring(6);
                        if (json != "[DONE]")
                        {
                            worker.ReportProgress(0, json);
                        }
                    }
                }
            }
        }

        public string[] ListSessions()
        {
            try
            {
                string url = _baseUrl + "/sessions";
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 5000;
                req.ReadWriteTimeout = 5000;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    JObject obj = JObject.Parse(body);
                    JToken token = obj.SelectToken("sessions");
                    if (token != null)
                    {
                        JArray arr = (JArray)token;
                        string[] result = new string[arr.Count];
                        for (int i = 0; i < arr.Count; i++)
                        {
                            result[i] = (string)arr[i];
                        }
                        return result;
                    }
                    return new string[0];
                }
            }
            catch
            {
                return new string[0];
            }
        }

        public void AbortMessage(string sessionId)
        {
            try
            {
                string url = _baseUrl + "/session/" + sessionId + "/abort";
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.ContentLength = 0;
                req.Timeout = 5000;
                req.ReadWriteTimeout = 5000;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    // Response discarded
                }
            }
            catch
            {
                // Best effort abort
            }
        }
    }

    /// <summary>
    /// Simple serializable payload for the prompt POST body.
    /// Using a class instead of anonymous type for .NET 2.0 compatibility.
    /// </summary>
    internal class SessionPayload
    {
        private string _workingDirectory;
        private string _history;

        public SessionPayload(string workingDirectory)
        {
            _workingDirectory = workingDirectory;
        }

        public string workingDirectory
        {
            get { return _workingDirectory; }
            set { _workingDirectory = value; }
        }

        public string history
        {
            get { return _history; }
            set { _history = value; }
        }
    }

    internal class PromptPayload
    {
        private string _prompt;

        public PromptPayload(string prompt)
        {
            _prompt = prompt;
        }

        public string prompt
        {
            get { return _prompt; }
            set { _prompt = value; }
        }
    }
}
