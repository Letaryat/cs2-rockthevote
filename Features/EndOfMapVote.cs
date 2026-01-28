using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using cs2_rockthevote.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using Microsoft.Extensions.DependencyInjection;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using Microsoft.Extensions.Logging;

namespace cs2_rockthevote
{
    public class EndOfMapVote : IPluginDependency<Plugin, Config>
    {
        private readonly StringLocalizer? _localizer;
        private TimeLimitManager _timeLimit;
        private MaxRoundsManager _maxRounds;
        private PluginState _pluginState;
        private GameRules _gameRules;
        private EndMapVoteManager _voteManager;

        private ChangeMapManager _changeMapManager;
        private EndOfMapConfig _config = new();
        private Timer? _timer;
        private bool deathMatch => _gameMode?.GetPrimitiveValue<int>() == 2 && _gameType?.GetPrimitiveValue<int>() == 1;
        private ConVar? _gameType;
        private ConVar? _gameMode;
        private MapLister _mapLister;


        // overload for multilang support
        public EndOfMapVote(StringLocalizer localizer, TimeLimitManager timeLimit, MaxRoundsManager maxRounds, PluginState pluginState, GameRules gameRules, EndMapVoteManager voteManager, MapLister mapLister, ChangeMapManager changeMapManager)
        {
            _localizer = localizer;
            _timeLimit = timeLimit;
            _maxRounds = maxRounds;
            _pluginState = pluginState;
            _gameRules = gameRules;
            _voteManager = voteManager;
            _mapLister = mapLister;
            _changeMapManager = changeMapManager;
        }
        public EndOfMapVote(TimeLimitManager timeLimit, MaxRoundsManager maxRounds, PluginState pluginState, GameRules gameRules, EndMapVoteManager voteManager, MapLister mapLister, ChangeMapManager changeMapManager)
        {
            //_localizer = new StringLocalizer();
            _timeLimit = timeLimit;
            _maxRounds = maxRounds;
            _pluginState = pluginState;
            _gameRules = gameRules;
            _voteManager = voteManager;
            _mapLister = mapLister;
            _changeMapManager = changeMapManager;
        }

        bool CheckMaxRounds()
        {
            //Server.PrintToChatAll($"Remaining rounds {_maxRounds.RemainingRounds}, remaining wins: {_maxRounds.RemainingWins}, triggerBefore {_config.TriggerRoundsBeforeEnd}");
            // Prevent triggering vote too early
            if (_gameRules.TotalRoundsPlayed < 1) // This line is a newly added safeguard
                return false;
            if (_maxRounds.UnlimitedRounds)
                return false;
            if (_maxRounds.RemainingRounds <= _config.TriggerRoundsBeforeEnd)
                return true;
            return _maxRounds.CanClinch && _maxRounds.RemainingWins <= _config.TriggerRoundsBeforeEnd;
        }


        bool CheckTimeLeft()
        {
            return !_timeLimit.UnlimitedTime && _timeLimit.TimeRemaining <= _config.TriggerSecondsBeforeEnd;
        }

        public void StartVote()
        {
            KillTimer();
            if (_config.Enabled)
            {
                if (_pluginState.EofVoteHappening)
                {
                    Server.PrintToChatAll($"[RockTheVote] End of map vote is already in progress. Cannot start a new vote.");
                    return;
                }
                _voteManager.StartVote(_config);
            }
        }

        public void OnMapStart(string map)
        {
            KillTimer();
        }

        void KillTimer()
        {
            _timer?.Kill();
            _timer = null;
        }



        public void OnLoad(Plugin plugin)
        {
            _gameMode = ConVar.Find("game_mode");
            _gameType = ConVar.Find("game_type");

            void MaybeStartTimer()
            {
                KillTimer();
                if (!_timeLimit.UnlimitedTime && _config.Enabled)
                {
                    _timer = plugin.AddTimer(1.0F, () =>
                    {
                        if (_gameRules is not null && !_gameRules.WarmupRunning && !_pluginState.DisableCommands && _timeLimit.TimeRemaining > 0)
                        {
                            if (CheckTimeLeft() && !_pluginState.EofVoteHappening)
                                StartVote();
                        }
                    }, TimerFlags.REPEAT);
                }
            }

            plugin.RegisterEventHandler<EventRoundStart>((ev, info) =>
            {
                if (!_pluginState.DisableCommands && !_gameRules.WarmupRunning && CheckMaxRounds() && _config.Enabled && !_pluginState.EofVoteHappening)
                    StartVote();
                else if (deathMatch)
                {
                    MaybeStartTimer();
                }

                return HookResult.Continue;
            });

            plugin.RegisterEventHandler<EventRoundAnnounceMatchStart>((ev, info) =>
            {
                MaybeStartTimer();
                return HookResult.Continue;
            });

            plugin.RegisterEventHandler<EventCsWinPanelMatch>((ev, info) =>
            {
#if DEBUG
                plugin?.Logger.LogInformation("WinPanelMatch active. Ending voting so nextlevel wont not be null.");
#endif
                _voteManager.timeLeft = -1; // This ends if voting is still going.

                plugin?.AddTimer(1.0f, () =>
                {
                    Map mapInfo = _mapLister.Maps!.FirstOrDefault(x => x.Name == _voteManager.winner.Key!)!;

                    if (mapInfo == null)
                    {
                        mapInfo = _mapLister.Maps![0];
#if DEBUG
                        plugin?.Logger.LogInformation($"Somehow mapInfo is null. Taking the first one in maplist: {mapInfo.Name}");
#endif
                    }

                    if (_config.ForceChangeOnWinPanelMatch)
                    {
#if DEBUG
                        plugin?.Logger.LogInformation($"ForceChangeOnWinPanelMatch = True. Changing map.");
#endif
                        _changeMapManager.ChangeNextMap();
                    }
                    else
                    {
#if DEBUG
                        plugin?.Logger.LogInformation($"Checking if it is a workshop map not from collection.");
#endif

                        if (mapInfo.Id is not null)
                        {
#if DEBUG
                            plugin?.Logger.LogInformation($"Map not from collection. Executing host_workshop_map before being brokey! ");
#endif
                            Server.ExecuteCommand($"host_workshop_map {mapInfo.Id}");
                        }
                    }
                });
                return HookResult.Continue;
            }, HookMode.Pre);

            plugin.RegisterEventHandler<EventNextlevelChanged>((e, i) =>
            {
                //Map mapInfo = _mapLister.Maps!.FirstOrDefault(x => x.Name == _voteManager.winner.Key!)!;
#if DEBUG
                plugin?.Logger.LogInformation($"EventNextLevelChanged: Map is changing from: {Server.MapName} to -> {e.Nextlevel}");
#endif
                if (Server.MapName == "" || e.Nextlevel == "")
                {
                    Random rnd = new();
                    var randomMap = _mapLister.Maps!.ElementAt(rnd.Next(0, _mapLister.Maps!.Count()));
#if DEBUG
                    plugin?.Logger.LogInformation($"EventNextLevelChanged: Map is empty. Changing to random: {randomMap.Id} | {randomMap.Name}");
#endif

                    if (Server.IsMapValid(randomMap.Name))
                    {
#if DEBUG
                        plugin?.Logger.LogInformation($"EventNextLevelChanged: Executing changelevel command for {randomMap.Name}");
#endif
                        Server.ExecuteCommand($"nextlevel {randomMap.Name}"); // Better to be safe if nextlevel somehow will be still null
                        Server.ExecuteCommand($"changelevel {randomMap.Name}");
                    }
                    else if (randomMap.Id is not null)
                    {
#if DEBUG
                        plugin?.Logger.LogInformation($"EventNextLevelChanged: Executing host_workshop_map command for map ID {randomMap.Id}");
#endif
                        //Server.ExecuteCommand($"nextlevel {map.Name}");  // Better to be safe if nextlevel somehow will be still null
                        Server.ExecuteCommand($"host_workshop_map {randomMap.Id}");
                    }
                    else
                    {
#if DEBUG
                        plugin?.Logger.LogInformation($"EventNextLevelChanged: Executing ds_workshop_changelevel command for {randomMap.Name}");
#endif
                        Server.ExecuteCommand($"nextlevel {randomMap.Name}");  // Better to be safe if nextlevel somehow will be still null
                        Server.ExecuteCommand($"ds_workshop_changelevel {randomMap.Name}");
                    }
                }
                return HookResult.Continue;
            });

            plugin.RegisterEventHandler<EventRoundStart>((e, i) =>
            {
                if (Server.MapName == "")
                {
                    Random rnd = new();
                    var randomMap = _mapLister.Maps!.ElementAt(rnd.Next(0, _mapLister.Maps!.Count()));
#if DEBUG
                    plugin?.Logger.LogInformation($"EventNextLevelChanged: Map is empty. Changing to random: {randomMap.Id} | {randomMap.Name}");
#endif

                    if (Server.IsMapValid(randomMap.Name))
                    {
#if DEBUG
                        plugin?.Logger.LogInformation($"EventNextLevelChanged: Executing changelevel command for {randomMap.Name}");
#endif
                        Server.ExecuteCommand($"nextlevel {randomMap.Name}"); // Better to be safe if nextlevel somehow will be still null
                        Server.ExecuteCommand($"changelevel {randomMap.Name}");
                    }
                    else if (randomMap.Id is not null)
                    {
#if DEBUG
                        plugin?.Logger.LogInformation($"EventNextLevelChanged: Executing host_workshop_map command for map ID {randomMap.Id}");
#endif
                        //Server.ExecuteCommand($"nextlevel {map.Name}");  // Better to be safe if nextlevel somehow will be still null
                        Server.ExecuteCommand($"host_workshop_map {randomMap.Id}");
                    }
                    else
                    {
#if DEBUG
                        plugin?.Logger.LogInformation($"EventNextLevelChanged: Executing ds_workshop_changelevel command for {randomMap.Name}");
#endif
                        Server.ExecuteCommand($"nextlevel {randomMap.Name}");  // Better to be safe if nextlevel somehow will be still null
                        Server.ExecuteCommand($"ds_workshop_changelevel {randomMap.Name}");
                    }
                }
                return HookResult.Continue;
            });

        }

        public void OnConfigParsed(Config config)
        {
            _config = config.EndOfMapVote;
        }
    }
}
