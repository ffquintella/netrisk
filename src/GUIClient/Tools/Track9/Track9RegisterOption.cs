namespace GUIClient.Tools.Track9;

public sealed class Track9RegisterOption<T>
{
    public T Value { get; }
    public string Name { get; }

    public Track9RegisterOption(T value, string name)
    {
        Value = value;
        Name = name;
    }

    public override string ToString() => Name;
}
