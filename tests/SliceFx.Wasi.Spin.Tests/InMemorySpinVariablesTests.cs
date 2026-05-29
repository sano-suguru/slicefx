namespace SliceFx.Wasi.Spin.Tests;

public sealed class InMemorySpinVariablesTests
{
    [Fact]
    public async Task GetAsync_DefinedVariable_ReturnsValue()
    {
        var vars = new InMemorySpinVariables();
        vars.Set("my_key", "hello");

        var result = await vars.GetAsync("my_key", TestContext.Current.CancellationToken);

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task GetAsync_UndefinedVariable_ReturnsNull()
    {
        var vars = new InMemorySpinVariables();

        var result = await vars.GetAsync("missing", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_SeededConstructor_ReturnsValue()
    {
        var vars = new InMemorySpinVariables(
            new Dictionary<string, string> { ["token"] = "secret" });

        var result = await vars.GetAsync("token", TestContext.Current.CancellationToken);

        Assert.Equal("secret", result);
    }

    [Fact]
    public async Task Set_OverwritesExistingValue()
    {
        var vars = new InMemorySpinVariables();
        vars.Set("k", "v1");
        vars.Set("k", "v2");

        var result = await vars.GetAsync("k", TestContext.Current.CancellationToken);

        Assert.Equal("v2", result);
    }

    [Fact]
    public async Task Clear_RemovesAllVariables()
    {
        var vars = new InMemorySpinVariables();
        vars.Set("k", "v");
        vars.Clear();

        var result = await vars.GetAsync("k", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public void SeededConstructor_NullArgument_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new InMemorySpinVariables(null!));
}
