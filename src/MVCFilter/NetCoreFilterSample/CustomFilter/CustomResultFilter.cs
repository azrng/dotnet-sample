using Common.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NetCoreFilterSample.CustomFilter;

#region 返回类处理(让返回结果外面包一层公共业务返回类)

/// <summary>
/// 方案一：返回类处理(让返回结果外面包一层公共业务返回类)
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public class CustomResultPackFilter : Attribute, IResultFilter
{
    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is EmptyResult)
        {
            context.Result = new OkObjectResult(new ResultModel
            {
                Code = "200",
                IsSuccess = true,
                Message = "成功"
            });
            return;
        }

        context.Result = new OkObjectResult(new ResultModel<object>
        {
            Code = "200",
            IsSuccess = true,
            Data = ((ObjectResult)context.Result).Value
        });
    }
}

/// <summary>
/// 方案二：返回类处理(让返回结果外面包一层公共业务返回类增加返回code和消息)
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public class CustomResultPack2Filter : ActionFilterAttribute
{
    public class ReturnDataFilterAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuted(ActionExecutedContext context)
        {
            if (context.Result is EmptyResult)
            {
                context.Result = new OkObjectResult(new ResultModel
                {
                    Code = "200",
                    IsSuccess = true,
                    Message = "成功"
                });
                return;
            }

            context.Result = new OkObjectResult(new ResultModel<object>
            {
                Code = "200",
                IsSuccess = true,
                Data = ((ObjectResult)context.Result).Value
            });
        }
    }
}

#endregion

#region 返回值处理：匿名化

/// <summary>
/// 返回结果匿名化
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public class CustomResultAnonymityFilter : Attribute, IResultFilter
{
    //目的：比如对名称进行匿名化处理

    public void OnResultExecuted(ResultExecutedContext context)
    {

    }

    public void OnResultExecuting(ResultExecutingContext context)
    {
        var result = context.Result as ObjectResult;
        if (result?.Value == null)
        {
            return;
        }

        //TODO 未完成

        var strValue = JsonConvert.SerializeObject(result.Value, GlobalConst.DefaultResponseJsonSerializerSettings);
        var newObj = JObject.Parse(strValue);
        //var newObj = JArray.Parse(strValue);
        //var bb = newObj.SelectTokens();

        context.Result = new ObjectResult(newObj);
    }
}

#endregion