using ComCross.Shared.Models;

namespace ComCross.Shared.Interfaces;

/// <summary>
/// Message stream service for managing log messages
/// </summary>
public interface IMessageStreamService
{
    /// <summary>
    /// Appends a message to the stream
    /// </summary>
    void Append(string sessionId, LogMessage message);

    /// <summary>
    /// Gets messages for a specific session
    /// </summary>
    IReadOnlyList<LogMessage> GetMessages(string sessionId, int skip = 0, int take = 100);

    /// <summary>
    /// Searches messages
    /// </summary>
    IReadOnlyList<LogMessage> Search(string sessionId, string query, bool isRegex = false);

    /// <summary>
    /// Clears messages for a session
    /// </summary>
    void Clear(string sessionId);

    /// <summary>
    /// Subscribes to new messages
    /// </summary>
    IDisposable Subscribe(string sessionId, Action<LogMessage> handler);
}
