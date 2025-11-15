using H2MLauncher.Core.Services;

namespace H2MLauncher.Core.Game
{
    public sealed class PropertiesPlayerNameProvider : IPlayerNameProvider
    {
        private readonly GameDirectoryService _gameDirectoryService;

        public string PlayerName { get; private set; }

        public event Action<string, string>? PlayerNameChanged;

        public PropertiesPlayerNameProvider(GameDirectoryService gameDirectoryService)
        {
            _gameDirectoryService = gameDirectoryService;
            _gameDirectoryService.PlayerPropertiesChanged += GameDirectoryService_PlayerPropertiesChanged;

            PlayerName = GetPlayerName(gameDirectoryService.CurrentPlayersProperties);
        }

        private void GameDirectoryService_PlayerPropertiesChanged(string path, GameDirectoryService.Properties? newContent)
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

        private static string GetPlayerName(GameDirectoryService.Properties? properties)
        {
            return properties?.PlayerName ?? "Unknown Soldier";
        }
    }
}
