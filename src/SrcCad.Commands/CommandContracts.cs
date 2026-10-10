namespace SrcCad.Commands;

public sealed class CommandContext
{
    public Action<string>? SetStatus { get; init; }
    public Action? ModelChanged { get; init; }
}

public interface ICadCommand
{
    string Id { get; }
    string Title { get; }
    void Begin(CommandContext context);
    void End(bool commit);
}

public sealed class CadCommandManager
{
    public ICadCommand? Active { get; private set; }

    public void Start(ICadCommand command, CommandContext context)
    {
        Active?.End(false);
        Active = command;
        command.Begin(context);
    }

    public void Cancel()
    {
        Active?.End(false);
        Active = null;
    }

    public void Finish()
    {
        Active?.End(true);
        Active = null;
    }
}
