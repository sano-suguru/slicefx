using System.CommandLine;
using Slice.Cli.Commands;

var newCmd = new Command("new", "Scaffold a new Slice file.")
{
    NewFeatureCommand.Build(),
    NewFilterCommand.Build()
};

var root = new RootCommand("Slice CLI — vertical slice scaffolding.")
{
    newCmd
};

return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
