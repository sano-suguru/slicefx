namespace SliceFx.Wasi.KeyValue.Tests;

public sealed class InMemoryKeyValueStoreTests
{
    [Fact]
    public async Task GetBytesAsync_MissingKey_ReturnsNull()
    {
        var store = new InMemoryKeyValueStore();
        var result = await store.GetBytesAsync("missing", TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAndGetBytes_RoundTrips()
    {
        var store = new InMemoryKeyValueStore();
        var data = new byte[] { 1, 2, 3 };
        await store.SetBytesAsync("k", data, TestContext.Current.CancellationToken);
        var result = await store.GetBytesAsync("k", TestContext.Current.CancellationToken);
        Assert.Equal(data, result);
    }

    [Fact]
    public async Task SetBytesAsync_OverwritesExisting()
    {
        var store = new InMemoryKeyValueStore();
        var ct = TestContext.Current.CancellationToken;
        await store.SetBytesAsync("k", new byte[] { 1 }, ct);
        await store.SetBytesAsync("k", new byte[] { 2, 3 }, ct);
        var result = await store.GetBytesAsync("k", ct);
        Assert.Equal(new byte[] { 2, 3 }, result);
    }

    [Fact]
    public async Task DeleteAsync_RemovesKey()
    {
        var store = new InMemoryKeyValueStore();
        var ct = TestContext.Current.CancellationToken;
        await store.SetBytesAsync("k", new byte[] { 1 }, ct);
        await store.DeleteAsync("k", ct);
        Assert.Null(await store.GetBytesAsync("k", ct));
    }

    [Fact]
    public async Task DeleteAsync_MissingKey_IsNoOp()
    {
        var store = new InMemoryKeyValueStore();
        var ct = TestContext.Current.CancellationToken;
        await store.DeleteAsync("missing", ct);
        Assert.False(await store.ExistsAsync("missing", ct));
    }

    [Fact]
    public async Task ExistsAsync_AfterSet_ReturnsTrue()
    {
        var store = new InMemoryKeyValueStore();
        var ct = TestContext.Current.CancellationToken;
        await store.SetBytesAsync("k", new byte[] { 0 }, ct);
        Assert.True(await store.ExistsAsync("k", ct));
    }

    [Fact]
    public async Task ExistsAsync_MissingKey_ReturnsFalse()
    {
        var store = new InMemoryKeyValueStore();
        Assert.False(await store.ExistsAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListKeysAsync_ReturnsAllKeys()
    {
        var store = new InMemoryKeyValueStore();
        var ct = TestContext.Current.CancellationToken;
        await store.SetBytesAsync("a", new byte[] { 1 }, ct);
        await store.SetBytesAsync("b", new byte[] { 2 }, ct);
        var keys = await store.ListKeysAsync(ct);
        Assert.Contains("a", keys);
        Assert.Contains("b", keys);
        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public async Task Clear_RemovesAllEntries()
    {
        var store = new InMemoryKeyValueStore();
        await store.SetBytesAsync("x", "nine"u8.ToArray(), TestContext.Current.CancellationToken);
        store.Clear();
        Assert.Empty(await store.ListKeysAsync(TestContext.Current.CancellationToken));
    }
}
