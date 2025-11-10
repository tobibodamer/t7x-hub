using System.Text.Json;

using H2MLauncher.Core.Services;

namespace H2MLauncher.Core.Game
{
    public sealed class FilePlayerNameProvider : IPlayerNameProvider
    {
        private readonly GameDirectoryService _gameDirectoryService;

        public string PlayerName { get; private set; }

        public event Action<string, string>? PlayerNameChanged;

        public FilePlayerNameProvider(GameDirectoryService gameDirectoryService)
        {
            _gameDirectoryService = gameDirectoryService;
            _gameDirectoryService.UserPropertiesChanged += GameDirectoryService_UserPropertiesChanged;

            PlayerName = GetPlayerName(gameDirectoryService.CurrentUserProperties);
        }

        private void GameDirectoryService_UserPropertiesChanged(string path, JsonDocument? newContent)
        {
            if (newContent is null)
            {
                return;
            }

            string oldPlayerName = PlayerName;
            PlayerName = GetPlayerName(newContent);

            if (oldPlayerName == PlayerName)
            {
                return;
            }

            PlayerNameChanged?.Invoke(oldPlayerName, PlayerName);
        }

        private static string GetPlayerName(JsonDocument? properties)
        {
            return properties?.RootElement.GetProperty("playerName").GetString() ?? "Unknown Soldier";
        }
    }
}
