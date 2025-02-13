### ProfileServer

Automated system to download, update and launch a steam game

### Usage

Create a `.env` file in the working directory containing values for the following environment variables:

```
STEAMUSERNAME=
STEAMPASSWORD=
STEAMGAMEID=
STEAMBETABRANCH=
```

Building and launching the executable with valid parameters will log into steam and start downloading the game. You will probably need to authenticate steam guard as well, the server will prompt you for this in the console.
