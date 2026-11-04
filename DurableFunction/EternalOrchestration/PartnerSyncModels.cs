namespace DurableFunction.EternalOrchestration;

// The ONLY state carried from one generation to the next. Keep it minimal:
// a cursor and a counter, not a growing list of everything synced so far.
public record SyncState(string PartnerId, string? Cursor, long Generation, int IntervalSeconds);

public record SyncResult(string? NextCursor, int ItemsSynced);
