using System.Text.Json.Serialization;

namespace SliceFx.Wasi.KeyValue.Tests;

public sealed class KeyValueStoreExtensionsTests
{
    [Fact]
    public async Task GetStringAsync_MissingKey_ReturnsNull()
    {
        var store = new InMemoryKeyValueStore();
        Assert.Null(await store.GetStringAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetAndGetString_RoundTrips()
    {
        var store = new InMemoryKeyValueStore();
        var ct = TestContext.Current.CancellationToken;
        await store.SetStringAsync("title", "hello world", ct);
        Assert.Equal("hello world", await store.GetStringAsync("title", ct));
    }

    [Fact]
    public async Task SetAndGetString_UnicodeRoundTrips()
    {
        var store = new InMemoryKeyValueStore();
        var ct = TestContext.Current.CancellationToken;
        await store.SetStringAsync("k", "日本語テスト", ct);
        Assert.Equal("日本語テスト", await store.GetStringAsync("k", ct));
    }

    [Fact]
    public async Task GetJsonAsync_MissingKey_ReturnsDefault()
    {
        var store = new InMemoryKeyValueStore();
        var result = await store.GetJsonAsync(
            "k", TestJsonContext.Default.TestRecord, TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task SetAndGetJson_RoundTrips()
    {
        var store = new InMemoryKeyValueStore();
        var ct = TestContext.Current.CancellationToken;
        var record = new TestRecord("inbox-item-1", 42);
        await store.SetJsonAsync("item", record, TestJsonContext.Default.TestRecord, ct);
        var got = await store.GetJsonAsync("item", TestJsonContext.Default.TestRecord, ct);
        Assert.Equal(record, got);
    }
}

internal sealed record TestRecord(string Id, int Count);

[JsonSerializable(typeof(TestRecord))]
internal sealed partial class TestJsonContext : JsonSerializerContext;
