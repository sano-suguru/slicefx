using System.CommandLine;
using Slice.Cli.Commands;

var newCmd = new Command("new", "Scaffold a new Slice file.")
{
    NewFeatureCommand.Build(),
    NewFilterCommand.Build(),
    NewWasiCloudflareCommand.Build()
};

var clientCmd = new Command("client", "Generate typed clients from Slice feature routes.")
{
    GenerateCSharpClientCommand.Build()
};

var manifestCmd = new Command("manifest", "Generate deployment manifests from Slice feature routes.")
{
    ManifestAwsLambdaCommand.Build()
};

var packageCmd = new Command("package", "Publish/package Slice deployment artifacts.")
{
    PackageAwsLambdaCommand.Build()
};

var root = new RootCommand("Slice CLI — vertical slice scaffolding.")
{
    newCmd,
    clientCmd,
    manifestCmd,
    packageCmd,
    ListRoutesCommand.Build()
};

return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
