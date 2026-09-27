using Centinela.Core;
using Centinela.Media.Tests.Rtsp;

namespace Centinela.Media.Tests;

[Collection("rtsp")]
public sealed class StreamErrorTests(RtspTestServer server)
{
    const string WrongPassword = "Wr0ngPass!";

    StreamSession Open(string url)
    {
        Skip.If(server.SkipReason is not null, server.SkipReason);
        FFmpegLoader.Initialize();
        return new StreamSession(url);
    }

    static StreamError? FirstError(StreamSession session, TimeSpan timeout)
    {
        StreamError? first = null;
        session.ErrorOccurred += e => first ??= e;
        session.Start();
        TestUtil.WaitFor(() => first is not null, timeout);
        return first;
    }

    [SkippableFact]
    public void Wrong_password_is_AuthFailed_with_status_401_and_no_credentials()
    {
        using var session = Open(server.Url("secure", RtspTestServer.SecureUser, WrongPassword));
        var error = FirstError(session, TimeSpan.FromSeconds(10));
        Assert.NotNull(error);
        Assert.Equal(StreamErrorKind.AuthFailed, error.Kind);
        Assert.Equal(401, error.RtspStatus);
        Assert.DoesNotContain(WrongPassword, error.Detail);
        Assert.DoesNotContain(Uri.EscapeDataString(WrongPassword), error.Detail);
        Assert.DoesNotContain("viewer:", error.Detail);
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.AuthFailed, TimeSpan.FromSeconds(5)));
        Assert.Equal(StreamErrorKind.AuthFailed, session.LastErrorKind);
    }

    [SkippableFact]
    public void Unpublished_path_is_NotFound()
    {
        using var session = Open(server.Url("missing"));
        var error = FirstError(session, TimeSpan.FromSeconds(10));
        Assert.Equal(StreamErrorKind.NotFound, error?.Kind);
        Assert.True(TestUtil.WaitFor(() => session.State == SessionState.Reconnecting, TimeSpan.FromSeconds(3)));
    }

    [SkippableFact]
    public void Closed_port_is_Unreachable()
    {
        using var session = Open("rtsp://127.0.0.1:1/none");
        Assert.Equal(StreamErrorKind.Unreachable, FirstError(session, TimeSpan.FromSeconds(10))?.Kind);
    }
}
