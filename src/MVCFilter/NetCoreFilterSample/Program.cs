using Microsoft.OpenApi.Models;
using NetCoreFilterSample.CustomFilter;
using NetCoreFilterSample.Filter;
using System.Reflection;

/*
过滤器示例演示项目

流程顺序:授权过滤器=>资源过滤器=>Action过滤器=>异常过滤器=>Result过滤器
 */

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers(option =>
{
    //添加全局过滤器
    //option.Filters.Add(typeof(CustomExceptionFilter));
    //option.Filters.Add(typeof(CustomResultPackFilter));
    //option.Filters.Add(typeof(ModelValidationFilter));
    //option.Filters.Add(typeof(RequestParamRecordFilter));
    //option.Filters.Add(typeof(CustomResultAnonymityFilter));
    //option.Filters.Add(typeof(IdempotentAttributeFilter));

});
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(option =>
{
    option.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "API",
        Version = "v1",
        Description = "过滤器示例",
    });

    var xmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Assembly.GetExecutingAssembly().GetName().Name + ".xml");
    option.IncludeXmlComments(xmlPath, true);
});

//builder.Services.AddMemoryCache();

#region ServiceFilterAttribute模式需要配置

//授权过滤器
builder.Services.AddScoped(typeof(Authorization_01Filter));
builder.Services.AddScoped(typeof(Authorization_01AsyncFilter));
builder.Services.AddScoped(typeof(Authonization_2Filter));

//资源过滤器
builder.Services.AddScoped(typeof(Resource_01Filter));
builder.Services.AddScoped(typeof(Resource_02Filter));

//action过滤器
builder.Services.AddScoped(typeof(Action_01Filter));
builder.Services.AddScoped(typeof(Action_02Filter));
builder.Services.AddScoped(typeof(Action_03Filter));

//异步过滤器
builder.Services.AddScoped(typeof(Exception_01Filter));
builder.Services.AddScoped(typeof(Exception_02Filter));

//Result过滤器
builder.Services.AddScoped(typeof(Result_01Filter));
builder.Services.AddScoped(typeof(Result_02Filter));
builder.Services.AddScoped(typeof(Result_03Filter));

//自定义过滤器
//builder.Services.AddScoped<CustomExceptionFilter>();
//builder.Services.AddScoped<ModelValidationFilter>();

#endregion

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

////读取请求体设置可以重复读取
//app.Use((context, next) =>
// {
//     context.Request.EnableBuffering();
//     return next(context);
// });


app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();