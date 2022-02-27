using Microsoft.AspNetCore.Mvc;
using NetCoreFilterSample.Filter;

namespace NetCoreFilterSample.Controllers;

/// <summary>
/// 授权过滤器
/// </summary>
[Route("api/[controller]/[action]")]
[ApiController]
public class AuthorizationSampleController : ControllerBase
{
    private readonly ILogger<AuthorizationSampleController> _logger;

    public AuthorizationSampleController(ILogger<AuthorizationSampleController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 获取天气（同步验证IAuthorizationFilter）
    /// </summary>
    /// <returns></returns>
    //[Authorization_01Filter]//无参数过滤器
    [ServiceFilter(typeof(Authorization_01Filter))]
    [HttpGet]
    public IEnumerable<WeatherForecast> Get01()
    {
        const string str = @"
    业务逻辑开始处理
    处...
    理...
    中...
    业务逻辑结束处理";
        _logger.LogInformation(str);
        return (new WeatherForecast()).GetList();
    }

    /// <summary>
    /// 获取天气（异步验证IAsyncAuthorizationFilter）
    /// </summary>
    /// <returns></returns>
    //[Authorization_01Filter]//无参数过滤器
    [ServiceFilter(typeof(Authorization_01AsyncFilter))]
    [HttpGet]
    public IEnumerable<WeatherForecast> Get02()
    {
        const string str = @"
        业务逻辑开始处理
        处...
        理...
        中...
        业务逻辑结束处理";
        _logger.LogInformation(str);
        return (new WeatherForecast()).GetList();
    }

    /// <summary>
    /// 获取天气（异步验证AuthorizeFilter）
    /// </summary>
    /// <returns></returns>
    //[Authorization_01Filter]//无参数过滤器
    [ServiceFilter(typeof(Authonization_2Filter))]
    [HttpGet]
    public IEnumerable<WeatherForecast> Get03()
    {
        const string str = @"
        业务逻辑开始处理
        处...
        理...
        中...
        业务逻辑结束处理";
        _logger.LogInformation(str);
        // No authenticationScheme was specified, and there was no DefaultChallengeScheme found.
        return (new WeatherForecast()).GetList();
    }
}