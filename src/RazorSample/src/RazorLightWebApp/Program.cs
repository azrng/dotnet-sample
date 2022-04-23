using RazorLight;
using RazorLightWebApp.Utils;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var docRazorEngion = new RazorLightEngineBuilder()
    .UseEmbeddedResourcesProject(typeof(Program))
    .SetOperatingAssembly(typeof(Program).Assembly)
    .UseMemoryCachingProvider()
    .DisableEncoding()  //���ñ��� ���������ַ����ᱻ�����Unicode
    .Build();
builder.Services.AddSingleton<IDocumentGeneration>(_ => new DocumentGeneration(docRazorEngion));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
