### ProfileServer

Automated system to download, update and launch, and profile an Unreal based steam game

### Usage

Create a `.env` file in the working directory containing values for the following environment variables:

```
STEAMUSERNAME=
STEAMPASSWORD=
STEAMGAMEID=
STEAMBETABRANCH=
GAMEBINARY=
DISCORD_TOKEN=
DISCORD_GUILD=
DISCORD_CHANNEL=
UNREALINSIGHTSPATH=  (probably "C:\Program Files\Epic Games\UE_5.4\Engine\Binaries\Win64\UnrealInsights.exe")
```

Building and launching the executable with valid parameters will log into Discord and enable you to download the game. Status updates will be reported on the Discord channel. The game can also be started from here.