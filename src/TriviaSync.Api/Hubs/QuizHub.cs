using Microsoft.AspNetCore.SignalR;
using TriviaSync.Api.Models;
using TriviaSync.Api.Services;

namespace TriviaSync.Api.Hubs;

public class QuizHub : Hub<IQuizClient>
{
    private readonly IGameEngineService _gameEngine;
    private readonly ITournamentService _tournaments;
    private readonly IAuthService _auth;
    private readonly ILogger<QuizHub> _logger;

    public QuizHub(IGameEngineService gameEngine, ITournamentService tournaments, IAuthService auth, ILogger<QuizHub> logger)
    {
        _gameEngine = gameEngine;
        _tournaments = tournaments;
        _auth = auth;
        _logger = logger;
    }

    public async Task JoinRoom(string pin, string fullName, string identifier = "", string organizationId = "global")
    {
        var cleanPin = pin?.Trim().ToUpperInvariant() ?? string.Empty;
        var session = _gameEngine.GetSession(cleanPin);

        if (session == null)
        {
            await Clients.Caller.ErrorNotification("No game is running with that PIN. Check the code on the host's screen.");
            return;
        }

        // Tournament games count toward standings, so players must be signed in. Joining with the
        // PIN shown in the room enrolls them in the tournament, and their account name is used.
        if (!string.IsNullOrEmpty(session.TournamentSessionId))
        {
            var tournamentSession = _tournaments.GetSession(session.TournamentSessionId);
            var tournament = tournamentSession == null ? null : _tournaments.Get(tournamentSession.TournamentId);
            var email = Context.User.UserEmail();
            var user = email == null ? null : _auth.FindUser(email);
            if (tournament == null)
            {
                await Clients.Caller.ErrorNotification("This game is no longer available.");
                return;
            }
            if (user == null)
            {
                await Clients.Caller.SignInRequired(new { tournamentName = tournament.Name });
                return;
            }
            if (tournament.HostEmail == user.Email)
            {
                await Clients.Caller.ErrorNotification("You're hosting this tournament, so you can't play in it.");
                return;
            }

            _tournaments.EnsureMember(tournament.Id, user.Email, user.DisplayName);
            fullName = user.DisplayName;
            identifier = user.Email;
            // Keep two members with the same display name from sharing a seat.
            organizationId = user.Email;
        }

        var (success, message, player) = _gameEngine.JoinOrReconnectPlayer(
            cleanPin, Context.ConnectionId, fullName, identifier, organizationId);

        if (!success || player == null)
        {
            await Clients.Caller.ErrorNotification(message);
            return;
        }

        // Add to SignalR group for the room
        await Groups.AddToGroupAsync(Context.ConnectionId, cleanPin);

        // Notify room of player join
        await Clients.Group(cleanPin).PlayerJoined(new
        {
            fullName = player.FullName,
            playerId = player.PlayerId,
            totalCount = session.ConnectedPlayerCount,
            allPlayers = session.Players.Values.Where(p => p.IsConnected).Select(p => new
            {
                playerId = p.PlayerId,
                fullName = p.FullName,
                score = p.Score,
                rank = p.Rank
            }).ToList()
        });

        // Send current room state to reconnecting/joining player
        await Clients.Caller.RoomState(new
        {
            pin = session.Pin,
            title = session.Quiz.Title,
            state = session.State.ToString(),
            currentQuestionIndex = session.CurrentQuestionIndex,
            totalQuestions = session.Quiz.Questions.Count,
            hostId = session.HostId,
            tournamentId = session.TournamentId,
            tournamentName = session.TournamentName,
            sessionNumber = session.SessionNumber,
            totalSessions = session.TotalSessions,
            sessionType = session.SessionType,
            player = new
            {
                playerId = player.PlayerId,
                fullName = player.FullName,
                score = player.Score,
                rank = player.Rank,
                streak = player.CurrentStreak,
                lastAnswerQuestionIndex = player.LastAnswerQuestionIndex,
                lastAnswerPoints = player.LastAnswerPoints,
                lastAnswerIsCorrect = player.LastAnswerIsCorrect
            }
        });

        // If a question is currently active, send question details (without correct answer)
        if (session.State == GameState.QuestionActive && session.CurrentQuestionIndex >= 0 && session.CurrentQuestionIndex < session.Quiz.Questions.Count)
        {
            var q = session.Quiz.Questions[session.CurrentQuestionIndex];
            await Clients.Caller.QuestionStarted(new
            {
                index = session.CurrentQuestionIndex,
                questionNumber = session.CurrentQuestionIndex + 1,
                totalQuestions = session.Quiz.Questions.Count,
                text = q.Text,
                choices = q.Choices,
                timeLimit = session.RemainingSeconds,
                points = q.Points,
                alreadyAnswered = session.CurrentRoundAnswers.ContainsKey(player.PlayerId)
            });
        }
    }

    /// <summary>
    /// Resolves the session for a host-only action and checks the caller owns it (or is an admin).
    /// Sends an error to the caller and returns null otherwise.
    /// </summary>
    private async Task<GameSession?> GetControlledSessionAsync(string? pin)
    {
        var cleanPin = pin?.Trim().ToUpperInvariant() ?? string.Empty;
        var session = _gameEngine.GetSession(cleanPin);

        if (session == null)
        {
            await Clients.Caller.ErrorNotification("Room PIN not found.");
            return null;
        }

        if (!Context.User.CanControl(session))
        {
            _logger.LogWarning("Rejected host action on {Pin} from {User}", cleanPin, Context.User.UserEmail() ?? "anonymous");
            await Clients.Caller.ErrorNotification("Only the host who created this game can control it.");
            return null;
        }

        return session;
    }

    public async Task HostJoin(string pin)
    {
        var session = await GetControlledSessionAsync(pin);
        if (session == null)
        {
            return;
        }
        var cleanPin = session.Pin;

        _gameEngine.RegisterHostConnection(cleanPin, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, cleanPin);

        await Clients.Caller.RoomState(new
        {
            pin = session.Pin,
            title = session.Quiz.Title,
            state = session.State.ToString(),
            currentQuestionIndex = session.CurrentQuestionIndex,
            totalQuestions = session.Quiz.Questions.Count,
            connectedPlayerCount = session.ConnectedPlayerCount,
            hostId = session.HostId,
            tournamentId = session.TournamentId,
            tournamentName = session.TournamentName,
            sessionNumber = session.SessionNumber,
            totalSessions = session.TotalSessions,
            sessionType = session.SessionType,
            autoAdvance = session.AutoAdvance,
            allPlayers = session.Players.Values.Where(p => p.IsConnected).Select(p => new
            {
                playerId = p.PlayerId,
                fullName = p.FullName,
                score = p.Score,
                rank = p.Rank
            }).ToList()
        });

        // A host re-attaching mid-question needs the question back on screen.
        if (session.State == GameState.QuestionActive && session.CurrentQuestionIndex >= 0 && session.CurrentQuestionIndex < session.Quiz.Questions.Count)
        {
            var q = session.Quiz.Questions[session.CurrentQuestionIndex];
            await Clients.Caller.QuestionStarted(new
            {
                index = session.CurrentQuestionIndex,
                questionNumber = session.CurrentQuestionIndex + 1,
                totalQuestions = session.Quiz.Questions.Count,
                text = q.Text,
                choices = q.Choices,
                timeLimit = session.RemainingSeconds,
                points = q.Points,
                answeredCount = session.CurrentRoundAnswers.Count
            });
        }
    }

    public async Task StartQuiz(string pin)
    {
        var session = await GetControlledSessionAsync(pin);
        if (session == null)
        {
            return;
        }

        var started = await _gameEngine.StartQuizAsync(session.Pin);
        if (!started)
        {
            await Clients.Caller.ErrorNotification("Unable to start quiz. Check room status and questions.");
        }
    }

    public async Task SubmitAnswer(string pin, int questionIndex, int choiceIndex)
    {
        var cleanPin = pin?.Trim().ToUpperInvariant() ?? string.Empty;
        var (success, message, submission) = _gameEngine.SubmitAnswer(cleanPin, Context.ConnectionId, questionIndex, choiceIndex);

        if (!success)
        {
            await Clients.Caller.ErrorNotification(message);
        }
    }

    public async Task AdvanceQuestion(string pin)
    {
        var session = await GetControlledSessionAsync(pin);
        if (session == null)
        {
            return;
        }

        var advanced = await _gameEngine.AdvanceQuestionAsync(session.Pin);
        if (!advanced)
        {
            await Clients.Caller.ErrorNotification("Unable to advance question.");
        }
    }

    public async Task SetAutoAdvance(string pin, bool enabled)
    {
        var session = await GetControlledSessionAsync(pin);
        if (session == null)
        {
            return;
        }

        session.AutoAdvance = enabled;
    }

    public async Task EndQuestion(string pin)
    {
        var session = await GetControlledSessionAsync(pin);
        if (session == null)
        {
            return;
        }

        if (!_gameEngine.EndQuestionEarly(session.Pin))
        {
            await Clients.Caller.ErrorNotification("There's no question running right now.");
        }
    }

    public async Task KickPlayer(string pin, string playerId)
    {
        var controlled = await GetControlledSessionAsync(pin);
        if (controlled == null)
        {
            return;
        }

        var cleanPin = controlled.Pin;
        var (success, kickedConnId) = _gameEngine.KickPlayer(cleanPin, playerId);

        if (success)
        {
            if (!string.IsNullOrEmpty(kickedConnId))
            {
                await Clients.Client(kickedConnId).PlayerKicked(new
                {
                    message = "You have been removed from the session by the host."
                });
            }

            var session = _gameEngine.GetSession(cleanPin);
            if (session != null)
            {
                await Clients.Group(cleanPin).PlayerLeft(new
                {
                    playerId = playerId,
                    totalCount = session.ConnectedPlayerCount
                });
            }
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var (pin, player) = _gameEngine.HandlePlayerDisconnect(Context.ConnectionId);
        if (pin != null && player != null)
        {
            var session = _gameEngine.GetSession(pin);
            int count = session?.ConnectedPlayerCount ?? 0;

            await Clients.Group(pin).PlayerLeft(new
            {
                fullName = player.FullName,
                playerId = player.PlayerId,
                totalCount = count
            });
        }

        await base.OnDisconnectedAsync(exception);
    }
}
