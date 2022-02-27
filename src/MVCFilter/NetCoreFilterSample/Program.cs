using Microsoft.OpenApi.Models;
using NetCoreFilterSample.CustomFilter;
using NetCoreFilterSample.Filter;
using System.Reflection;

/*
������ʾ����ʾ��Ŀ

����˳��:��Ȩ������=>��Դ������=>Action������=>�쳣������=>Result������
 */

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers(option =>
{
    //���ȫ�ֹ�����
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
        Description = "������ʾ��",
    });

    var xmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Assembly.GetExecutingAssembly().GetName().Name + ".xml");
    option.IncludeXmlComments(xmlPath, true);
});

//builder.Services.AddMemoryCache();

#region ServiceFilterAttributeģʽ��Ҫ����

//��Ȩ������
builder.Services.AddScoped(typeof(Authorization_01Filter));
builder.Services.AddScoped(typeof(Authorization_01AsyncFilter));
builder.Services.AddScoped(typeof(Authonization_2Filter));

//��Դ������
builder.Services.AddScoped(typeof(Resource_01Filter));
builder.Services.AddScoped(typeof(Resource_02Filter));

//action������
builder.Services.AddScoped(typeof(Action_01Filter));
builder.Services.AddScoped(typeof(Action_02Filter));
builder.Services.AddScoped(typeof(Action_03Filter));

//�첽������
builder.Services.AddScoped(typeof(Exception_01Filter));
builder.Services.AddScoped(typeof(Exception_02Filter));

//Result������
builder.Services.AddScoped(typeof(Result_01Filter));
builder.Services.AddScoped(typeof(Result_02Filter));
builder.Services.AddScoped(typeof(Result_03Filter));

//�Զ��������
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

////��ȡ���������ÿ����ظ���ȡ
//app.Use((context, next) =>
// {
//     context.Request.EnableBuffering();
//     return next(context);
// });


app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();