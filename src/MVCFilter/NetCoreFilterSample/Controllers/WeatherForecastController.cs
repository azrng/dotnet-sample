using Microsoft.AspNetCore.Mvc;
using NetCoreFilterSample.CustomFilter;
using NetCoreFilterSample.Model;
using Newtonsoft.Json;

namespace NetCoreFilterSample.Controllers;

/// <summary>
/// ����������
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
    /// ��ȡ����
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
    /// ��ȡ������Ϣ������
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    [RequestLimitFilter("getinfo", 5, 10)]
    public string GetPatientName(int patientId)
    {
        return "����" + patientId;
    }

    /// <summary>
    /// ���߳Է�������
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    [HttpPost]
    public string AddPatientEat(AddPatientEatRequest request)
    {
        return JsonConvert.SerializeObject(request);
    }

    /// <summary>
    /// ��ȡ������Ϣ�б�
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    public IEnumerable<GetPatientResult> GetPatientList()
    {
        return new GetPatientResult().GetList();
    }

    /// <summary>
    /// ��ȡ������Ϣ�б�
    /// </summary>
    /// <returns></returns>
    [HttpGet]
    public GetPatientResult GetPatient()
    {
        return new GetPatientResult().GetDetails();
    }
}