﻿using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
namespace cs2_rockthevote
{
    public partial class Plugin
    {
        [GameEventHandler(HookMode.Post)]
        public HookResult OnRoundEndMapChanger(EventRoundEnd @event, GameEventInfo info)
        {
            _changeMapManager.ChangeNextMap();
            return HookResult.Continue;
        }

        [GameEventHandler(HookMode.Post)]
        public HookResult OnRoundStartMapChanger(EventRoundStart @event, GameEventInfo info)
        {
            _changeMapManager.ChangeNextMap();
            return HookResult.Continue;
        }
    }

    public class ChangeMapManager : IPluginDependency<Plugin, Config>
    {
        private Plugin? _plugin;
        private StringLocalizer _localizer;
        private PluginState _pluginState;
        private MapLister _mapLister;

        
        public string? NextMap { get; private set; } = null;
        private string _prefix = DEFAULT_PREFIX;
        private const string DEFAULT_PREFIX = "rtv.prefix";
        private bool _mapEnd = false;
        public string? fileName = null;
        private Map[] _maps = new Map[0];
        private Config _config;
        public CounterStrikeSharp.API.Modules.Timers.Timer? reservedTimer = null;
        public ChangeMapManager(StringLocalizer localizer, PluginState pluginState, MapLister mapLister)
        {
            _localizer = localizer;
            _pluginState = pluginState;
            _mapLister = mapLister;
            _mapLister.EventMapsLoaded += OnMapsLoaded;
        }

        public void OnMapsLoaded(object? sender, Map[] maps)
        {
            _maps = maps;
        }


        public void ScheduleMapChange(string map, bool mapEnd = false, string prefix = DEFAULT_PREFIX)
        {
            NextMap = map;
            _prefix = prefix;
            _pluginState.MapChangeScheduled = true;
            _mapEnd = mapEnd;
        }

        public void OnMapStart(string _map)
        {
            fileName = $"auto-{DateTime.Now:yyyyMMdd-HHmm}-{_map}-CS2_____Arena_1v1_____Pierdolnik.eu___1shot1kill.pl";
            Server.NextWorldUpdate(() => Server.ExecuteCommand($"tv_record \"replays/{fileName}.dem\""));
            NextMap = null;
            _prefix = DEFAULT_PREFIX;
        }

        public bool ChangeNextMap(bool mapEnd = false)
        {
            if (mapEnd != _mapEnd)
                return false;

            if (!_pluginState.MapChangeScheduled)
                return false;

            _pluginState.MapChangeScheduled = false;
            Server.PrintToChatAll(_localizer.LocalizeWithPrefixInternal(_prefix, "general.changing-map", NextMap!));
            _plugin.AddTimer(3.0F, () =>
            {
                Map map = _maps.FirstOrDefault(x => x.Name == NextMap!)!;
                if (Server.IsMapValid(map.Name))
                {
                    Server.ExecuteCommand("tv_stoprecord");
                    _plugin.AddTimer(1.0F, () =>{
                        Server.ExecuteCommand($"changelevel {map.Name}");
                    });
                }
                else if (map.Id is not null)
                {
                    Server.ExecuteCommand("tv_stoprecord");
                    _plugin.AddTimer(1.0F, () =>{
                        Server.ExecuteCommand($"host_workshop_map {map.Id}");                        
                    });

                }
                else
                    Server.ExecuteCommand("tv_stoprecord");
                    _plugin.AddTimer(1.0F, () =>{
                        Server.ExecuteCommand($"ds_workshop_changelevel {map.Name}");
                    });

            });
            return true;
        }

        public void OnConfigParsed(Config config)
        {
            _config = config;
        }

        public void OnLoad(Plugin plugin)
        {
            _plugin = plugin;
            plugin.RegisterEventHandler<EventCsWinPanelMatch>((ev, info) =>
            {
                if (_pluginState.MapChangeScheduled)
                {
                    var delay = _config.EndOfMapVote.DelayToChangeInTheEnd - 3.0F; //subtracting the delay that is going to be applied by ChangeNextMap function anyway
                    if (delay < 0)
                        delay = 0;

                    _plugin.AddTimer(delay, () =>
                    {
                        ChangeNextMap(true);
                    });
                }
                return HookResult.Continue;
            });
        }
    }
}
