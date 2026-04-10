using System.Collections.Concurrent;

namespace TextToSqlAgent.API.Services;

/// <summary>
/// Tracks background semantic-indexing progress per connection so the UI can poll
/// without blocking on a long-running Qdrant indexing request.
/// </summary>
public class ConnectionIndexingTracker
{
    private readonly ConcurrentDictionary<string, ConnectionIndexingStatusState> _statuses = new();

    public ConnectionIndexingStatusSnapshot? Get(string connectionId)
    {
        if (!_statuses.TryGetValue(connectionId, out var state))
        {
            return null;
        }

        lock (state.SyncRoot)
        {
            return state.ToSnapshot();
        }
    }

    public ConnectionIndexingStatusSnapshot StartOrGetExisting(
        string connectionId,
        Action<ConnectionIndexingStatusState> initialize,
        out bool started)
    {
        while (true)
        {
            if (_statuses.TryGetValue(connectionId, out var existing))
            {
                lock (existing.SyncRoot)
                {
                    if (existing.IsActive)
                    {
                        started = false;
                        return existing.ToSnapshot();
                    }
                }
            }

            var created = new ConnectionIndexingStatusState
            {
                ConnectionId = connectionId
            };

            lock (created.SyncRoot)
            {
                initialize(created);
                created.UpdatedAt = DateTime.UtcNow;
            }

            if (_statuses.TryAdd(connectionId, created))
            {
                started = true;
                return created.ToSnapshot();
            }

            if (!_statuses.TryGetValue(connectionId, out existing))
            {
                continue;
            }

            lock (existing.SyncRoot)
            {
                if (existing.IsActive)
                {
                    started = false;
                    return existing.ToSnapshot();
                }
            }

            if (_statuses.TryUpdate(connectionId, created, existing))
            {
                started = true;
                return created.ToSnapshot();
            }
        }
    }

    public ConnectionIndexingStatusSnapshot Update(
        string connectionId,
        Action<ConnectionIndexingStatusState> update)
    {
        var state = _statuses.GetOrAdd(connectionId, id => new ConnectionIndexingStatusState
        {
            ConnectionId = id
        });

        lock (state.SyncRoot)
        {
            update(state);
            state.UpdatedAt = DateTime.UtcNow;
            return state.ToSnapshot();
        }
    }
}

public class ConnectionIndexingStatusSnapshot
{
    public string ConnectionId { get; set; } = string.Empty;
    public string Status { get; set; } = "idle";
    public string Stage { get; set; } = "idle";
    public string Message { get; set; } = string.Empty;
    public int ProgressPercent { get; set; }
    public bool SchemaCached { get; set; }
    public bool ChatReady { get; set; }
    public bool FingerprintMatched { get; set; }
    public int TableCount { get; set; }
    public int ColumnCount { get; set; }
    public int RelationshipCount { get; set; }
    public int ExpectedPointCount { get; set; }
    public int IndexedPointCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class ConnectionIndexingStatusState
{
    public object SyncRoot { get; } = new();
    public string ConnectionId { get; set; } = string.Empty;
    public string Status { get; set; } = "idle";
    public string Stage { get; set; } = "idle";
    public string Message { get; set; } = string.Empty;
    public int ProgressPercent { get; set; }
    public bool SchemaCached { get; set; }
    public bool ChatReady { get; set; }
    public bool FingerprintMatched { get; set; }
    public int TableCount { get; set; }
    public int ColumnCount { get; set; }
    public int RelationshipCount { get; set; }
    public int ExpectedPointCount { get; set; }
    public int IndexedPointCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public bool IsActive => Status is "queued" or "indexing";

    public ConnectionIndexingStatusSnapshot ToSnapshot()
    {
        return new ConnectionIndexingStatusSnapshot
        {
            ConnectionId = ConnectionId,
            Status = Status,
            Stage = Stage,
            Message = Message,
            ProgressPercent = ProgressPercent,
            SchemaCached = SchemaCached,
            ChatReady = ChatReady,
            FingerprintMatched = FingerprintMatched,
            TableCount = TableCount,
            ColumnCount = ColumnCount,
            RelationshipCount = RelationshipCount,
            ExpectedPointCount = ExpectedPointCount,
            IndexedPointCount = IndexedPointCount,
            ErrorMessage = ErrorMessage,
            StartedAt = StartedAt,
            UpdatedAt = UpdatedAt,
            CompletedAt = CompletedAt
        };
    }
}
