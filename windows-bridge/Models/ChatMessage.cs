using System;

namespace XycloToothBridge.Models;

public enum ChatSender
{
    User,
    AI,
    System
}

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public ChatSender Sender { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string? FilePath { get; set; }
    public string Status { get; set; } = string.Empty;
}
