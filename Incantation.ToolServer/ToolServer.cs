using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Incantation.ToolServer
{
    class Program
    {
        private static string _defaultCwd;
        private static bool _running = true;

        static void Main(string[] args)
        {
            int port = 8888;
            if (args.Length > 0)
            {
                try { port = int.Parse(args[0]); }
                catch { }
            }

            _defaultCwd = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (args.Length > 1)
            {
                _defaultCwd = args[1];
            }

            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            Console.WriteLine(string.Format("Incantation Tool Server listening on port {0}", port));
            Console.WriteLine(string.Format("Default directory: {0}", _defaultCwd));
            Console.WriteLine("Press Ctrl+C to stop.");

            while (_running)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(new WaitCallback(HandleClient), client);
                }
                catch (SocketException)
                {
                    // Transient accept error, continue
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }

            listener.Stop();
        }

        private static void HandleClient(object state)
        {
            TcpClient client = null;
            try
            {
                client = (TcpClient)state;
                NetworkStream stream = client.GetStream();
                stream.ReadTimeout = 30000;

                // Read HTTP request
                byte[] headerBuf = new byte[8192];
                int headerLen = 0;
                int headerEnd = -1;

                while (headerEnd < 0 && headerLen < headerBuf.Length)
                {
                    int n = stream.Read(headerBuf, headerLen, headerBuf.Length - headerLen);
                    if (n == 0) break;
                    headerLen += n;

                    // Look for \r\n\r\n
                    string partial = Encoding.ASCII.GetString(headerBuf, 0, headerLen);
                    headerEnd = partial.IndexOf("\r\n\r\n");
                }

                if (headerEnd < 0)
                {
                    client.Close();
                    return;
                }

                string headers = Encoding.ASCII.GetString(headerBuf, 0, headerEnd);
                int bodyStart = headerEnd + 4;
                int alreadyRead = headerLen - bodyStart;

                // Parse request line
                string[] lines = headers.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                if (lines.Length == 0)
                {
                    client.Close();
                    return;
                }
                string[] requestLine = lines[0].Split(' ');
                string method = requestLine[0];
                string path = requestLine.Length > 1 ? requestLine[1] : "/";

                // Parse Content-Length
                int contentLength = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = lines[i].Substring(15).Trim();
                        int.TryParse(val, out contentLength);
                    }
                }

                // Read body
                string body = "";
                if (contentLength > 0)
                {
                    byte[] bodyBuf = new byte[contentLength];
                    int copied = 0;

                    // Copy bytes already read past the header
                    if (alreadyRead > 0)
                    {
                        int toCopy = Math.Min(alreadyRead, contentLength);
                        Array.Copy(headerBuf, bodyStart, bodyBuf, 0, toCopy);
                        copied = toCopy;
                    }

                    // Read remaining body
                    while (copied < contentLength)
                    {
                        int n = stream.Read(bodyBuf, copied, contentLength - copied);
                        if (n == 0) break;
                        copied += n;
                    }

                    body = Encoding.UTF8.GetString(bodyBuf, 0, copied);
                }

                // Route request
                string responseBody = "";
                int statusCode = 200;

                path = path.ToLower();
                try
                {
                    if (method == "GET" && path == "/health")
                    {
                        responseBody = HandleHealth();
                    }
                    else if (method == "POST" && path == "/read")
                    {
                        responseBody = HandleRead(body);
                    }
                    else if (method == "POST" && path == "/write")
                    {
                        responseBody = HandleWrite(body);
                    }
                    else if (method == "POST" && path == "/list")
                    {
                        responseBody = HandleList(body);
                    }
                    else if (method == "POST" && path == "/command")
                    {
                        responseBody = HandleCommand(body);
                    }
                    else
                    {
                        statusCode = 404;
                        responseBody = "{\"success\":false,\"error\":\"Not found\"}";
                    }
                }
                catch (Exception ex)
                {
                    statusCode = 500;
                    JObject err = new JObject();
                    err["success"] = false;
                    err["error"] = ex.Message;
                    responseBody = err.ToString(Formatting.None);
                }

                // Send HTTP response
                byte[] responseBytes = Encoding.UTF8.GetBytes(responseBody);
                string responseHeader = string.Format(
                    "HTTP/1.1 {0} OK\r\nContent-Type: application/json\r\nContent-Length: {1}\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n",
                    statusCode, responseBytes.Length);

                byte[] headerBytes = Encoding.ASCII.GetBytes(responseHeader);
                stream.Write(headerBytes, 0, headerBytes.Length);
                stream.Write(responseBytes, 0, responseBytes.Length);
                stream.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("Request error: {0}", ex.Message));
            }
            finally
            {
                try { if (client != null) client.Close(); }
                catch { }
            }
        }

        // ================================================================
        // Handlers
        // ================================================================

        private static string HandleHealth()
        {
            JObject obj = new JObject();
            obj["status"] = "ok";
            obj["hostname"] = Environment.MachineName;
            obj["cwd"] = _defaultCwd;
            obj["platform"] = Environment.OSVersion.ToString();
            return obj.ToString(Formatting.None);
        }

        private static string HandleRead(string body)
        {
            JObject req = JObject.Parse(body);
            string filePath = (string)req["path"];
            if (filePath == null || filePath.Length == 0)
            {
                return Error("Missing 'path' field");
            }

            if (!File.Exists(filePath))
            {
                return Error(string.Format("File not found: {0}", filePath));
            }

            string content = File.ReadAllText(filePath);
            JObject result = new JObject();
            result["success"] = true;
            result["content"] = content;
            result["size"] = content.Length;
            return result.ToString(Formatting.None);
        }

        private static string HandleWrite(string body)
        {
            JObject req = JObject.Parse(body);
            string filePath = (string)req["path"];
            string content = (string)req["content"];

            if (filePath == null || filePath.Length == 0)
            {
                return Error("Missing 'path' field");
            }
            if (content == null)
            {
                content = "";
            }

            string dir = Path.GetDirectoryName(filePath);
            if (dir != null && dir.Length > 0 && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(filePath, content);

            JObject result = new JObject();
            result["success"] = true;
            result["path"] = filePath;
            result["bytesWritten"] = content.Length;
            return result.ToString(Formatting.None);
        }

        private static string HandleList(string body)
        {
            JObject req = JObject.Parse(body);
            string dirPath = (string)req["path"];
            if (dirPath == null || dirPath.Length == 0)
            {
                dirPath = _defaultCwd;
            }

            if (!Directory.Exists(dirPath))
            {
                return Error(string.Format("Directory not found: {0}", dirPath));
            }

            JArray entries = new JArray();

            string[] dirs = Directory.GetDirectories(dirPath);
            for (int i = 0; i < dirs.Length; i++)
            {
                DirectoryInfo di = new DirectoryInfo(dirs[i]);
                JObject entry = new JObject();
                entry["name"] = di.Name;
                entry["type"] = "directory";
                entry["modified"] = di.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
                entries.Add(entry);
            }

            string[] files = Directory.GetFiles(dirPath);
            for (int i = 0; i < files.Length; i++)
            {
                FileInfo fi = new FileInfo(files[i]);
                JObject entry = new JObject();
                entry["name"] = fi.Name;
                entry["type"] = "file";
                entry["size"] = fi.Length;
                entry["modified"] = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
                entries.Add(entry);
            }

            JObject result = new JObject();
            result["success"] = true;
            result["path"] = dirPath;
            result["entries"] = entries;
            return result.ToString(Formatting.None);
        }

        private static string HandleCommand(string body)
        {
            JObject req = JObject.Parse(body);
            string command = (string)req["command"];
            string workDir = (string)req["workingDirectory"];
            int timeout = 30000;

            JToken timeoutToken = req["timeout"];
            if (timeoutToken != null)
            {
                timeout = (int)timeoutToken;
            }

            if (command == null || command.Length == 0)
            {
                return Error("Missing 'command' field");
            }
            if (workDir == null || workDir.Length == 0)
            {
                workDir = _defaultCwd;
            }

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/c " + command;
            psi.WorkingDirectory = workDir;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;

            Process proc = Process.Start(psi);
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();

            bool exited = proc.WaitForExit(timeout);
            int exitCode = -1;
            if (exited)
            {
                exitCode = proc.ExitCode;
            }
            else
            {
                try { proc.Kill(); } catch { }
            }
            proc.Dispose();

            JObject result = new JObject();
            result["success"] = exited && exitCode == 0;
            result["exitCode"] = exitCode;
            result["stdout"] = stdout;
            result["stderr"] = stderr;
            if (!exited)
            {
                result["error"] = string.Format("Command timed out after {0}ms", timeout);
            }
            return result.ToString(Formatting.None);
        }

        private static string Error(string message)
        {
            JObject obj = new JObject();
            obj["success"] = false;
            obj["error"] = message;
            return obj.ToString(Formatting.None);
        }
    }
}
