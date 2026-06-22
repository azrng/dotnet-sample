using Azrng.DuckDB.Quack.Internal;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// QuackRetryPolicy 重试策略的单元测试
/// </summary>
public class QuackRetryPolicyTests
{
    /// <summary>
    /// 验证首次执行成功时不进行重试
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SucceedsOnFirstAttempt_DoesNotRetry()
    {
        var policy = new QuackRetryPolicy(maxRetries: 3, initialDelay: TimeSpan.FromMilliseconds(1));
        var calls = 0;

        var result = await policy.ExecuteAsync(() =>
        {
            calls++;
            return Task.FromResult(42);
        });

        Assert.Equal(42, result);
        Assert.Equal(1, calls);
    }

    /// <summary>
    /// 验证遇到瞬态故障时自动重试直到成功
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_RetriesOnTransientFailure()
    {
        var policy = new QuackRetryPolicy(maxRetries: 3, initialDelay: TimeSpan.FromMilliseconds(1));
        var calls = 0;

        var result = await policy.ExecuteAsync<int>(() =>
        {
            calls++;
            return calls < 3
                ? Task.FromException<int>(new HttpRequestException("transient"))
                : Task.FromResult(7);
        });

        Assert.Equal(7, result);
        Assert.Equal(3, calls);
    }

    /// <summary>
    /// 验证遇到不可重试的异常时不进行重试，直接抛出
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DoesNotRetryOnNonRetryableException()
    {
        var policy = new QuackRetryPolicy(maxRetries: 3, initialDelay: TimeSpan.FromMilliseconds(1));
        var calls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await policy.ExecuteAsync<int>(() =>
            {
                calls++;
                return Task.FromException<int>(new InvalidOperationException("non-retryable"));
            });
        });

        Assert.Equal(1, calls);
    }

    /// <summary>
    /// 验证根据 HTTP 状态码决定是否重试（5xx/429 可重试，4xx 不重试）
    /// </summary>
    [Theory]
    [InlineData(500, true)]
    [InlineData(502, true)]
    [InlineData(503, true)]
    [InlineData(429, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(404, false)]
    public async Task ExecuteAsync_RetriesBasedOnStatusCode(int statusCode, bool shouldRetry)
    {
        var policy = new QuackRetryPolicy(maxRetries: 1, initialDelay: TimeSpan.FromMilliseconds(1));
        var calls = 0;

        try
        {
            await policy.ExecuteAsync<int>(() =>
            {
                calls++;
                return Task.FromException<int>(new QuackProtocolException($"http {statusCode}", statusCode));
            });
        }
        catch (QuackProtocolException)
        {
            // expected for non-retryable
        }

        Assert.Equal(shouldRetry ? 2 : 1, calls);
    }

    /// <summary>
    /// 验证达到最大重试次数后抛出最后一次异常
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ThrowsLastExceptionAfterMaxRetries()
    {
        var policy = new QuackRetryPolicy(maxRetries: 2, initialDelay: TimeSpan.FromMilliseconds(1));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            policy.ExecuteAsync<int>(() => Task.FromException<int>(new HttpRequestException("always fail"))));

        Assert.Equal("always fail", ex.Message);
    }
}
