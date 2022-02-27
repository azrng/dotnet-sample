using Microsoft.AspNetCore.Mvc;
using NetCoreFilterSample.Filter;

namespace NetCoreFilterSample.Controllers;

/// <summary>
/// 结果过滤器
/// </summary>
[Route("api/[controller]/[action]")]
[ApiController]
public class ResultController : ControllerBase
{
    private readonly ILogger<ResultController> _logger;

    public ResultController(ILogger<ResultController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 获取天气（同步验证IResultFilter）
    /// </summary>
    /// <returns></returns>
    //[Result_01Filter]//无参数过滤器
    [ServiceFilter(typeof(Result_01Filter))]
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
    /// 获取天气（异步验证IAsyncResultFilter）
    /// </summary>
    /// <returns></returns>
    //[Result_02Filter]//无参数过滤器
    [ServiceFilter(typeof(Result_02Filter))]
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
    /// 获取天气（异步验证ResultFilterAttribute）
    /// </summary>
    /// <returns></returns>
    //[Result_03Filter]//无参数过滤器
    [ServiceFilter(typeof(Result_03Filter))]
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

        return (new WeatherForecast()).GetList();
    }

}
