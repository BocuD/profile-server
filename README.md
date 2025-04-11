### ProfileServer

Automated system to download, update and launch, and profile an Unreal based steam game.

The Profiler tool handles logging in to steam, authenticating, (supporting multiple 2FA methods), updating and configuration and starting the game.
2FA is handled through Discord modals, and status is displayed during any given task:
![image](https://github.com/user-attachments/assets/74f9d6cc-2b08-4123-969b-6affaf72ed13)
![image](https://github.com/user-attachments/assets/78396612-fb46-4301-b434-2987254fc360)

### Usage

Create a `.env` file in the working directory containing values for the following environment variables:

```
STEAMUSERNAME=
STEAMPASSWORD=
STEAMGAMEID=
STEAMBETABRANCH=
GAMEBINARY=
GAMEARGS=
DISCORD_TOKEN=
DISCORD_GUILD=
DISCORD_CHANNEL=
UNREALINSIGHTSPATH=  (probably "C:\Program Files\Epic Games\UE_5.4\Engine\Binaries\Win64\UnrealInsights.exe")
```

Start the server by navigating to the cloned directory and running `dotnet run` inside of the `ProfileServer` directory.

Building and launching the executable with valid parameters will log into Discord and enable you to download the game. Status updates will be reported on the Discord channel. The game can also be started from here.

Commands:

/update-game
/run-game

Environment variables are used to configure the game and all appropriate credentials.
