namespace Incantation.Chat
{
    public interface IChatRenderer
    {
        void SuspendPainting();
        void ResumePainting();
        void ScrollToEnd();
        void AppendUserMessage(string name, System.DateTime time, string content);
        void AppendAssistantHeader(string name, System.DateTime time);
        void AppendReasoning(string text);
        void EndReasoning();
        void AppendDelta(string text);
        void AppendToolCall(string summary, string detail);
        void AppendToolCall(string summary);
        void AppendError(string message);
        void AppendSystemMessage(string text);
        void AppendFileArtifact(string filePath);
        void AppendNewline();
        void FinalizeMessage();
        void Clear();
    }
}
