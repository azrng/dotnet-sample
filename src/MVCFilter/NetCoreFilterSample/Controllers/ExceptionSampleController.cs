using Microsoft.AspNetCore.Mvc;
using NetCoreFilterSample.Filter;

namespace NetCoreFilterSample.Controllers;

/// <summary>
/// 异常过滤器
/// </summary>
[Route("api/[controller]/[action]")]
[ApiController]
public class ExceptionSampleController : ControllerBase
{
    private readonly ILogger<ExceptionSampleController> _logger;

    public ExceptionSampleController(ILogger<ExceptionSampleController> logger)
    {
        _logger = logger;
        _logger.LogInformation($"{typeof(ExceptionSampleController)} 被构造..... ");
    }

    /// <summary>
    /// 获取天气(同步方法，IExceptionFilter)
    /// </summary>
    /// <returns></returns>
    //[TypeFilter(typeof(Exception_01Filter))]//默认情况
    //[TypeFilter(typeof(Exception_01Filter), Arguments = new object[] { "aa", "bb" })]//传递参数的情况
    [TypeFilter(typeof(Exception_01Filter))]
    [HttpGet]
    public IEnumerable<WeatherForecast> Get01()
    {
        throw new ArgumentNullException("aaa");

        return (new WeatherForecast()).GetList();
    }

    /// <summary>
    /// 获取天气(异步方法，IAsyncExceptionFilter)
    /// </summary>
    /// <returns></returns>
    //[TypeFilter(typeof(Exception_02Filter))]//默认情况
    [TypeFilter(typeof(Exception_02Filter))]
    [HttpGet]
    public IEnumerable<WeatherForecast> Get02()
    {
        throw new ArgumentNullException("aaa");

        return (new WeatherForecast()).GetList();
    }

    /// <summary>
    /// 获取天气(异步方法，ExceptionFilterAttribute)
    /// </summary>
    /// <returns></returns>
    //[TypeFilter(typeof(Exception_03Filter))]//默认情况
    [TypeFilter(typeof(Exception_03Filter))]
    [HttpGet]
    public IEnumerable<WeatherForecast> Get03()
    {
        throw new ArgumentNullException("aaa");

        return (new WeatherForecast()).GetList();
    }
}