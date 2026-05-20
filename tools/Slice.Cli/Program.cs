using System.CommandLine;
using Slice.Cli.Commands;

var newCmd = new Command("new", "Scaffold a new Slice file.")
{
    NewFeatureCommand.Build(),
    NewFilterCommand.Build()
};

var clientCmd = new Command("client", "Generate typed clients from Slice feature routes.")
{
    GenerateCSharpClientCommand.Build()
};

var root = new RootCommand("Slice CLI — vertical slice scaffolding.")
{
    newCmd,
    clientCmd,
    ListRoutesCommand.Build()
};

return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
