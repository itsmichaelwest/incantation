using System;
using Newtonsoft.Json.Linq;

namespace Incantation.Net
{
    public static class SseParser
    {
        public static string ParseEventType(string json)
        {
            try
            {
                JObject obj = JObject.Parse(json);
                JToken token = obj.SelectToken("type");
                if (token != null)
                {
                    return (string)token;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        public static string ParseContent(string json)
        {
            try
            {
                JObject obj = JObject.Parse(json);
                JToken token = obj.SelectToken("content");
                if (token != null)
                {
                    return (string)token;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        public static string ParseToolName(string json)
        {
            try
            {
                JObject obj = JObject.Parse(json);
                JToken token = obj.SelectToken("tool");
                if (token != null)
                {
                    return (string)token;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        public static string ParseErrorMessage(string json)
        {
            try
            {
                JObject obj = JObject.Parse(json);
                JToken token = obj.SelectToken("message");
                if (token != null)
                {
                    return (string)token;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
