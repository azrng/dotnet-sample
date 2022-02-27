using Microsoft.AspNetCore.Mvc;
using NetCoreFilterSample.CustomFilter;
using NetCoreFilterSample.Model;
using Newtonsoft.Json;

namespace NetCoreFilterSample.Controllers;

/// <summary>
/// 天气控制器
/// </summary>
[ApiController]
[Route("api/[controller]/[action]")]
//[TypeFilter(typeof(CustomExceptionFilter))]
//[TypeFilter(typeof(CustomResultPackFilter))]
public class WeatherForecastController : ControllerBase
{
    private readonly ILogger<WeatherForecastController> _logger;

    public WeatherForecastController(ILogger<WeatherForecastController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 获取天气
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    public IEnumerable<WeatherForecast> Get(bool isError)
    {
        if (isError)
        {
            Convert.ToInt32("aa");
        }
        return (new WeatherForecast()).GetList();
    }

    /// <summary>
    /// 获取患者信息返回类
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    [RequestLimitFilter("getinfo", 5, 10)]
    public string GetPatientName(int patientId)
    {
        return "张三" + patientId;
    }

    /// <summary>
    /// 患者吃饭请求类
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    [HttpPost]
    public string AddPatientEat(AddPatientEatRequest request)
    {
        return JsonConvert.SerializeObject(request);
    }

    /// <summary>
    /// 获取患者信息列表
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    public IEnumerable<GetPatientResult> GetPatientList()
    {
        return new GetPatientResult().GetList();
    }

    /// <summary>
    /// 获取患者信息列表
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    public GetPatientResult GetPatient()
    {
        return new GetPatientResult().GetDetails();
    }
}