### ProfileServer

Automated system to download, update and launch, and profile an Unreal based steam game.

The Profiler tool handles logging in to steam, authenticating, (supporting multiple 2FA methods), updating and configuration and starting the game.
2FA is handled through Discord modals, and status is displayed during any given task:

![image](https://github.com/user-attachments/assets/0f3aa525-6b8f-43ed-9cd7-c20f9494ca54)

![image](https://github.com/user-attachments/assets/74f9d6cc-2b08-4123-969b-6affaf72ed13)

![image](https://github.com/user-attachments/assets/78396612-fb46-4301-b434-2987254fc360)

### Usage

Create a `.env` file in the working directory containing values for the following environment variables:

```
STEAMUSERNAME=        steam username
STEAMPASSWORD=        steam password
STEAMGAMEID=          steam game id
STEAMBETABRANCH=      steam beta branch (when using release branches)
GAMEBINARY=           relative to the game install directory, so probably executablename.exe
GAMEARGS=             arguments to launch the game with (add your -startBenchmark or whatever thing here; you probably also want -execcmds="stat namedevents")
GAMEFOLDER=           Most likely the name of your Unreal Project. This is the root folder under which "content, plugins, saved" etc is stored
DISCORD_TOKEN=        Discord token
DISCORD_GUILD=        Discord guild id
DISCORD_CHANNEL=      Discord channel id to interact in
UNREALINSIGHTSPATH=    (probably something like "C:\Program Files\Epic Games\UE_5.4\Engine\Binaries\Win64\UnrealInsights.exe" - without quotes)
CSVTOSVGPATH=          (probably something like "C:\Program Files\Epic Games\UE_5.4\Engine\Binaries\DotNET\CSVTools\CSVToSVG.exe" - without quotes) 
ENABLE_GIT=           Set to TRUE to upload unreal insights trace files to a git repo.
```
### Git
Notes about GIT repository uploads:
Trace files will be copied to the "output" directory inside the working directory, and this directory will be treated as a git repo (git commands will be ran relative to this directory, so if you want to use it, make sure to initialize a git repository inside this directory)
Git commands that will be used:
```sh
git add {file}
git commit -m "Trace data"
git push
```

### Game Requirements
Your game should have some way to use launch arguments to make it run a predefined benchmark, such as a camera flythrough of a level with scripted actions. It should also run two console commands (both at the start and end of the scripted session)

At start of session: `StartFPSChart`
At the end of the session: `StopFPSChart`

The results of those will be used to analyze the game performance and draw an FPS chart over time, as well as gathering other data which will be stored in the database.

### Database
Report data will be stored in an SQLite database stored at `(workingdirectory)/performance.db3`.
Each new profiling session will automatically show performance deltas compared to the previous session and link back to it. A full history report can be generated with `/log-history`.

![image](https://github.com/user-attachments/assets/8ecd8b8d-4f14-4bed-a612-8b2c7cad8c32)

### Launching the server
Start the server by navigating to the cloned directory and running `dotnet run` inside of the `ProfileServer` directory.

Building and launching the executable with valid parameters will log into Discord and enable you to download the game. Status updates will be reported on the Discord channel. The game can also be started from here.
Launch parameters for the game should be set so that your Unreal game automatically plays back a predefined benchmark, such as a camera flythrough of a level with scripted actions.

Commands:

`/update-game` - update the game configured in environment

`/run-game` - launch the game with specified launch parameters

`/stop-game` - force close any running game processes launched by the profile server

`/log-history` - create a graph showing profiling data over time (containing data stored in the database)
