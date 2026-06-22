using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Azrng.DuckDB.Quack.Internal;

/// <summary>
/// Quack 协议操作的重试策略。
/// 支持指数退避。
/// </summary>
internal sealed class QuackRetryPolicy
{
    private readonly ILogger _logger;
    private readonly int _maxRetries;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maxDelay;
    private readonly double _backoffMultiplier;

    /// <summary>
    /// 初始化重试策略实例。
    /// </summary>
    /// <param name="logger">日志记录器，为 null 时使用空日志记录器。</param>
    /// <param name="maxRetries">最大重试次数，默认为 3。</param>
    /// <param name="initialDelay">初始重试延迟时间，默认为 100 毫秒。</param>
    /// <param name="maxDelay">最大重试延迟时间，默认为 30 秒。</param>
    /// <param name="backoffMultiplier">退避倍数，默认为 2.0。</param>
    public QuackRetryPolicy(
        ILogger? logger = null,
        int maxRetries = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double backoffMultiplier = 2.0)
    {
        _logger = logger ?? NullLogger.Instance;
        _maxRetries = maxRetries;
        _initialDelay = initialDelay ?? TimeSpan.FromMilliseconds(100);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
        _backoffMultiplier = backoffMultiplier;
    }

    /// <summary>
    /// 使用重试逻辑执行操作。
    /// </summary>
    /// <typeparam name="T">操作返回值的类型。</typeparam>
    /// <param name="operation">要执行的操作。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    /// <returns>操作的结果。</returns>
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        var delay = _initialDelay;
        Exception? lastException = null;

        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < _maxRetries && IsRetryable(ex))
            {
                lastException = ex;
                _logger.LogWarning(ex, "Operation failed (attempt {Attempt}/{MaxRetries}), retrying in {Delay}ms",
                    attempt + 1, _maxRetries, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                delay = TimeSpan.FromTicks(Math.Min(
                    (long)(delay.Ticks * _backoffMultiplier),
                    _maxDelay.Ticks));
            }
        }

        throw lastException ?? new QuackProtocolException("Max retries exceeded");
    }

    /// <summary>
    /// 使用重试逻辑执行操作（无返回值）。
    /// </summary>
    /// <param name="operation">要执行的操作。</param>
    /// <param name="cancellationToken">用于取消操作的令牌。</param>
    public async Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async () =>
        {
            await operation().ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsRetryable(Exception ex)
    {
        if (ex is HttpRequestException || ex is TaskCanceledException || ex is TimeoutException)
            return true;

        if (ex is QuackProtocolException qex && qex.StatusCode is { } code)
        {
            // Retry server errors and rate-limiting; client errors (4xx other than 429) are not retryable.
            return code >= 500 || code == 429;
        }

        return false;
    }
}
