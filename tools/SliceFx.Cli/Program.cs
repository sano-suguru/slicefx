using System.CommandLine;
using SliceFx.Cli.Commands;

var newCmd = new Command("new", "Scaffold a new SliceFx file.")
{
    NewFeatureCommand.Build(),
    NewFilterCommand.Build(),
    NewWasiCloudflareCommand.Build()
};

var clientCmd = new Command("client", "Generate typed clients from SliceFx feature routes.")
{
    GenerateCSharpClientCommand.Build(),
    GenerateTypeScriptClientCommand.Build()
};

var manifestCmd = new Command("manifest", "Generate deployment manifests from SliceFx feature routes.")
{
    ManifestAwsLambdaCommand.Build()
};

var packageCmd = new Command("package", "Publish/package SliceFx deployment artifacts (AWS Lambda function-per-feature today).")
{
    PackageAwsLambdaCommand.Build()
};

var root = new RootCommand("SliceFx CLI — vertical slice scaffolding.")
{
    newCmd,
    clientCmd,
    manifestCmd,
    packageCmd,
    GenerateOpenApiCommand.Build(),
    ListRoutesCommand.Build()
};

return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
