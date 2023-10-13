namespace ClientServices.Events;

public class ProgressBarrEventArgs: EventArgs
{
    public int Progess { get; set; } = 0;
}