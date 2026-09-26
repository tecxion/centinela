namespace CamaraWin.Core;

public enum StreamErrorKind { AuthFailed, Unreachable, NotFound, CameraBusy, ServerError, Stalled, Unknown }

/// <summary>One failed connection attempt or stall. Detail is sanitized (never contains credentials).</summary>
public sealed record StreamError(StreamErrorKind Kind, int? RtspStatus, int FfmpegCode, string Detail, DateTime TimeUtc);
