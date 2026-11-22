using System.Net.Http;
using Tabletop.Client.Core;
using Tabletop.Protocol;

namespace Tabletop.Client.Tests;

public class FeedbackTests
{
    const string Url = "http://127.0.0.1:5100";

    [Theory]
    [InlineData(RefusalReason.OutsideLegalSet, "Not one of the choices offered: no such option.")]
    [InlineData(RefusalReason.Illegal, "Against the rules: move_allowance.")]
    [InlineData(RefusalReason.WrongStatus, "Not at this point of the game: setup has already started.")]
    [InlineData(RefusalReason.NotPermitted, "Not allowed: only the creator starts setup.")]
    [InlineData(RefusalReason.Unknown, "Refused: more than 64 terrain pieces.")]
    public void A_refusal_names_its_kind_and_carries_the_server_s_detail(RefusalReason reason, string expected)
    {
        var detail = expected[(expected.IndexOf(": ", StringComparison.Ordinal) + 2)..^1];
        Assert.Equal(expected, Feedback.Refusal(new Refused(Guid.NewGuid(), reason, detail)));
    }

    [Fact]
    public void A_refusal_the_decision_box_shows_is_not_toasted_again()
    {
        var sent = Guid.NewGuid();
        var refused = new Refused(sent, RefusalReason.Illegal, "Flayer Cavalry would end out of coherency.");
        Assert.Null(Feedback.Refusal(refused, shownInBox: sent));
        Assert.Equal("Against the rules: Flayer Cavalry would end out of coherency.", Feedback.Refusal(refused, shownInBox: Guid.NewGuid()));
    }

    [Fact]
    public void A_refusal_keeps_its_own_full_stop_and_says_something_without_a_detail()
    {
        Assert.Equal("Not allowed: not your army.", Feedback.Refusal(new Refused(Guid.NewGuid(), RefusalReason.NotPermitted, "not your army.")));
        Assert.Equal("Not allowed.", Feedback.Refusal(new Refused(Guid.NewGuid(), RefusalReason.NotPermitted, "")));
    }

    [Fact]
    public void A_watcher_s_command_is_explained_and_its_refused_presence_is_silent()
    {
        Assert.Equal("You are watching this game.", Feedback.Refusal(new Refused(Guid.NewGuid(), RefusalReason.Watcher, "a watcher sends nothing that changes the game")));
        Assert.Null(Feedback.Refusal(new Refused(Guid.Empty, RefusalReason.Watcher, "a watcher sends nothing that changes the game")));
    }

    [Theory]
    [InlineData(1000, "replaced by a newer connection", false, "This game was opened in another window, which took over.")]
    [InlineData(1000, "replaced: too many connections for this account", false, "Your account has too many game windows open; the oldest closed.")]
    [InlineData(1001, "shutting down", true, "The game server is restarting; reconnecting…")]
    [InlineData(1008, "session expired", true, "Your sign-in on the game server expired; reconnecting with a fresh one…")]
    [InlineData(1009, "frame too large", true, "A message was larger than the server accepts; reconnecting…")]
    [InlineData(1011, "this game session has faulted", true, "The game server failed (this game session has faulted); reconnecting…")]
    [InlineData(1013, "too far behind", true, "The connection fell too far behind the game; reconnecting…")]
    [InlineData(4000, "odd", false, "Disconnected: odd.")]
    public void A_close_code_reads_as_what_happened(int code, string reason, bool reconnecting, string expected) =>
        Assert.Equal(expected, Feedback.Closed(new SocketClosed(code, reason, null), reconnecting));

    [Fact]
    public void A_socket_that_never_opened_names_the_server_and_a_desync_names_the_rebuild()
    {
        Assert.Equal("Cannot reach the game server (Connection refused); reconnecting…", Feedback.Closed(new SocketClosed(null, "Connection refused", new IOException("x")), true));
        Assert.Equal("The connection dropped; reconnecting…", Feedback.Closed(new SocketClosed(null, null, null), true));
        var desync = new ProjectionDesyncException("models_moved", Guid.Empty);
        Assert.Equal("Rebuilding the table from the log…", Feedback.Closed(new SocketClosed(null, desync.Message, desync), true));
        Assert.StartsWith("This game's log cannot be replayed: ", Feedback.Closed(new SocketClosed(null, desync.Message, desync), false));
    }

    [Theory]
    [InlineData(RejectReason.UpgradeRequired, "This client is older than the game server accepts.")]
    [InlineData(RejectReason.InvalidSession, "The game server did not accept your sign-in. Sign out and in again if it happens again.")]
    [InlineData(RejectReason.NotAParticipant, "You are not in that game: join it from the list.")]
    public void A_rejection_reads_as_what_to_do(RejectReason reason, string expected) => Assert.Equal(expected, Feedback.Rejected(reason));

    [Theory]
    [InlineData(401, "Unauthorized", null, "Your sign-in has expired: sign in again.")]
    [InlineData(403, "Forbidden", "not yours", "Not allowed: not yours.")]
    [InlineData(404, "Not found", "no game", "Not found: no game.")]
    [InlineData(413, "Payload too large", null, "Too large for the server.")]
    [InlineData(422, "Unprocessable", "name is at most 64 characters.", "name is at most 64 characters.")]
    [InlineData(429, "Too many requests", "Too many requests. Wait a minute and try again.", "Too many requests. Wait a minute and try again.")]
    [InlineData(429, "Too many requests", null, "Too many requests. Wait a minute and try again.")]
    [InlineData(500, "Internal Server Error", null, "The server failed (Internal Server Error). Try again shortly.")]
    [InlineData(409, "Conflict", "already joined", "Conflict: already joined.")]
    public void A_management_status_reads_as_what_happened(int status, string title, string? detail, string expected) =>
        Assert.Equal(expected, Feedback.Error(new ManagementException(status, title, detail), Url));

    [Fact]
    public void Network_failures_name_the_server()
    {
        Assert.Equal($"Cannot reach {Url}: refused", Feedback.Error(new HttpRequestException("refused"), Url));
        Assert.Equal($"{Url} did not answer in time.", Feedback.Error(new TaskCanceledException("t"), Url));
        Assert.Equal("This client is older than the server accepts.", Feedback.Error(new UpdateRequiredException("Update required", null), Url));
        Assert.Equal("boom", Feedback.Error(new InvalidOperationException("boom"), Url));
    }
}
