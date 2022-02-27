using Microsoft.AspNetCore.Mvc;
using NetCoreFilterSample.Filter;

namespace NetCoreFilterSample.Controllers;

[Route("api/[controller]/[action]")]
[ApiController]
public class ActionSampleController : ControllerBase
{
    private readonly ILogger<ActionSampleController> _logger;

    public ActionSampleController(ILogger<ActionSampleController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 获取天气（同步验证IActionFilter）
    /// </summary>
    /// <returns></returns>
    //[Action_01Filter]//无参数过滤器
    [ServiceFilter(typeof(Action_01Filter))]
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
    /// 获取天气（异步验证IAsyncActionFilter）
    /// </summary>
    /// <returns></returns>
    //[Action_02Filter]//无参数过滤器
    [ServiceFilter(typeof(Action_02Filter))]
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
    /// 获取天气（异步验证ActionFilterAttribute）
    /// </summary>
    /// <returns></returns>
    //[Action_03Filter]//无参数过滤器
    [ServiceFilter(typeof(Action_03Filter))]
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