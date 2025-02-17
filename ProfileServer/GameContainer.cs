using System.Diagnostics;

namespace ProfileServer;

public class GameContainer
{
    public GameContainer(string workingDirectory, string executable)
    {
        //start game process
        gameProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = workingDirectory + executable,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false
            }
        };
    }

    private readonly Process gameProcess;
    
    public void Start()
    {
        gameProcess.Start();
    }
}