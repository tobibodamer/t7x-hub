using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using H2MLauncher.Core.Game;
using H2MLauncher.Core.Settings;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace H2MLauncher.Core.Services
{
    public sealed partial class GameDirectoryService : IGameConfigProvider, IDisposable
    {
        /// <summary>
        /// Type of the config.ini file in the T7X players directory
        /// </summary>
        public record ConfigIniContent(int MaxFps);

        /// <summary>
        /// Type of the properties.json file in the T7X players directory
        /// </summary>
        public record Properties(string? PlayerName);

        private const string T7X_PLAYERS_DIR = "t7x\\players";
        private const string PROPERTIES_FILENAME = "properties.json";
        private const string CONFIG_INI_FILENAME = "config.ini";

        private FileSystemWatcher? _fileSystemWatcher;
        private readonly IOptionsMonitor<H2MLauncherSettings> _optionsMonitor;
        private readonly ILogger<GameDirectoryService> _logger;
        private readonly IDisposable? _optionsMonitorChangeRegistration;

        public string? CurrentDir { get; private set; }
        public bool IsWatching => _fileSystemWatcher != null;
        public ConfigIniContent? CurrentConfig { get; private set; }
        public Properties? CurrentPlayersProperties { get; private set; }


        public event ConfigChangedEventHandler? ConfigMpChanged;

        public event PropertiesChangedEventHandler? PlayerPropertiesChanged;


        public event Action<string, string>? FastFileChanged;

        public delegate void ConfigChangedEventHandler(string filePath, ConfigIniContent? config);

        public delegate void PropertiesChangedEventHandler(string filePath, Properties? content);


        public GameDirectoryService(IOptionsMonitor<H2MLauncherSettings> optionsMonitor, ILogger<GameDirectoryService> logger)
        {
            _logger = logger;
            _optionsMonitor = optionsMonitor;
            _optionsMonitorChangeRegistration = optionsMonitor.OnChange((settings, _) =>
            {
                if (settings.WatchGameDirectory)
                {
                    WatchGameDirectory(settings);
                }
                else if (IsWatching)
                {
                    UninitializeWatcher();
                }
            });

            if (_optionsMonitor.CurrentValue.WatchGameDirectory)
            {
                WatchGameDirectory(optionsMonitor.CurrentValue);
            }
        }

        private void WatchGameDirectory(H2MLauncherSettings settings)
        {
            CurrentDir = GetGameDir(settings);
            if (CurrentDir is null)
            {
                UninitializeWatcher();
                return;
            }

            if (_fileSystemWatcher is null)
            {
                InitializeWatcher(CurrentDir);
            }
            else
            {
                _fileSystemWatcher.Path = CurrentDir;
            }

            OnConfigFileChanged(Path.Combine(CurrentDir, T7X_PLAYERS_DIR, CONFIG_INI_FILENAME));
            OnUserPropertiesFileChanged(Path.Combine(CurrentDir, T7X_PLAYERS_DIR, PROPERTIES_FILENAME));
        }

        private static string? GetGameDir(H2MLauncherSettings settings)
        {
            string? gameDir = Path.GetDirectoryName(settings.GameLocation);
            if (gameDir is null)
            {
                return null;
            }

            if (!Directory.Exists(gameDir))
            {
                return null;
            }

            return gameDir;
        }

        [MemberNotNull(nameof(_fileSystemWatcher))]
        private void InitializeWatcher(string path)
        {
            _logger.LogDebug("Start watching game directory {gameDir}", path);

            _fileSystemWatcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true,
                IncludeSubdirectories = true
            };
            _fileSystemWatcher.Filters.Add("*.ff");
            _fileSystemWatcher.Filters.Add(PROPERTIES_FILENAME);

            _fileSystemWatcher.Changed += FileSystemWatcherEvent;
            _fileSystemWatcher.Created += FileSystemWatcherEvent;
            _fileSystemWatcher.Deleted += FileSystemWatcherEvent;
            _fileSystemWatcher.Error += FileSystemWatcher_Error;
        }

        private void UninitializeWatcher()
        {
            if (_fileSystemWatcher is not null)
            {
                _fileSystemWatcher.Dispose();
                _fileSystemWatcher.Changed -= FileSystemWatcherEvent;
            }
        }

        private void FileSystemWatcher_Error(object sender, ErrorEventArgs e)
        {
            _logger.LogError(e.GetException(), "Error of game directory file system watcher:");

            // reinitialize
            WatchGameDirectory(_optionsMonitor.CurrentValue);
        }

        private void FileSystemWatcherEvent(object sender, FileSystemEventArgs e)
        {
            try
            {
                _logger.LogTrace("Game directory file changed: {path}, {changeType}", e.FullPath, e.ChangeType);

                string currentDirAbsolutePath = Path.GetFullPath(CurrentDir ?? "");

                if (e.FullPath.Equals(Path.Combine(currentDirAbsolutePath, T7X_PLAYERS_DIR, CONFIG_INI_FILENAME)))
                {
                    OnConfigFileChanged(e.FullPath);
                }
                else if (e.FullPath.Equals(Path.Combine(currentDirAbsolutePath, T7X_PLAYERS_DIR, PROPERTIES_FILENAME)))
                {
                    OnUserPropertiesFileChanged(e.FullPath);
                }
                else
                {
                    string relativePath = e.FullPath[currentDirAbsolutePath.Length..];
                    if (relativePath.EndsWith(".ff"))
                    {
                        OnFastFileChanged(Path.GetFileName(relativePath), e.FullPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while handling game directory watcher event");
            }
        }

        private void OnConfigFileChanged(string path)
        {
            if (!File.Exists(path))
            {
                CurrentConfig = null;
                ConfigMpChanged?.Invoke(path, null);
                return;
            }

            _logger.LogTrace("Config file change detected, parsing...");

            int com_maxFps = -1;

            // open file with read write share
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, Encoding.Default))
            {
                string? line;
                while ((line = sr.ReadLine()) is not null)
                {

                    Match maxFpsMatch = MaxFpsEntryRegex().Match(line);
                    if (maxFpsMatch.Success)
                    {
                        com_maxFps = int.Parse(maxFpsMatch.Groups[1].Value);
                    }
                }
            }
          

            _logger.LogTrace("Parsed '{configFile}': {config}", CONFIG_INI_FILENAME, CurrentConfig);

            ConfigIniContent newContent = new(com_maxFps);
            if (!newContent.Equals(CurrentConfig))
            {
                _logger.LogInformation("New '{configFile}' loaded: {config}", CONFIG_INI_FILENAME, newContent);
                CurrentConfig = newContent;
                ConfigMpChanged?.Invoke(path, CurrentConfig);
            }
        }

        private void OnUserPropertiesFileChanged(string path)
        {
            if (!File.Exists(path))
            {
                CurrentPlayersProperties = null;
                PlayerPropertiesChanged?.Invoke(path, null);
                return;
            }

            _logger.LogTrace("Properties file change detected, parsing...");

            Properties? content;

            // open file with read write share
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                content = JsonSerializer.Deserialize<Properties>(fs);
            }

            _logger.LogTrace("Parsed '{propertiesFiles}': {content}", PROPERTIES_FILENAME, content);
                        
            if (!EqualityComparer<Properties>.Default.Equals(content, CurrentPlayersProperties))
            {
                _logger.LogInformation("New '{propertiesFiles}' loaded: {content}", PROPERTIES_FILENAME, content);
                CurrentPlayersProperties = content;
                PlayerPropertiesChanged?.Invoke(path, CurrentPlayersProperties);
            }
        }

        private void OnFastFileChanged(string fileName, string fullPath)
        {
            _logger.LogInformation("Detected fast file change: {fastfileName}", fileName);

            FastFileChanged?.Invoke(fileName, fullPath);
        }

        public bool? HasOgMap(string mapName)
        {
            string? gameDir = GetGameDir(_optionsMonitor.CurrentValue);
            if (string.IsNullOrEmpty(gameDir))
            {
                return null;
            }

            // look for fastfile
            string ff = $"{mapName}.ff";

            // check in game directory
            string mapFile = Path.Combine(gameDir, ff);
            if (File.Exists(mapFile))
            {
                return true;
            }

            // check in 'zone' subfolder
            mapFile = Path.Combine(gameDir, "zone", ff);
            return File.Exists(mapFile);
        }

        public void Dispose()
        {
            UninitializeWatcher();
            _optionsMonitorChangeRegistration?.Dispose();
        }

        [GeneratedRegex("MaxFPS(?: *)=(?: *)\"([0-9]+)\"")]
        private static partial Regex MaxFpsEntryRegex();
    }
}
