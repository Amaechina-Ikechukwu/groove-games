using Microsoft.AspNetCore.SignalR;
using TriviaSync.Api.Models;
using TriviaSync.Api.Services;

namespace TriviaSync.Api.Hubs;

public class QuizHub : Hub<IQuizClient>
{
    private readonly IGameEngineService _gameEngine;
    private readonly ILogger<QuizHub> _logger;

    public QuizHub(IGameEngineService gameEngine, ILogger<QuizHub> logger)
    {
        _gameEngine = gameEngine;
        _logger = logger;
    }

    public async Task JoinRoom(string pin, string fullName, string identifier = "", string organizationId = "global")
    {
        var cleanPin = pin?.Trim().ToUpperInvariant() ?? string.Empty;
        var session = _gameEngine.GetSession(cleanPin);

        if (session == null)
        {
            await Clients.Caller.ErrorNotification("Room PIN not found. Please verify the code.");
            return;
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

    public async Task HostJoin(string pin)
    {
        var cleanPin = pin?.Trim().ToUpperInvariant() ?? string.Empty;
        var session = _gameEngine.GetSession(cleanPin);

        if (session == null)
        {
            await Clients.Caller.ErrorNotification("Room PIN not found.");
            return;
        }

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
            allPlayers = session.Players.Values.Where(p => p.IsConnected).Select(p => new
            {
                playerId = p.PlayerId,
                fullName = p.FullName,
                score = p.Score,
                rank = p.Rank
            }).ToList()
        });
    }

    public async Task StartQuiz(string pin)
    {
        var cleanPin = pin?.Trim().ToUpperInvariant() ?? string.Empty;
        var started = await _gameEngine.StartQuizAsync(cleanPin);
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
        var cleanPin = pin?.Trim().ToUpperInvariant() ?? string.Empty;
        var advanced = await _gameEngine.AdvanceQuestionAsync(cleanPin);
        if (!advanced)
        {
            await Clients.Caller.ErrorNotification("Unable to advance question.");
        }
    }

    public async Task KickPlayer(string pin, string playerId)
    {
        var cleanPin = pin?.Trim().ToUpperInvariant() ?? string.Empty;
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
